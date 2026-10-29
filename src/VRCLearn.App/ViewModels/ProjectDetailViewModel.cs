using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Hashing;
using VRCLearn.Core.IO;
using VRCLearn.Core.Packages;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Templates;
using VRCLearn.Core.Text;
using VRCLearn.Core.Unity;
using VRCLearn.Core.Versioning;
using VRCLearn.Core.Vpm;
using VRCLearn.Infrastructure.Shell;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.ViewModels;

public sealed partial class ProjectDetailViewModel(
    ProjectService projects,
    RepositoryService repositories,
    IPackageInstaller installer,
    ProjectBackupRunner backupRunner,
    ProjectRestoreService restores,
    ProjectCopyService copies,
    UserPackageService userPackages,
    IBackupStore backupStore,
    IUserTemplateStore userTemplates,
    IFileIoFactory fileIo,
    GuiConfigStore configStore,
    IUnityLauncher launcher,
    UnityProjectOpener opener,
    ILauncherSettingsStore settings,
    NavigationService navigation,
    LocalizationService localization,
    DialogService dialogs,
    ProgressService progress,
    ToastService toasts,
    ILogger<ProjectDetailViewModel> logger) : PageViewModelBase(toasts), IPageViewModel
{
    private const int SearchLimit = 100;

    private const int SearchDelayMilliseconds = 250;

    private UnityProject? _project;
    private readonly Dictionary<string, PackageInfo> _requestedAdds = [];

    private PendingProjectChanges? _pending;

    private string _pendingFingerprint = "";


    private bool _resolveAfterLoad;

    private PackageCatalog? _catalog;
    private PackageCatalog? _offeredCatalog;
    private TransientAction? _search;
    private readonly HashSet<string> _customPackages = new(StringComparer.OrdinalIgnoreCase);
    private bool _catalogComplete;

    public string ProjectPath { get; private set; } = "";

    public string ProjectName => Path.GetFileName(ProjectPath);

    public string HeaderSubtitle => _project is null ? ProjectPath : $"{TypeName(ProjectType)} · {ProjectPath}";

    // ---- 用哪个 Unity 打开 ----

    public ObservableCollection<UnityEditorOption> InstalledEditors { get; } = [];

    [ObservableProperty]
    private UnityEditorOption? _selectedEditor;

    public bool HasEditors => InstalledEditors.Count > 0;

    public string EditorCaption => localization["ProjectDetail.UnityEditor"];

    /// <summary>记住用户按 Unity 大版本选择的编辑器。</summary>
    partial void OnSelectedEditorChanged(UnityEditorOption? value)
    {
        if (_reloadingEditors || value is null || _project is not { } project)
        {
            return;
        }

        if (value.Editor.Version > project.UnityVersion)
        {
            WarnHigherUnity(value.Editor.Version, project.UnityVersion);
            return;
        }

        if (!value.Editor.MatchesProject(project.UnityVersion))
        {
            return;
        }

        settings.Current.PreferredUnityEditors[MajorOf(project)] = value.Path;
        settings.Save();

        logger.LogInformation("项目使用的 Unity 编辑器：{Editor}", value.Path);
    }

    private void WarnHigherUnity(UnityVersion selected, UnityVersion current)
    {
        var choice = dialogs.ShowChoice(
            localization["ProjectDetail.UnityUpgrade.Title"],
            localization.Format("ProjectDetail.UnityUpgrade.Message", selected, current),
            localization["ProjectDetail.UnityUpgrade.Ok"],
            localization["ProjectDetail.UnityUpgrade.Backup"]);

        if (choice == ConfirmChoice.Extra)
        {
            BackupCommand.Execute(null);
        }
    }

    protected override ILogger Logger => logger;

    protected override LocalizationService Localization => localization;

    private static string MajorOf(UnityProject project) => project.UnityVersion.Major.ToString();

    private void ReloadEditors()
    {
        _reloadingEditors = true;

        try
        {
            InstalledEditors.Clear();

            if (_project is { } project)
            {
                var installed = opener.AllEditors();

                var chosen = UnityEditorList.PickForProject(
                    installed,
                    project.UnityVersion,
                    settings.Current.PreferredUnityEditors);

                foreach (var editor in installed)
                {
                    InstalledEditors.Add(new UnityEditorOption(editor));
                }

                SelectedEditor = InstalledEditors.FirstOrDefault(x => x.Path == chosen?.ExecutablePath);
            }

            SelectedEditor ??= InstalledEditors.FirstOrDefault();
        }
        finally
        {
            _reloadingEditors = false;
        }

        OnPropertyChanged(nameof(HasEditors));
    }

    [ObservableProperty]
    private string _searchText = "";

    public ObservableCollection<InstalledPackageViewModel> Installed { get; } = [];

    public ObservableCollection<AvailablePackageViewModel> Available { get; } = [];

    public ObservableCollection<PackageChangeViewModel> PendingChanges { get; } = [];

    public ObservableCollection<string> PendingConflicts { get; } = [];

    public bool HasPendingChanges => PendingChanges.Count > 0;

    public bool IsSearchEmpty => string.IsNullOrWhiteSpace(SearchText);

    public bool NeedsResolve => _project?.ShouldResolve(_customPackages) ?? false;

    public ProjectType ProjectType { get; private set; }

    [ObservableProperty]
    private string? _copyDestination;

    partial void OnCopyDestinationChanged(string? value) => OnPropertyChanged(nameof(HasPendingCopy));

    public bool HasPendingCopy => CopyDestination is not null;

    public ObservableCollection<BackupItemViewModel> Backups { get; } = [];

    public bool HasBackups => Backups.Count > 0;

    [ObservableProperty]
    private BackupItemViewModel? _pendingRestore;

    partial void OnPendingRestoreChanged(BackupItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasPendingRestore));
        OnPropertyChanged(nameof(RestorePrompt));
    }

    public bool HasPendingRestore => PendingRestore is not null;

    public string RestorePrompt => PendingRestore is { } entry
        ? localization.Format("ProjectDetail.Restore.ConfirmPrompt", entry.Display)
        : "";

    public bool IsUnityRunning { get; private set; }

    public void RefreshUnityState() =>
        SetUnityRunning(launcher.Status(ProjectPath) != UnityProjectStatus.Closed);

    private void SetUnityRunning(bool value)
    {
        if (IsUnityRunning == value)
        {
            return;
        }

        IsUnityRunning = value;
        OnPropertyChanged(nameof(IsUnityRunning));
    }

    [ObservableProperty]
    private string? _unityHubInstallLink;

    partial void OnUnityHubInstallLinkChanged(string? value) =>
        OnPropertyChanged(nameof(HasUnityHubInstallLink));

    public bool HasUnityHubInstallLink => UnityHubInstallLink is not null;

    [RelayCommand]
    private void InstallInUnityHub()
    {
        if (UnityHubInstallLink is { } link)
        {
            ShellLauncher.OpenUrl(link);
        }
    }


    private IReadOnlyList<PackageInfo> _userPackages = [];

    private bool _allowPrerelease;

    /// <summary>一行一条参数；留空用全局设置。</summary>
    [ObservableProperty]
    private string _unityArgumentsText = "";

    partial void OnUnityArgumentsTextChanged(string value)
    {
        if (_reloadingUnityArguments)
        {
            return;
        }

        var arguments = SplitArguments(value);
        var config = configStore.Current;

        if (arguments.Count == 0)
        {
            config.ProjectUnityArguments.Remove(ProjectPath);
        }
        else
        {
            config.ProjectUnityArguments[ProjectPath] = arguments;
        }

        configStore.Save();
    }

    private bool _reloadingUnityArguments;

    private void ReloadUnityArguments()
    {
        var config = configStore.Current;

        _reloadingUnityArguments = true;

        try
        {
            UnityArgumentsText = config.ProjectUnityArguments.TryGetValue(ProjectPath, out var saved)
                ? string.Join(Environment.NewLine, saved)
                : "";
        }
        finally
        {
            _reloadingUnityArguments = false;
        }
    }

    private static List<string> SplitArguments(string text) =>
        [.. text.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0)];

    private bool _reloadingEditors;

    private PackageCatalog Catalog()
    {
        if (_catalog is { } cached)
        {
            return cached;
        }

        var cachedRepositories = repositories.CachedRepositories();
        _catalogComplete = cachedRepositories.Count == repositories.List().Count;

        return _catalog = PackageCatalog.FromRepositories(cachedRepositories, _userPackages);
    }

    /// <summary>「可以装什么」用这份；隐藏的仓库不计入。</summary>
    private PackageCatalog OfferedCatalog()
    {
        if (_offeredCatalog is { } cached)
        {
            return cached;
        }

        var config = configStore.Current;
        var hidden = config.HiddenRepositories;

        return _offeredCatalog = PackageCatalog.FromRepositories(
            [.. repositories.CachedRepositories().Where(x => x.Id is null || !hidden.Contains(x.Id))],
            config.HideLocalUserPackages ? [] : _userPackages);
    }

    private void ForgetCatalogs()
    {
        _catalog = null;
        _offeredCatalog = null;
        _catalogComplete = false;
    }

    public void Release()
    {
        if (IsBusy)
        {
            return;
        }

        _project = null;
        _pending = null;
        _userPackages = [];
        _customPackages.Clear();
        ForgetCatalogs();

        Installed.Clear();
        Available.Clear();
        PendingChanges.Clear();
        PendingConflicts.Clear();
        Backups.Clear();
        InstalledEditors.Clear();
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(NeedsResolve));
    }

    private string Join(IEnumerable<string> names) =>
        string.Join(localization["List.Separator"], names);

    private static IEnumerable<string> Describe(IEnumerable<MissingDependency> missing) =>
        missing.Select(x => $"{x.Name} {x.Range}");

    private string ReportMissingDependencies(IEnumerable<MissingDependency> missing)
    {
        var text = Join(Describe(missing));

        Status = localization.Format("ProjectDetail.MissingDependencies", text);

        return text;
    }

    /// <param name="resolveAfterLoad">刚建出来的项目，装载完接着把清单里的依赖装上。</param>
    public void Open(string projectPath, bool resolveAfterLoad = false)
    {
        ProjectPath = projectPath;
        _project = null;
        _pending = null;
        _pendingFingerprint = "";
        _resolveAfterLoad = resolveAfterLoad;
        _customPackages.Clear();
        ForgetCatalogs();

        Installed.Clear();
        Available.Clear();
        Backups.Clear();
        InstalledEditors.Clear();
        SelectedEditor = null;
        PendingRestore = null;
        ClearPending();
        Status = null;
        SearchText = "";
        ProjectType = ProjectType.Unknown;

        OnPropertyChanged(nameof(ProjectPath));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(NeedsResolve));
    }

    public async Task LoadAsync()
    {
        IsBusy = true;

        try
        {
            _project = await projects.OpenAsync(ProjectPath, CancellationToken.None);
            _userPackages = await userPackages.LoadPackagesAsync(CancellationToken.None);
            ForgetCatalogs();
            _allowPrerelease = settings.Current.ShowPrereleasePackages;
            ReloadUnityArguments();
            ProjectType = _project.DetectProjectType();
            OnPropertyChanged(nameof(HeaderSubtitle));

            ReloadEditors();
            ReloadInstalled();
            ReloadAvailable();
            ReloadBackups();

            RefreshUnityState();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "读取项目失败：{Project}", ProjectPath);

            _project = null;
            _customPackages.Clear();
            OnPropertyChanged(nameof(HeaderSubtitle));
            Status = localization.Format("ProjectDetail.Unreadable", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }

        OnPropertyChanged(nameof(NeedsResolve));

        if (_resolveAfterLoad && _project is not null)
        {
            _resolveAfterLoad = false;

            await ResolveAndApplyAsync();
        }
    }

    [RelayCommand]
    private void OpenFolder() => ShellLauncher.OpenFolder(ProjectPath);

    /// <summary>按当前项目记一个模板：名字、Unity 版本、已装的包都照抄。</summary>
    [RelayCommand]
    private async Task CreateTemplate()
    {
        if (_project is null
            || !dialogs.ShowConfirm(
                localization["ProjectDetail.CreateTemplate"],
                localization.Format("ProjectDetail.Template.ConfirmMessage", ProjectName),
                localization["ProjectDetail.Template.ConfirmButton"]))
        {
            return;
        }

        var unityVersion = _project.UnityVersion;

        var template = new AlcomTemplate(
            ProjectName,
            TemplateFormat.NewAnonymousId(),
            TemplateFormat.BlankId,
            $"{unityVersion.Major}.{unityVersion.Minor}.{unityVersion.Revision}",
            _project.InstalledPackages.ToDictionary(x => x.Key, x => x.Value.Version.ToString()),
            [],
            DateTimeOffset.Now);

        try
        {
            await userTemplates.SaveAsync(template, CancellationToken.None);
            Status = localization.Format("ProjectDetail.Template.Saved", ProjectName);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "存档为模板失败：{Project}", ProjectPath);

            Status = localization.Format("ProjectDetail.Template.Failed", exception.Message);
        }
    }

    [RelayCommand]
    private async Task OpenInUnity()
    {
        if (_project is null)
        {
            return;
        }

        UnityHubInstallLink = null;
        Status = localization["Unity.StartingHub"];

        var outcome = await opener.OpenAsync(
            ProjectPath,
            SelectedEditor?.Editor,
            opener.ArgumentsFor(ProjectPath),
            CancellationToken.None);

        if (outcome.Result == UnityOpenResult.NoEditor)
        {
            UnityHubInstallLink = UnityHubLink.ForInstall(_project.UnityVersion, _project.UnityRevision);
        }

        Status = UnityLaunchStatus.For(outcome, localization, ProjectName, _project.UnityVersion.ToString());

        RefreshUnityState();
    }

    [RelayCommand]
    private void BringUnityToFront()
    {
        if (_project is null)
        {
            return;
        }

        var result = opener.BringToFront(ProjectPath);

        Status = localization[$"Unity.BringToFront.{result}"];

        if (result == BringUnityToFrontResult.WindowNotFound)
        {
            RefreshUnityState();
        }
    }

    [RelayCommand]
    private void Back() => navigation.GoTo("projects");

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    [RelayCommand]
    private async Task Backup()
    {
        if (_project is null)
        {
            return;
        }

        var note = dialogs.ShowNote(
            localization["Backups.Note.Title.New"],
            "",
            localization["Backups.Note.Start"],
            localization["Backups.Note.Hint"]);

        if (note is null)
        {
            return;
        }

        await RunBusyAsync(
            "备份项目失败：{Project}",
            ProjectName,
            async () =>
            {
                var record = await backupRunner.RunAsync(
                    ProjectPath,
                    ProjectName,
                    note,
                    auto: false,
                    CancellationToken.None);

                ReloadBackups();
                Status = localization.Format("ProjectDetail.BackupDone", record.FileName);
            },
            "ProjectDetail.BackupFailed");
    }

    [RelayCommand]
    private void Restore(BackupItemViewModel entry)
    {
        if (_project is null || entry.IsMissing)
        {
            return;
        }

        if (UnityIsRunning())
        {
            return;
        }

        Status = null;
        PendingRestore = entry;
    }

    private bool UnityIsRunning()
    {
        if (!launcher.IsRunning(ProjectPath))
        {
            return false;
        }

        logger.LogWarning("Unity 正在运行，无法还原项目：{Project}", ProjectPath);

        Status = localization["ProjectDetail.Restore.UnityRunning"];
        return true;
    }

    [RelayCommand]
    private void CancelRestore() => PendingRestore = null;

    [RelayCommand]
    private async Task ConfirmRestore()
    {
        if (_project is null || PendingRestore is not { } entry)
        {
            return;
        }

        if (entry.IsMissing || !File.Exists(entry.FilePath))
        {
            PendingRestore = null;
            Status = localization["Backups.FileMoved"];
            ReloadBackups();
            return;
        }

        if (UnityIsRunning())
        {
            PendingRestore = null;
            return;
        }

        IsBusy = true;
        Status = localization["ProjectDetail.Restore.Working"];

        try
        {
            var config = configStore.Current;

            var outcome = await progress.RunAsync(
                localization["Progress.Restore"],
                reporter => restores.RestoreAsync(
                    _project,
                    ProjectName,
                    entry.FilePath,
                    ProjectBackupService.ParseFormat(config.BackupFormat),
                    DateTime.Now,
                    reporter,
                    CancellationToken.None));

            PendingRestore = null;

            await LoadAsync();

            Status = outcome.LeftoverPath is null
                ? localization.Format("ProjectDetail.Restore.Done", Path.GetFileName(outcome.SafetyBackup))
                : localization.Format(
                    "ProjectDetail.Restore.DoneWithLeftover",
                    Path.GetFileName(outcome.SafetyBackup),
                    outcome.LeftoverPath);
        }
        catch (InsufficientDiskSpaceException exception)
        {
            logger.LogWarning(exception, "还原项目失败：{Project}", ProjectPath);

            PendingRestore = null;
            Status = localization.FormatDiskSpace(exception);

            await LoadAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "还原项目失败：{Project}", ProjectPath);

            PendingRestore = null;
            Status = localization.Format("ProjectDetail.Restore.Failed", exception.Message);

            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Copy()
    {
        if (_project is null || ParentDirectory() is not { } parent)
        {
            return;
        }

        Status = null;
        CopyDestination = Path.Combine(parent, ProjectCopyService.FreeSiblingPath(fileIo.For(parent), ProjectName));
    }

    private string? ParentDirectory() => PathUtil.ParentOf(ProjectPath);

    [RelayCommand]
    private void CancelCopy() => CopyDestination = null;

    [RelayCommand]
    private async Task ConfirmCopy()
    {
        if (_project is null || CopyDestination is not { Length: > 0 } destination)
        {
            return;
        }

        Status = localization.Format("ProjectDetail.Copy.Working", destination);

        await RunBusyAsync(
            "复制项目失败：{Project}",
            ProjectName,
            async () =>
            {
                await progress.RunAsync(
                    localization["Progress.Copy"],
                    reporter => copies.CopyAsync(_project, destination, reporter, CancellationToken.None));

                CopyDestination = null;

                projects.Add(destination);
                Status = localization.Format("ProjectDetail.Copy.Done", destination);
            },
            "ProjectDetail.Copy.Failed");
    }

    private void ReloadBackups()
    {
        Backups.Clear();

        if (_project is null)
        {
            OnPropertyChanged(nameof(HasBackups));
            return;
        }

        foreach (var record in backupStore.List())
        {
            var entry = BackupItemViewModel.From(record);

            if (string.Equals(entry.ProjectName, ProjectName, StringComparison.OrdinalIgnoreCase))
            {
                Backups.Add(entry);
            }
        }

        if (PendingRestore is { } pending
            && Backups.All(x => !string.Equals(x.FilePath, pending.FilePath, StringComparison.OrdinalIgnoreCase)))
        {
            PendingRestore = null;
        }

        OnPropertyChanged(nameof(HasBackups));
    }

    [RelayCommand]
    private async Task Resolve()
    {
        if (_project is null)
        {
            return;
        }

        Status = null;

        await RunBusyAsync(
            "解析包依赖失败：{Project}",
            ProjectPath,
            async () =>
            {
                var outcome = await PackageOperations.ResolveAsync(
                    _project,
                    Catalog(),
                    _customPackages,
                    CancellationToken.None);

                if (outcome.Status != ResolveStatus.Success)
                {
                    logger.LogWarning(
                        "解析包依赖失败：{Project} 缺少 {Missing}",
                        ProjectPath,
                        ReportMissingDependencies(outcome.MissingDependencies));
                    return;
                }

                await SetPendingAsync(outcome.Changes!);
            },
            "ProjectDetail.ApplyFailed");
    }

    [RelayCommand]
    private async Task InstallPackage(AvailablePackageViewModel candidate)
    {
        if (_project is null)
        {
            return;
        }

        Status = null;

        var previous = _requestedAdds.GetValueOrDefault(candidate.Name);

        _requestedAdds[candidate.Name] = candidate.Package;

        if (await RefreshPendingAsync())
        {
            return;
        }

        if (previous is null)
        {
            _requestedAdds.Remove(candidate.Name);
        }
        else
        {
            _requestedAdds[candidate.Name] = previous;
        }
    }

    [RelayCommand]
    private async Task RemovePending(PackageChangeViewModel change)
    {
        if (change.Name is not { } name || !_requestedAdds.Remove(name))
        {
            return;
        }

        Status = null;

        await RefreshPendingAsync();
    }

    /// <summary>把已经加进清单的包合成一份待应用变更。</summary>
    private async Task<bool> RefreshPendingAsync()
    {
        if (_project is null)
        {
            return false;
        }

        if (_requestedAdds.Count == 0)
        {
            ClearPending();

            return true;
        }

        var outcome = await PackageOperations.AddAsync(
            _project,
            Catalog(),
            [.. _requestedAdds.Values],
            AddPackageOperation.AutoDetected,
            _allowPrerelease,
            CancellationToken.None);

        if (outcome.Status != AddPackageStatus.Success)
        {
            logger.LogWarning(
                "合成待应用变更失败：{Project} 缺少 {Missing}",
                ProjectPath,
                ReportMissingDependencies(outcome.MissingDependencies));

            return false;
        }

        await SetPendingAsync(outcome.Changes!, _requestedAdds.Keys);

        return true;
    }

    [RelayCommand]
    private async Task Remove(InstalledPackageViewModel installed)
    {
        if (!installed.CanRemove)
        {
            return;
        }

        await RemovePackagesAsync([installed.Name]);
    }

    private async Task<bool> RemovePackagesAsync(IReadOnlyList<string> names)
    {
        if (_project is null)
        {
            return false;
        }

        if (HasPendingChanges)
        {
            Status = localization["ProjectDetail.Remove.PendingFirst"];
            return false;
        }

        Status = null;

        var outcome = await PackageOperations.RemoveAsync(_project, names, CancellationToken.None);

        if (outcome.Status != RemovePackageStatus.Success)
        {
            logger.LogWarning(
                "卸载包失败：{Project} {Package} 未安装",
                ProjectPath,
                Join(outcome.NotInstalled));

            Status = localization.Format("ProjectDetail.NotInstalled", Join(outcome.NotInstalled));
            return false;
        }

        var changes = outcome.Changes!;
        var orphans = changes.PackageChanges
            .Where(x => x.Value is Remove { Reason: not RemoveReason.Requested })
            .Select(x => x.Key)
            .Where(x => !names.Contains(x, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var headline = names.Count == 1
            ? localization.Format("ProjectDetail.Remove.Message", names[0])
            : localization.Format("ProjectDetail.Remove.MessageMany", names.Count)
                + Environment.NewLine
                + string.Join(
                    Environment.NewLine,
                    names.Select(x => localization.Format("ProjectDetail.Remove.Orphan", x)));

        var detailTitle = orphans.Count == 0
            ? null
            : localization["ProjectDetail.Remove.Orphans.Title"];

        var detailBody = orphans.Count == 0
            ? null
            : string.Join(
                Environment.NewLine,
                orphans.Select(x => localization.Format("ProjectDetail.Remove.Orphan", x)));

        if (!dialogs.ShowConfirm(
                localization["ProjectDetail.Remove.Title"],
                headline,
                localization["ProjectDetail.Remove.Confirm"],
                danger: true,
                detailTitle: detailTitle,
                detailBody: detailBody))
        {
            return false;
        }

        IsBusy = true;

        try
        {
            await ApplyChangesAsync(changes, localization.Format("ProjectDetail.Remove.Done", names.Count));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "移除包失败：{Project}", ProjectPath);

            Status = localization.Format("ProjectDetail.ApplyFailed", exception.Message);

            await LoadAsync();
            return false;
        }
        finally
        {
            IsBusy = false;
        }

        return true;
    }

    public bool HasSelectedInstalled => Installed.Any(x => x.IsSelected && x.CanRemove);

    [RelayCommand(CanExecute = nameof(HasSelectedInstalled))]
    private async Task RemoveSelected()
    {
        var names = Installed
            .Where(x => x.IsSelected && x.CanRemove)
            .Select(x => x.Name)
            .ToList();

        if (names.Count == 0)
        {
            return;
        }

        await RemovePackagesAsync(names);
    }

    [RelayCommand]
    private async Task Reinstall(InstalledPackageViewModel installed)
    {
        if (!installed.CanReinstall)
        {
            return;
        }

        await ReinstallPackagesAsync([installed.Name]);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedInstalled))]
    private async Task ReinstallSelected()
    {
        var names = Installed
            .Where(x => x.IsSelected && x.CanReinstall)
            .Select(x => x.Name)
            .ToList();

        if (names.Count == 0)
        {
            return;
        }

        await ReinstallPackagesAsync(names);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedInstalled))]
    private void ClearSelection()
    {
        foreach (var row in Installed)
        {
            row.IsSelected = false;
        }
    }

    private async Task ReinstallPackagesAsync(IReadOnlyList<string> names)
    {
        if (_project is null)
        {
            return;
        }

        Status = null;

        var outcome = await PackageOperations.ReinstallAsync(_project, Catalog(), names, CancellationToken.None);

        if (outcome.Status != ReinstallStatus.Success)
        {
            logger.LogWarning(
                "重装包失败：{Project} {Package} {Status}",
                ProjectPath,
                Join(names),
                outcome.Status);

            if (outcome.Status == ReinstallStatus.NotInstalled)
            {
                Status = localization.Format("ProjectDetail.NotInstalled", Join(outcome.NotInstalled));
            }
            else
            {
                ReportMissingDependencies(outcome.MissingDependencies);
            }

            return;
        }

        await SetPendingAsync(outcome.Changes!);
    }

    [RelayCommand]
    private Task Discard() => WithBusyAsync(async () =>
    {
        ClearPending();

        if (_project is not null)
        {
            try
            {
                _project = await projects.OpenAsync(ProjectPath, CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "重新读取项目失败：{Project}", ProjectPath);

                Status = localization.Format("ProjectDetail.Unreadable", exception.Message);

                return;
            }
        }

        ReloadInstalled();

        Status = localization["ProjectDetail.Discarded"];
    });

    /// <summary>解析出待装列表再应用，用于新建项目。</summary>
    private async Task ResolveAndApplyAsync()
    {
        await Resolve();

        if (_pending is null)
        {
            return;
        }

        Status = localization["ProjectDetail.InstallingDependencies"];

        await Apply();
    }

    [RelayCommand]
    private async Task Apply()
    {
        if (_project is null || _pending is not { } changes)
        {
            return;
        }

        IsBusy = true;

        try
        {
            if (await FingerprintAsync() != _pendingFingerprint)
            {
                logger.LogWarning("待应用的变更已过期：{Project}", ProjectPath);

                Status = localization["ProjectDetail.ChangesStale"];
                ClearPending();
                await LoadAsync();
                return;
            }

            await ApplyChangesAsync(changes, localization["ProjectDetail.Applied"]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "应用包变更失败：{Project}", ProjectPath);

            Status = localization.Format("ProjectDetail.ApplyFailed", exception.Message);

            ClearPending();
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyChangesAsync(PendingProjectChanges changes, string successMessage)
    {
        var leftover = await progress.RunAsync(
            localization["Progress.Apply"],
            _ => ProjectChangesApplier.ApplyAsync(_project!, installer, changes, CancellationToken.None));

        ClearPending();
        _project = await projects.OpenAsync(ProjectPath, CancellationToken.None);

        ReloadInstalled();
        ReloadAvailable();

        Status = leftover.Count > 0
            ? localization.Format("ProjectDetail.AppliedWithLeftovers", leftover.Count)
            : successMessage;
    }

    private TransientAction Search =>
        _search ??= new TransientAction(TimeSpan.FromMilliseconds(SearchDelayMilliseconds), ReloadAvailable);

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearchEmpty));
        Search.Run();
    }

    private async Task<string> FingerprintAsync()
    {
        if (_project is null)
        {
            return "";
        }

        var bytes = await _project.Io.ReadAllBytesOrNullAsync(VpmManifest.RelativePath, CancellationToken.None);
        return bytes is null ? "" : Sha256Hex.Of(bytes);
    }

    private async Task SetPendingAsync(PendingProjectChanges changes, IReadOnlyCollection<string>? requestedAdds = null)
    {
        if (requestedAdds is null)
        {
            _requestedAdds.Clear();
        }

        _pending = changes;
        _pendingFingerprint = await FingerprintAsync();

        PendingChanges.Clear();
        PendingConflicts.Clear();

        foreach (var (name, change) in changes.PackageChanges)
        {
            var description = change switch
            {
                Install { Package: { } package } => localization.Format(
                    "ProjectDetail.Change.Install",
                    name,
                    package.Version.ToString()),
                Install => localization.Format("ProjectDetail.Change.AddDependency", name),
                Remove remove => localization.Format(
                    $"ProjectDetail.Change.Remove.{remove.Reason}",
                    name),
                _ => name,
            };

            var removable = change is Install && requestedAdds?.Contains(name) == true;

            PendingChanges.Add(new PackageChangeViewModel(description, name, removable));
        }

        foreach (var (name, conflict) in changes.Conflicts)
        {
            if (conflict.ConflictsWithUnity)
            {
                PendingConflicts.Add(localization.Format("ProjectDetail.Conflict.Unity", name));
            }

            if (conflict.ConflictingPackages.Count > 0)
            {
                PendingConflicts.Add(localization.Format(
                    "ProjectDetail.Conflict.Packages",
                    name,
                    Join(conflict.ConflictingPackages)));
            }

            foreach (var unlocked in conflict.UnlockedNames)
            {
                PendingConflicts.Add(localization.Format("ProjectDetail.Conflict.Unlocked", unlocked));
            }
        }

        foreach (var asset in changes.RemoveLegacyFiles.Concat(changes.RemoveLegacyFolders))
        {
            PendingChanges.Add(new PackageChangeViewModel(
                localization.Format("ProjectDetail.Change.RemoveLegacyAsset", asset.Path, asset.PackageName)));
        }

        OnPropertyChanged(nameof(HasPendingChanges));
    }

    private void ClearPending()
    {
        _pending = null;
        _requestedAdds.Clear();
        _pendingFingerprint = "";
        PendingChanges.Clear();
        PendingConflicts.Clear();
        OnPropertyChanged(nameof(HasPendingChanges));
    }

    private string TypeName(ProjectType type) => localization[$"ProjectType.{type}"];

    private void ReloadInstalled()
    {
        Installed.Clear();
        _customPackages.Clear();

        if (_project is not null)
        {
            var catalog = Catalog();
            var unlockedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var customRows = new List<InstalledPackageViewModel>();

            foreach (var (name, manifest) in _project.UnlockedPackages)
            {
                unlockedNames.Add(name);

                if (manifest is not null)
                {
                    unlockedNames.Add(manifest.Name);
                }
            }

            foreach (var (name, manifest) in _project.InstalledPackages)
            {
                var row = Row(
                    catalog,
                    name,
                    manifest.DisplayName ?? name,
                    manifest.Version.ToString(),
                    isUnlocked: false,
                    isCustom: _catalogComplete && !catalog.HasName(name),
                    isMissing: false);

                if (row.IsCustom)
                {
                    customRows.Add(row);
                    _customPackages.Add(name);
                }
                else
                {
                    Installed.Add(row);
                }
            }

            if (_catalogComplete)
            {
                foreach (var locked in _project.LockedPackages)
                {
                    if (catalog.HasName(locked.Name))
                    {
                        continue;
                    }

                    if (_project.GetInstalledPackage(locked.Name) is not null)
                    {
                        _customPackages.Add(locked.Name);
                        continue;
                    }

                    if (unlockedNames.Contains(locked.Name))
                    {
                        continue;
                    }

                    customRows.Add(Row(
                        catalog,
                        locked.Name,
                        locked.Name,
                        locked.Version.ToString(),
                        isUnlocked: false,
                        isCustom: true,
                        isMissing: true));

                    _customPackages.Add(locked.Name);
                }
            }

            foreach (var (name, manifest) in _project.UnlockedPackages)
            {
                customRows.Add(Row(
                    catalog,
                    name,
                    manifest?.DisplayName ?? name,
                    manifest?.Version.ToString() ?? "",
                    isUnlocked: true,
                    isCustom: true,
                    isMissing: false));

                _customPackages.Add(manifest?.Name ?? name);
            }

            foreach (var row in customRows)
            {
                Installed.Add(row);
            }
        }

        OnPropertyChanged(nameof(NeedsResolve));
        NotifySelectionChanged();
    }

    private InstalledPackageViewModel Row(
        PackageCatalog catalog,
        string name,
        string displayName,
        string version,
        bool isUnlocked,
        bool isCustom,
        bool isMissing)
    {
        var selector = OfferedVersionSelector();

        var versions = isUnlocked || isCustom
            ? []
            : catalog.GetVersions(name)
                .Where(x => selector.Satisfies(x.Manifest))
                .Select(x => x.Version)
                .OrderDescending()
                .Select(x => x.ToString())
                .ToList();

        if (version.Length > 0 && !versions.Contains(version))
        {
            versions.Insert(0, version);
        }

        var row = new InstalledPackageViewModel(
            name,
            displayName,
            version,
            isUnlocked,
            isCustom,
            isMissing,
            versions,
            NewerVersion(catalog, name, version, isUnlocked || isCustom),
            isCustom ? null : chosen => _ = SwitchVersionAsync(name, chosen));

        row.PropertyChanged += OnInstalledChanged;

        return row;
    }

    private void OnInstalledChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InstalledPackageViewModel.IsSelected))
        {
            return;
        }

        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelectedInstalled));
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        ReinstallSelectedCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
    }

    private string? NewerVersion(PackageCatalog catalog, string name, string version, bool isUnlocked)
    {
        if (isUnlocked
            || _project is null
            || !PackageVersion.TryParse(version, out var installed))
        {
            return null;
        }

        return catalog
            .NewerThan(name, installed, _project.UnityVersion, _allowPrerelease)
            ?.Version.ToString();
    }

    public Task SwitchToVersionAsync(string name, string version) => SwitchVersionAsync(name, version);

    private async Task SwitchVersionAsync(string name, string version)
    {
        if (_project is null)
        {
            return;
        }

        if (!PackageVersion.TryParse(version, out var parsed))
        {
            return;
        }

        Status = null;

        var outcome = await PackageOperations.SwitchVersionAsync(
            _project,
            Catalog(),
            name,
            parsed,
            _allowPrerelease,
            CancellationToken.None);

        if (outcome.Status == AddPackageStatus.VersionNotFound)
        {
            logger.LogWarning("指定的包版本不存在：{Project} {Package} {Version}", ProjectPath, name, version);

            Status = localization.Format("ProjectDetail.VersionUnavailable", name, version);
            return;
        }

        if (outcome.Status != AddPackageStatus.Success)
        {
            logger.LogWarning(
                "切换包版本失败：{Project} {Package} {Version} 缺少 {Missing}",
                ProjectPath,
                name,
                version,
                ReportMissingDependencies(outcome.MissingDependencies));
            return;
        }

        await SetPendingAsync(outcome.Changes!);
    }

    private VersionSelector OfferedVersionSelector() =>
        VersionSelector.LatestFor(_project?.UnityVersion, _allowPrerelease);

    private void ReloadAvailable()
    {
        Available.Clear();

        if (_project is null)
        {
            return;
        }

        var query = SearchText.Trim();

        if (query.Length == 0)
        {
            return;
        }

        var installed = _project.AllPackages.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var catalog = OfferedCatalog();
        var selector = OfferedVersionSelector();
        var matches = new List<(int Rank, string Name, IReadOnlyList<PackageInfo> Candidates, string Display)>();

        foreach (var name in catalog.PackageNames)
        {
            if (installed.Contains(name))
            {
                continue;
            }

            var candidates = catalog.GetVersions(name)
                .Where(x => selector.Satisfies(x.Manifest))
                .OrderByDescending(x => x.Version)
                .ToList();

            if (candidates.Count == 0)
            {
                continue;
            }

            var display = candidates[0].Manifest.DisplayName ?? name;

            if (ApproximateMatch.Rank(query, name, display) is not { } rank)
            {
                continue;
            }

            matches.Add((rank, name, candidates, display));
        }

        foreach (var match in matches
                     .OrderBy(x => x.Rank)
                     .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                     .Take(SearchLimit))
        {
            Available.Add(new AvailablePackageViewModel(match.Candidates, match.Display));
        }
    }
}

