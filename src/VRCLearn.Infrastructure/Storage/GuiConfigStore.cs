using Microsoft.Extensions.Logging;
using VRCLearn.Core.Configuration;

namespace VRCLearn.Infrastructure.Storage;

public sealed class GuiConfigStore
{
    private readonly AppPaths _paths;
    private readonly ILogger<GuiConfigStore> _logger;
    private readonly Lock _gate = new();
    private GuiConfig? _current;
    private bool _readOnly;

    public GuiConfigStore(AppPaths paths, ILogger<GuiConfigStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public event EventHandler? Changed;

    public GuiConfig Current
    {
        get
        {
            lock (_gate)
            {
                return _current ??= JsonFileStore.Load<GuiConfig>(
                    _paths.GuiConfigFile,
                    Complain,
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

    public void Save()
    {
        lock (_gate)
        {
            var config = Current;

            if (_readOnly)
            {
                return;
            }

            JsonFileStore.Save(_paths.GuiConfigFile, config, Complain);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Complain(string message) => _logger.LogWarning("{Message}", message);

    private void Unreadable(string message)
    {
        _readOnly = true;
        Complain(message);
    }
}
