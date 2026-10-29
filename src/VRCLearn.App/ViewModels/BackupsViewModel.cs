using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Unity;
using VRCLearn.Core.Vpm;
using VRCLearn.Infrastructure.Shell;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.ViewModels;

public sealed partial class BackupsViewModel : PageViewModelBase, IPageViewModel
{
    private readonly IBackupStore _store;
    private readonly ProjectRestoreService _restores;
    private readonly ProjectService _projects;
    private readonly ILauncherSettingsStore _settings;
    private readonly IUnityLauncher _launcher;
    private readonly IUnityHubLauncher _hub;
    private readonly DialogService _dialogs;
    private readonly LocalizationService _localization;
    private readonly ILogger<BackupsViewModel> _logger;
    private readonly ProgressService _progress;
    private readonly GuiConfigStore _configs;
    private readonly IReadOnlyList<Option> _sortOptions;
    private readonly IReadOnlyList<Option> _filterOptions;

    private IReadOnlyList<BackupItemViewModel> _all = [];

    public BackupsViewModel(
        IBackupStore store,
        ProjectRestoreService restores,
        ProjectService projects,
        ILauncherSettingsStore settings,
        IUnityLauncher launcher,
        IUnityHubLauncher hub,
        DialogService dialogs,
        LocalizationService localization,
        ProgressService progress,
        GuiConfigStore configs,
        ToastService toasts,
        ILogger<BackupsViewModel> logger)
        : base(toasts)
    {
        _store = store;
        _restores = restores;
        _projects = projects;
        _settings = settings;
        _launcher = launcher;
        _hub = hub;
        _dialogs = dialogs;
        _localization = localization;
        _logger = logger;
        _progress = progress;
        _configs = configs;

        _sortOptions =
        [
            new("newest", () => localization["Backups.Sort.Newest"]),
            new("oldest", () => localization["Backups.Sort.Oldest"]),
        ];

        _filterOptions =
        [
            new("backup", () => localization["Backups.Filter.Backup"]),
            new("archive", () => localization["Backups.Filter.Archive"]),
        ];

        _selectedSort = configs.Current.BackupsSorting == "oldest" ? "oldest" : "newest";
        _selectedFilter = configs.Current.BackupsFilter == "archive" ? "archive" : "backup";

        _localization.PropertyChanged += (_, _) =>
        {
            LocalizedOptions.Refresh(_sortOptions);
            LocalizedOptions.Refresh(_filterOptions);
            Apply();
        };
    }

    public IReadOnlyList<Option> SortOptions => _sortOptions;

    public IReadOnlyList<Option> FilterOptions => _filterOptions;

    [ObservableProperty]
    private string _selectedSort = "newest";

    partial void OnSelectedSortChanged(string value)
    {
        _configs.Current.BackupsSorting = value;
        _configs.Save();

        Apply();
    }

    [ObservableProperty]
    private string _selectedFilter = "backup";

    partial void OnSelectedFilterChanged(string value)
    {
        _configs.Current.BackupsFilter = value;
        _configs.Save();

        Apply();
    }

    [ObservableProperty]
    private string _searchText = "";

    partial void OnSearchTextChanged(string value) => Apply();

    public ObservableCollection<BackupItemViewModel> Backups { get; } = [];

    public bool HasBackups => Backups.Count > 0;

    public string EmptyText => _localization[_all.Count == 0 ? "Backups.Empty" : "Backups.NoMatch"];

    protected override ILogger Logger => _logger;

    protected override LocalizationService Localization => _localization;

    public void Reload()
    {
        _all = [.. _store.List().Select(BackupItemViewModel.From)];

        Apply();
    }

    [RelayCommand]
    private void OpenDirectory()
    {
        Directory.CreateDirectory(_store.Directory);
        ShellLauncher.OpenFolder(_store.Directory);
    }

    [RelayCommand]
    private void ConfigureAutoBackup() => _dialogs.ShowAutoBackup();

