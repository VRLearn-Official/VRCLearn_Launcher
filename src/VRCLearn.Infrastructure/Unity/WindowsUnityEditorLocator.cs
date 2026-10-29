using VRCLearn.Core.Unity;
using VRCLearn.Core.Vpm;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.Infrastructure.Unity;

public sealed class WindowsUnityEditorLocator(
    ILauncherSettingsStore settings,
    GuiConfigStore configStore,
    IUnityHubLauncher hub) : IUnityEditorLocator
{
    private const string DefaultEditorFolder = @"Unity\Hub\Editor";
    private const string EditorExecutable = @"Editor\Unity.exe";

    private volatile IReadOnlyList<UnityEditor> _fromHub = [];

    private bool UsesHub => configStore.Current.UnityHubAccessMethod == "call-hub";

    public IReadOnlyList<UnityEditor> FindEditors()
    {
        var scanned = new List<UnityEditor>();

        foreach (var folder in InstallFolders())
        {
            scanned.AddRange(ScanFolder(folder));
        }

        foreach (var executable in StandaloneExecutables(ProgramFiles()))
        {
            if (Read(executable) is { } editor)
            {
                scanned.Add(editor);
            }
        }

        return UnityEditorList.Merge(ManualEditors(), scanned, [.. ReadRecordedEditors(), .. _fromHub]);
    }

    public async Task RefreshFromHubAsync(CancellationToken cancellationToken)
    {
        if (!UsesHub)
        {
            return;
        }

        _fromHub = await hub.ListEditorsAsync(cancellationToken).ConfigureAwait(false);
    }

    public UnityEditor? Probe(string executablePath) => Read(executablePath);

    private IEnumerable<UnityEditor> ManualEditors() =>
        settings.Current.UnityEditors.Select(Read).OfType<UnityEditor>();

    private static UnityEditor? Read(string executablePath) =>
        File.Exists(executablePath) && PeImage.ReadUnityVersion(executablePath) is { } version
            ? new UnityEditor(version, executablePath, PeImage.ReadArchitecture(executablePath))
            : null;

    private static IEnumerable<string> InstallFolders()
    {
        var defaultFolder = Path.Combine(ProgramFiles(), DefaultEditorFolder);

        yield return defaultFolder;

        if (SecondaryInstallPath() is { } custom && !string.Equals(custom, defaultFolder, StringComparison.OrdinalIgnoreCase))
        {
            yield return custom;
        }
    }

    private static string ProgramFiles() =>
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    internal static IEnumerable<string> StandaloneExecutables(string programFiles)
    {
        yield return Path.Combine(programFiles, "Unity", EditorExecutable);

        const string prefix = "Unity ";

        foreach (var directory in Directories(programFiles))
        {
            var name = Path.GetFileName(directory);

            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !UnityEditorFolderName.IsValid(name[prefix.Length..]))
            {
                continue;
            }

            yield return Path.Combine(directory, EditorExecutable);
        }
    }

    private static string? SecondaryInstallPath() =>
        UnityHubLocalSettings.ParseSecondaryInstallPath(ReadSettingFile("secondaryInstallPath.json"))
        ?? UnityHubLocalSettings.ParseMachineWideInstallLocation(ReadSettingFile("settings.json"))
        ?? UnityHubLocalSettings.ParseMachineWideInstallLocation(ReadSettingFile("Settings"))
        ?? UnityHubLocalSettings.ParseMachineWideInstallLocation(
            ReadFile(Path.Combine(GlobalConfigFolder(), "services-config.json")));

    private static IReadOnlyList<UnityEditor> ReadRecordedEditors() => UnityHubLocalSettings.ParseEditors(ReadSettingFile("editors-v2.json"));

    private static IEnumerable<UnityEditor> ScanFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            yield break;
        }

        foreach (var directory in Directories(folder))
        {
            if (!UnityEditorFolderName.IsValid(Path.GetFileName(directory)))
            {
                continue;
            }

            if (Read(Path.Combine(directory, EditorExecutable)) is { } editor)
            {
                yield return editor;
            }
        }
    }

    private static IReadOnlyList<string> Directories(string folder)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(folder)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string UserDataFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnityHub");

    private static string GlobalConfigFolder() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Unity",
            "config");

    private static string? ReadSettingFile(string name) => ReadFile(Path.Combine(UserDataFolder(), name));

    private static string? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
