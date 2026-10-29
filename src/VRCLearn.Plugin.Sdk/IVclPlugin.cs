using System.Windows;

namespace VRCLearn.Plugin.Sdk;

public interface IVclPlugin
{
    void Initialize(IPluginContext context);

    FrameworkElement CreateView();
}
