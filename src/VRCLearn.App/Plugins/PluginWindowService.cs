using System.Windows;
using System.Windows.Threading;
using VRCLearn.App.Views;
using VRCLearn.Core.Plugins;

namespace VRCLearn.App.Plugins;

public sealed class PluginWindowService
{
    private readonly Dictionary<string, PluginWindow> _windows = [];

    public bool Activate(string id)
    {
        if (!_windows.TryGetValue(id, out var window))
        {
            return false;
        }

        window.Activate();

        return true;
    }

    public void Show(string id, string title, FrameworkElement content, PluginWindowOptions options)
    {
        if (_windows.TryGetValue(id, out var existing))
        {
            existing.Activate();
            return;
        }

        var window = new PluginWindow(title, content, options);

        if (Application.Current.MainWindow is { IsVisible: true } main)
        {
            window.Owner = main;
        }

        window.Closing += (_, _) =>
        {
            if (_windows.Count == 1)
            {
                ActivateMain();
            }
        };

        window.Closed += (_, _) =>
        {
            _windows.Remove(id);

            if (_windows.Count == 0)
            {
                ActivateMain();
                ActivateMain(window.Dispatcher);
            }
        };

        _windows[id] = window;
        window.Show();
    }

    private static void ActivateMain(Dispatcher dispatcher) =>
        dispatcher.BeginInvoke(ActivateMain, DispatcherPriority.ApplicationIdle);

    private static void ActivateMain()
    {
        if (Application.Current.MainWindow is { IsVisible: true, IsActive: false } main)
        {
            main.Activate();
        }
    }

    public void Close(string id)
    {
        if (_windows.Remove(id, out var window))
        {
            window.Close();
        }
    }

    public void CloseAll()
    {
        foreach (var window in _windows.Values.ToList())
        {
            window.Close();
        }

        _windows.Clear();
    }

    public void UpdateTitle(string id, string title)
    {
        if (_windows.TryGetValue(id, out var window))
        {
            window.UpdateTitle(title);
        }
    }
}
