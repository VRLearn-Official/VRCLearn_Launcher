using VRCLearn.App.Services;
using VRCLearn.Plugin.Sdk;

namespace VRCLearn.App.Plugins;

public sealed class PluginDialogHostService(DialogService dialogs) : IPluginDialogHost
{
    public PluginDialogChoice ShowChoice(
        string title,
        string message,
        string confirmText,
        string? extraText = null) =>
        dialogs.ShowChoice(title, message, confirmText, extraText, owner: PluginWindowOwner.Active()) switch
        {
            ConfirmChoice.Extra => PluginDialogChoice.Extra,
            ConfirmChoice.Confirm => PluginDialogChoice.Confirm,
            _ => PluginDialogChoice.Cancel,
        };
}
