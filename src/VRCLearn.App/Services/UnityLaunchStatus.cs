using VRCLearn.Core.Unity;

namespace VRCLearn.App.Services;

public static class UnityLaunchStatus
{
    public static string For(
        UnityOpenOutcome outcome,
        LocalizationService localization,
        string projectName,
        string unityVersion) =>
        outcome.Result switch
        {
            UnityOpenResult.AlreadyOpen =>
                localization.Format("Projects.Status.UnityAlreadyOpen", projectName),
            UnityOpenResult.NoEditor =>
                localization.Format("Projects.Status.UnityNotFound", unityVersion),
            UnityOpenResult.Timeout =>
                localization.Format("Projects.Status.UnityTimeout", projectName),
            UnityOpenResult.Launched when !outcome.Hub.IsUsable() =>
                localization[$"Unity.Hub.{outcome.Hub}"],
            UnityOpenResult.Launched =>
                localization.Format("Projects.Status.UnityLaunched", projectName),
            _ =>
                localization.Format("Projects.Status.UnityFailed", projectName, outcome.Detail ?? ""),
        };
}
