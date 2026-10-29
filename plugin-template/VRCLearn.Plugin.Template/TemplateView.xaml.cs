using System.Windows;
using System.Windows.Controls;
using VRCLearn.Plugin.Sdk;

namespace VRCLearn.Plugin.Template;

public partial class TemplateView : UserControl
{
    private readonly IPluginContext _context;
    private int _count;

    public TemplateView(IPluginContext context)
    {
        InitializeComponent();

        _context = context;
        HintText.Text = $"{context.PluginId} {context.PluginVersion} on host {context.HostVersion}";
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        _count++;
        CountText.Text = $"Clicked {_count} times";
    }
}