public sealed partial class InstalledPackageViewModel : ObservableObject
{
    private readonly Action<string>? _onVersionChosen;

    public InstalledPackageViewModel(
        string name,
        string displayName,
        string version,
        bool isUnlocked,
        bool isCustom,
        bool isMissing,
        IReadOnlyList<string> availableVersions,
        string? newerVersion,
        Action<string>? onVersionChosen)
    {
        Name = name;
        DisplayName = displayName;
        Version = version;
        IsUnlocked = isUnlocked;
        IsCustom = isCustom;
        IsMissing = isMissing;
        _onVersionChosen = onVersionChosen;
        _selectedVersion = version;

        AvailableVersions = availableVersions;
        NewerVersion = newerVersion;
    }

    public string Name { get; }

    public string DisplayName { get; }

    public string Version { get; }

    public bool IsUnlocked { get; }

    public bool IsCustom { get; }

    public bool IsMissing { get; }

    public bool CanRemove => !IsUnlocked && !IsMissing;

    public bool CanReinstall => !IsUnlocked && !IsCustom;

    public IReadOnlyList<string> AvailableVersions { get; }

    public bool CanSwitchVersion => !IsUnlocked && !IsCustom && AvailableVersions.Count > 1;

    public bool CannotSwitchVersion => !CanSwitchVersion;

