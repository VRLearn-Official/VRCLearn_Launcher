using System.Windows;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;
using VRCLearn.Plugin.Sdk;

namespace VRCLearn.App.Plugins;

public sealed class SettingsHostService(
    NavigationService navigation,
    SettingsViewModel settings) : ISettingsHost
{
    public void OpenNetworkSettings()
    {
        settings.FocusProxy();
        navigation.GoTo("settings");
        Application.Current.MainWindow?.Activate();
    }
}
