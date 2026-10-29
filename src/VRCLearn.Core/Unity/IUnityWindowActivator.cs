namespace VRCLearn.Core.Unity;

public enum BringUnityToFrontResult
{
    BroughtToFront,

    FailedToBringToFront,

    WindowNotFound,
}

public interface IUnityWindowActivator
{
    BringUnityToFrontResult BringToFront(string projectDirectory);
}
