using System.IO;
using System.Threading.Channels;
using System.Windows;
using Microsoft.Extensions.Logging;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Unity;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.Services;

public sealed class AutoBackupService : IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(20);

    private readonly GuiConfigStore _configs;
    private readonly ProjectService _projects;
    private readonly IUnityLauncher _launcher;
    private readonly ProjectBackupRunner _runner;
    private readonly IBackupStore _store;
    private readonly LocalizationService _localization;
    private readonly DialogService _dialogs;
    private readonly ILogger<AutoBackupService> _logger;

    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _idle = new(0);
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _cts = new();

    private Task? _worker;
    private long _lastRunTicks = DateTime.UtcNow.Ticks;
    private long _pausedAtTicks;
    private bool _abortRound;

    public AutoBackupService(
        GuiConfigStore configs,
        ProjectService projects,
        IUnityLauncher launcher,
        ProjectBackupRunner runner,
        IBackupStore store,
        LocalizationService localization,
        DialogService dialogs,
        ILogger<AutoBackupService> logger)
    {
        _configs = configs;
        _projects = projects;
        _launcher = launcher;
        _runner = runner;
        _store = store;
        _localization = localization;
        _dialogs = dialogs;
        _logger = logger;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_worker is not null)
            {
                return;
            }

            _worker = Task.Run(() => WorkAsync(_cts.Token));
            _ = Task.Run(() => ScheduleAsync(_cts.Token));
        }
    }

    public void RestartSchedule()
    {
        Interlocked.Exchange(ref _lastRunTicks, DateTime.UtcNow.Ticks);

        if (_configs.Current.AutoBackupEnabled)
        {
            ClearFailures();
        }
    }

    public void ProjectStarted()
    {
        if (_configs.Current.AutoBackupEnabled)
        {
            RestartSchedule();
        }
    }

    public TimeSpan? TimeUntilNextBackup()
    {
        var config = _configs.Current;

        if (!config.AutoBackupEnabled)
        {
            return null;
        }

        var interval = IntervalOf(config);
        var last = new DateTime(Interlocked.Read(ref _lastRunTicks), DateTimeKind.Utc);
        var pausedAt = Interlocked.Read(ref _pausedAtTicks);
        var now = pausedAt == 0 ? DateTime.UtcNow : new DateTime(pausedAt, DateTimeKind.Utc);
        var remaining = interval - (now - last);

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public bool HasFailed(string projectPath)
    {
        lock (_gate)
        {
            return _failed.Contains(projectPath);
        }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        Start();
        EnqueueRunningProjects();

        while (true)
        {
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    return;
                }
            }

            await _idle.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _queue.Writer.TryComplete();
    }

    private async Task ScheduleAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(Tick);

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (Interlocked.Read(ref _pausedAtTicks) != 0)
                {
                    continue;
                }

                var config = _configs.Current;

                if (!config.AutoBackupEnabled)
                {
                    RestartSchedule();
                    continue;
                }

                var interval = IntervalOf(config);
                var last = new DateTime(Interlocked.Read(ref _lastRunTicks), DateTimeKind.Utc);

                if (DateTime.UtcNow - last < interval)
                {
                    continue;
                }

                if (!EnqueueRunningProjects())
                {
                    RestartSchedule();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task WorkAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var path in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    if (!IsAborted() && _launcher.IsRunning(path))
                    {
                        await BackupOneAsync(path, cancellationToken).ConfigureAwait(false);
                        ClearFailure(path);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (InsufficientDiskSpaceException exception)
                {
                    MarkFailed(path);
                    _logger.LogWarning(exception, "自动备份失败：{Project}", path);

                    Fail(_localization.Format(
                        "AutoBackup.FailedReason.Disk",
                        SizeText.Format(exception.RequiredBytes),
                        SizeText.Format(exception.AvailableBytes)));
                }
                catch (Exception exception)
                {
                    MarkFailed(path);
                    _logger.LogWarning(exception, "自动备份失败：{Project}", path);

                    var detail = string.IsNullOrWhiteSpace(exception.Message)
                        ? exception.GetType().Name
                        : exception.Message;

                    Fail(_localization.Format("AutoBackup.FailedReason.Unknown", detail));
                }
                finally
                {
                    lock (_gate)
                    {
                        _pending.Remove(path);
                        FinishRoundIfIdle();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task BackupOneAsync(string path, CancellationToken cancellationToken)
    {
        var config = _configs.Current;
        var name = PathUtil.NameOf(path);

        if (name.Length == 0)
        {
            name = Path.GetFileName(path);
        }

        var record = await _runner.RunAsync(
            path,
            name,
            _localization["AutoBackup.Note"],
            auto: true,
            cancellationToken).ConfigureAwait(false);

        _store.PruneAutoBackups(name, Math.Max(1, config.AutoBackupRetention));

        _logger.LogInformation("自动备份完成：{Project} → {File}", name, record.FileName);
    }

    private void MarkFailed(string path)
    {
        lock (_gate)
        {
            _failed.Add(path);
        }
    }

    private void ClearFailure(string path)
    {
        lock (_gate)
        {
            _failed.Remove(path);
        }
    }

    private void FinishRoundIfIdle()
    {
        if (_pending.Count > 0)
        {
            return;
        }

        Interlocked.Exchange(ref _lastRunTicks, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref _pausedAtTicks, 0);
        _idle.Release();
    }

    private void Fail(string reason)
    {
        lock (_gate)
        {
            _abortRound = true;
        }

        var config = _configs.Current;
        config.AutoBackupEnabled = false;
        _configs.Save();

        var title = _localization["AutoBackup.FailedTitle"];
        var message = _localization.Format("AutoBackup.FailedMessage", reason);
        var close = _localization["Dialog.Confirm"];

        if (Application.Current?.Dispatcher is { } dispatcher)
        {
            dispatcher.Invoke(() => _dialogs.ShowNotice(title, message, close));
        }
    }

    private bool IsAborted()
    {
        lock (_gate)
        {
            return _abortRound;
        }
    }

    private void ClearFailures()
    {
        lock (_gate)
        {
            _failed.Clear();
        }
    }

    private static TimeSpan IntervalOf(GuiConfig config) =>
        TimeSpan.FromMinutes(Math.Max(15, config.AutoBackupIntervalMinutes));

    private bool EnqueueRunningProjects()
    {
        var queued = false;

        foreach (var path in _projects.List())
        {
            if (!_launcher.IsRunning(path))
            {
                continue;
            }

            lock (_gate)
            {
                if (!_pending.Add(path))
                {
                    continue;
                }

                if (_pending.Count == 1)
                {
                    _abortRound = false;
                    Interlocked.Exchange(ref _pausedAtTicks, DateTime.UtcNow.Ticks);
                }
            }

            if (_queue.Writer.TryWrite(path))
            {
                queued = true;
                continue;
            }

            lock (_gate)
            {
                _pending.Remove(path);
                FinishRoundIfIdle();
            }
        }

        return queued;
    }
}
