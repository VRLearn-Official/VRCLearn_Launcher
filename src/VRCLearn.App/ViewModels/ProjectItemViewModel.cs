using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCLearn.App.Services;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Versioning;
using Wpf.Ui.Controls;

namespace VRCLearn.App.ViewModels;

public sealed partial class ProjectItemViewModel(string projectPath) : ObservableObject
{
    public string ProjectPath { get; } = projectPath;

    public string Name => Path.GetFileName(ProjectPath);

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private bool _isUnreadable;

    [ObservableProperty]
    private bool _isUnityRunning;

    [ObservableProperty]
    private bool _isAutoBackupActive;

    [ObservableProperty]
    private string _autoBackupText = "";

    [ObservableProperty]
    private bool _isAutoBackupFailed;

    public void SetAutoBackup(bool failed, string text)
    {
        IsAutoBackupFailed = failed;
        AutoBackupText = text;
        IsAutoBackupActive = text.Length > 0;
    }

    public bool IsFavorite { get; private set; }

    public string FavoriteAction { get; private set; } = "";

    public string UnityVersion { get; private set; } = "";

    public UnityVersion? Version { get; private set; }

    public ProjectType ProjectType { get; private set; } = ProjectType.Unknown;

    [ObservableProperty]
    private SymbolRegular _typeIcon = SymbolRegular.QuestionCircle24;

    [ObservableProperty]
    private string _metaLine = "";

    public DateTime CreatedAt { get; } = SafeTimestamp(projectPath, Directory.GetCreationTimeUtc);

    public DateTime LastModified { get; } = SafeTimestamp(projectPath, Directory.GetLastWriteTimeUtc);

    private static DateTime SafeTimestamp(string path, Func<string, DateTime> read)
    {
        try
        {
            return read(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private static string FormatTimestamp(DateTime value) =>
        value == DateTime.MinValue
            ? "—"
            : value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public async Task LoadAsync(
        ProjectService service,
        LocalizationService localization,
        bool isFavorite,
        bool isUnityRunning,
        CancellationToken cancellationToken)
    {
        IsFavorite = isFavorite;
        FavoriteAction = localization[isFavorite ? "Projects.Unfavorite" : "Projects.Favorite"];
        IsUnityRunning = isUnityRunning;

        try
        {
            var project = await service.OpenAsync(ProjectPath, cancellationToken);
            var installed = project.InstalledPackages.Count();

            Summary = localization.Format(
                installed > 0 ? "Projects.Summary" : "Projects.Summary.Empty",
                project.UnityVersion,
                installed);

            Version = project.UnityVersion;
            UnityVersion = project.UnityVersion.ToString();
            ProjectType = project.DetectProjectType();
            TypeIcon = IconFor(ProjectType);
            IsUnreadable = false;

            MetaLine = string.Join(
                " · ",
                localization[$"ProjectType.{ProjectType}"],
                UnityVersion.Length > 0 ? $"Unity {UnityVersion}" : localization["Projects.Meta.NoUnity"],
                localization.Format("Projects.Meta.Added", FormatTimestamp(CreatedAt)),
                localization.Format("Projects.Meta.Modified", FormatTimestamp(LastModified)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Summary = localization.Format("Projects.Unreadable", exception.Message);
            MetaLine = "";
            IsUnreadable = true;
        }
    }

    private static SymbolRegular IconFor(ProjectType type) => type switch
    {
        ProjectType.Avatars or ProjectType.LegacyAvatars or ProjectType.UpmAvatars => SymbolRegular.Person24,
        ProjectType.Worlds or ProjectType.LegacyWorlds or ProjectType.UpmWorlds => SymbolRegular.Globe24,
        _ => SymbolRegular.QuestionCircle24,
    };
}