    [RelayCommand]
    private async Task Import()
    {
        _dialogs.ShowNotice(
            _localization["Backups.Import.NoticeTitle"],
            _localization["Backups.Import.NoticeMessage"],
            _localization["Backups.Import.NoticeButton"]);

        var files = _dialogs.PickFiles(
            _localization["Backups.Import.PickTitle"],
            "*.zip|*.zip|*|*");

        if (files.Length > 0)
        {
            await ImportFilesAsync(files);
        }
    }

    public async Task ImportFilesAsync(IReadOnlyList<string> paths)
    {
        if (IsBusy || paths.Count == 0)
        {
            return;
        }

        var required = 0L;

        foreach (var path in paths)
        {
            if (!string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                if (File.Exists(path))
                {
                    required += new FileInfo(path).Length;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        try
        {
            DiskSpace.Ensure(_store.Directory, required);
        }
        catch (InsufficientDiskSpaceException exception)
        {
            Status = _localization.FormatDiskSpace(exception);

            return;
        }

        var imported = 0;
        var failed = 0;

        await WithBusyAsync(async () =>
        {
            foreach (var path in paths)
            {
                try
                {
                    if (!string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        failed++;
                        continue;
                    }

                    var info = ProjectBackup.DescribeImport(
                        Path.GetFileName(path),
                        File.GetLastWriteTime(path));

                    await using var source = File.OpenRead(path);

                    await _store.CreateAsync(
                        new BackupCreateRequest(info.ProjectName, info.CreatedAt, info.IsArchive, null),
                        (destination, token) => source.CopyToAsync(destination, token),
                        CancellationToken.None);

                    imported++;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogWarning(exception, "导入备份失败：{Path}", path);
                    failed++;
                }
            }

            Reload();
        });

        Status = failed == 0
            ? _localization.Format("Backups.Import.Done", imported)
            : imported == 0
                ? _localization["Backups.Import.None"]
                : _localization.Format("Backups.Import.Partial", imported, failed);
    }

    private void Apply()
    {
        var query = SearchText.Trim();
        var archive = SelectedFilter == "archive";

        var items = _all.Where(x =>
            x.IsArchive == archive
            && (query.Length == 0
                || x.ProjectName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || x.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)));

        items = SelectedSort == "oldest"
            ? items.OrderBy(x => x.Time)
            : items.OrderByDescending(x => x.Time);

        Backups.Clear();

        foreach (var item in items)
        {
            Backups.Add(item);
        }

        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(EmptyText));
    }

    [RelayCommand]
    private async Task Restore(BackupItemViewModel item)
    {
        if (IsBusy || item.IsMissing)
        {
            return;
        }

        if (!File.Exists(item.FilePath))
        {
            Status = _localization["Backups.FileMoved"];
            Reload();
            return;
        }

        var parent = _dialogs.PickFolder(
            _localization["Backups.Restore.PickTitle"],
            InitialDirectoryFor(item));

        if (parent is null)
        {
            return;
        }

        var name = item.ProjectName;

        if (!_dialogs.ShowConfirm(
                _localization["Backups.Restore.ConfirmTitle"],
                RestoreMessage(Path.Combine(parent, name), item),
                _localization["Backups.Restore.ConfirmButton"]))
        {
            return;
        }

        Status = _localization.Format("Backups.Status.Restoring", item.ProjectName);

        await RunBusyAsync(
            "还原备份失败：{Backup}",
            item.ProjectName,
            async () =>
            {
                var restored = await _progress.RunAsync(
                    _localization["Progress.Restore"],
                    reporter => _restores.ExtractNewAsync(
                        item.FilePath,
                        parent,
                        name,
                        CancellationToken.None,
                        reporter));

                _projects.Add(restored);
                Status = _localization.Format("Backups.Status.Restored", restored);
            },
            "Backups.Status.RestoreFailed");
    }

    [RelayCommand]
    private void EditNote(BackupItemViewModel item)
    {
        var note = _dialogs.ShowNote(
            _localization["Backups.Note.Title.Edit"],
            item.Note ?? "",
            _localization["Backups.Note.Save"],
            _localization["Backups.Note.Hint"]);

        if (note is null)
        {
            return;
        }

        _store.WriteNote(item.FilePath, note);
        Status = _localization["Backups.Status.NoteSaved"];

        Reload();
    }

    [RelayCommand]
    private void Lock(BackupItemViewModel item)
    {
        if (!item.IsAuto)
        {
            return;
        }

        if (!_dialogs.ShowConfirm(
                _localization["Backups.Lock.Title"],
                _localization.Format("Backups.Lock.Message", item.FileName),
                _localization["Backups.Lock.Confirm"]))
        {
            return;
        }

        _store.LockAutoBackup(item.FilePath);

        Status = _localization.Format("Backups.Status.Locked", item.FileName);

        Reload();
    }

    [RelayCommand]
    private void Delete(BackupItemViewModel item)
    {
        if (!_dialogs.ShowConfirm(
                _localization["Backups.Delete.ConfirmTitle"],
                _localization.Format("Backups.Delete.ConfirmMessage", item.FileName),
                _localization["Backups.Delete.ConfirmButton"],
                danger: true))
        {
            return;
        }

        _store.Delete(item.FilePath);

        Status = _localization.Format("Backups.Status.Deleted", item.FileName);

        Reload();
    }

    private string RestoreMessage(string target, BackupItemViewModel item)
    {
        var parts = new List<string> { _localization.Format("Backups.Restore.Target", target) };

        if (IsUnityBusy(item.ProjectName))
        {
            parts.Add(_localization["Backups.Restore.UnityRunning"]);
        }

        parts.Add(_localization["Backups.Restore.Caches"]);

        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    public void Release()
    {
        if (IsBusy)
        {
            return;
        }

        _all = [];
        Backups.Clear();
    }

    private bool IsUnityBusy(string projectName) =>
        _hub.IsRunning
        || _projects.List().Any(x =>
            string.Equals(Path.GetFileName(x), projectName, StringComparison.OrdinalIgnoreCase)
            && _launcher.IsRunning(x));

    private string InitialDirectoryFor(BackupItemViewModel item)
    {
        var known = _projects.List().FirstOrDefault(x =>
            string.Equals(PathUtil.NameOf(x), item.ProjectName, StringComparison.OrdinalIgnoreCase));

        if (known is null)
        {
            return _settings.Current.EffectiveProjectPath;
        }

        return PathUtil.ParentOf(known)
            ?? _settings.Current.EffectiveProjectPath;
    }
}

public sealed class BackupItemViewModel
{
    private BackupItemViewModel(BackupRecord record)
    {
        FilePath = record.FilePath;
        FileName = record.FileName;
        ProjectName = string.IsNullOrWhiteSpace(record.ProjectName)
            ? Path.GetFileNameWithoutExtension(record.FilePath)
            : record.ProjectName;
        Time = record.CreatedAt;
        Note = string.IsNullOrWhiteSpace(record.Note) ? null : record.Note;
        IsArchive = record.IsArchive;
        IsAuto = record.IsAuto;
        IsAvailable = File.Exists(record.FilePath);

        var file = IsAvailable ? new FileInfo(record.FilePath) : null;

        Display = Time == default
            ? FileName
            : Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        SizeText = file is null ? "" : Services.SizeText.Format(file.Length);
    }

    public static BackupItemViewModel From(BackupRecord record) => new(record);

    public string FilePath { get; }

    public string FileName { get; }

    public bool IsArchive { get; }

    public bool IsAuto { get; }

    public bool CanEditNote => !IsArchive && !IsAuto;

    public bool CanLock => IsAuto;

    public string ProjectName { get; }

    public DateTime Time { get; }

    public string? Note { get; }

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public bool IsAvailable { get; }

    public bool IsMissing => !IsAvailable;

    public double RowOpacity => IsMissing ? 0.5 : 1.0;

    public string Display { get; }

    public string SizeText { get; }
}
