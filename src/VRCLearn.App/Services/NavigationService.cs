namespace VRCLearn.App.Services;

public sealed class NavigationService
{
    public event Action<string>? Requested;

    public void GoTo(string page) => Requested?.Invoke(page);
}
