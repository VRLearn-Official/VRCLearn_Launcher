using System.Windows;
using VRCLearn.App.Views;

namespace VRCLearn.App.Plugins;

internal static class PluginWindowOwner
{
    public static Window? Active() =>
        Application.Current.Windows.OfType<PluginWindow>().FirstOrDefault(x => x.IsActive)
        ?? Application.Current.Windows.OfType<PluginWindow>().FirstOrDefault(x => x.IsVisible)
        ?? Application.Current.MainWindow;
}