    public string? NewerVersion { get; }

    public bool HasNewerVersion => NewerVersion is not null;

    [ObservableProperty]
    private string _selectedVersion;

    partial void OnSelectedVersionChanged(string value)
    {
        if (value != Version)
        {
            _onVersionChosen?.Invoke(value);
        }
    }

    [ObservableProperty]
    private bool _isSelected;
}

public sealed class UnityEditorOption(UnityEditor editor)
{
    public UnityEditor Editor { get; } = editor;

    public string Path { get; } = editor.ExecutablePath;

    public string Display { get; } = editor.Version.ToString();
}

public sealed partial class AvailablePackageViewModel : ObservableObject
{
    private readonly IReadOnlyList<PackageInfo> _candidates;
    private readonly List<string> _versions;

    public AvailablePackageViewModel(IReadOnlyList<PackageInfo> candidates, string displayName)
    {
        _candidates = candidates;
        _versions = [.. candidates.Select(x => x.Version.ToString())];

        Name = candidates[0].Name;
        DisplayName = displayName;
        AvailableVersions = _versions;
        _selectedVersion = _versions[0];
    }

    public string Name { get; }

    public string DisplayName { get; }

    public IReadOnlyList<string> AvailableVersions { get; }

    public bool CanChooseVersion => _versions.Count > 1;

    public bool CannotChooseVersion => !CanChooseVersion;

    public PackageInfo Package => _candidates[Math.Max(0, _versions.IndexOf(SelectedVersion))];

    [ObservableProperty]
    private string _selectedVersion;
}

public sealed class PackageChangeViewModel(string description, string? name = null, bool removable = false)
{
    public string Description { get; } = description;

    public string? Name { get; } = name;

    public bool CanRemove { get; } = removable;
}
