namespace VRCLearn.Plugin.Sdk;

public interface IPluginDialogHost
{
    PluginDialogChoice ShowChoice(
        string title,
        string message,
        string confirmText,
        string? extraText = null);
}

public enum PluginDialogChoice
{
    Cancel,

    Extra,

    Confirm,
}
