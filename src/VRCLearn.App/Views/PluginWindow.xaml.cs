using System.Windows;
using VRCLearn.Core.Plugins;
using Wpf.Ui.Controls;

namespace VRCLearn.App.Views;

public partial class PluginWindow : FluentWindow
{
    public PluginWindow(string title, FrameworkElement content, PluginWindowOptions options)
    {
        InitializeComponent();

        Title = title;
        Width = options.Width;
        Height = options.Height;
        ResizeMode = options.Resizable ? ResizeMode.CanResize : ResizeMode.NoResize;
        Chrome.Content = content;

        Closed += (_, _) => Chrome.Content = null;
    }

    public void UpdateTitle(string title) => Title = title;
}
