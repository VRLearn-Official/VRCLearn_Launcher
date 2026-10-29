using System.Windows;
using VRCLearn.Plugin.Sdk;

namespace VRCLearn.Plugin.Template;

public sealed class TemplatePlugin : IVclPlugin
{
    private IPluginContext? _context;

    public void Initialize(IPluginContext context) => _context = context;

    public FrameworkElement CreateView() => new TemplateView(_context!);
}
