namespace VRCLearn.App.ViewModels;

public sealed class ToastMessage(string text)
{
    public string Text { get; } = text;
}
