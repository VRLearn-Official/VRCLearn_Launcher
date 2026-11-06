using Microsoft.Extensions.Logging;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Vpm;

namespace VRCLearn.Infrastructure.Storage;

public sealed class LauncherSettingsStore(AppPaths paths, ILogger<LauncherSettingsStore> logger)
    : ILauncherSettingsStore
{
    private readonly Lock _gate = new();
    private LauncherSettings? _current;
    private bool _readOnly;

    public event EventHandler? Changed;

    public LauncherSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current ??= JsonFileStore.Load<LauncherSettings>(
                    paths.SettingsFile,
                    LogCorrupt,
                    Unreadable);
            }
        }
    }

    public void Reload()
    {
        lock (_gate)
        {
            _current = null;
            _readOnly = false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Save()
    {
        bool saved;

        lock (_gate)
        {
            if (_readOnly)
            {
                return false;
            }

            saved = JsonFileStore.Save(paths.SettingsFile, Current, LogCorrupt);
        }

        if (saved)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return saved;
    }

    private void LogCorrupt(string message) => logger.LogWarning("{Message}", message);

    private void Unreadable(string message)
    {
        _readOnly = true;
        LogCorrupt(message);
    }
}
