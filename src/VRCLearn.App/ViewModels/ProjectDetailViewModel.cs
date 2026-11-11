using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.IO;
using VRCLearn.Core.Packages;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Repositories;
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
    private const int SearchDelayMilliseconds = 250;

    private UnityProject? _project;

    private bool _resolveAfterLoad;

    private int _loadGeneration;

    private bool _changingPackages;

    private PackageCatalog? _catalog;
    private PackageCatalog? _offeredCatalog;
    private ICollectionView? _packageView;
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

    private void ReloadEditors(IReadOnlyList<UnityEditor> installed)
    {
        _reloadingEditors = true;

        try
        {
            InstalledEditors.Clear();

            if (_project is { } project)
            {
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

    public ObservableCollection<PackageRowViewModel> Packages { get; } = [];

    public ICollectionView PackageView => _packageView ??= CreatePackageView();

    public bool NeedsResolve => _project?.ShouldResolve(_customPackages) ?? false;

    public ProjectType ProjectType { get; private set; }

    [ObservableProperty]
    private string? _copyDestination;

    partial void OnCopyDestinationChanged(string? value) => OnPropertyChanged(nameof(HasPendingCopy));

    public bool HasPendingCopy => CopyDestination is not null;

    public ObservableCollection<BackupItemViewModel> Backups { get; } = [];

    public bool HasBackups => Backups.Count > 0;

    [ObservableProperty]
    private bool _areBackupsExpanded;

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

    private PackageCatalog Catalog() => _catalog!;

    /// <summary>「可以装什么」用这份；隐藏的仓库不计入。</summary>
    private PackageCatalog OfferedCatalog() => _offeredCatalog!;

    private static (PackageCatalog Catalog, PackageCatalog Offered, bool Complete) BuildCatalogs(
        IReadOnlyList<LocalCachedRepository> cached,
        int repositoryCount,
        IReadOnlyList<PackageInfo> userPackages,
        IReadOnlyCollection<string> hidden,
        bool hideLocal)
    {
        var catalog = PackageCatalog.FromRepositories(cached, userPackages);
        var offered = PackageCatalog.FromRepositories(
            [.. cached.Where(x => x.Id is null || !hidden.Contains(x.Id))],
            hideLocal ? [] : userPackages);

        return (catalog, offered, cached.Count == repositoryCount);
    }

    private void ForgetCatalogs()
    {
        _catalog = null;
        _offeredCatalog = null;
        _catalogComplete = false;
    }

    public void Release()
    {
        _loadGeneration++;

        if (IsBusy)
        {
            return;
        }

        _project = null;
        _userPackages = [];
        _customPackages.Clear();
        ForgetCatalogs();

        CopyDestination = null;
        UnityHubInstallLink = null;
        PendingRestore = null;
        Packages.Clear();
        Backups.Clear();
        InstalledEditors.Clear();
        NotifySelectionChanged();
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
        _loadGeneration++;
        ProjectPath = projectPath;
        _project = null;
        _resolveAfterLoad = resolveAfterLoad;
        _customPackages.Clear();
        ForgetCatalogs();

        Packages.Clear();
        Backups.Clear();
        InstalledEditors.Clear();
        SelectedEditor = null;
        PendingRestore = null;
        AreBackupsExpanded = false;
        CopyDestination = null;
        UnityHubInstallLink = null;
        NotifySelectionChanged();
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
        var generation = ++_loadGeneration;
        var path = ProjectPath;

        OnPropertyChanged(nameof(EditorCaption));
        OnPropertyChanged(nameof(RestorePrompt));

        IsBusy = true;

        try
        {
            var project = await projects.OpenAsync(path, CancellationToken.None);
            var loadedUserPackages = await userPackages.LoadPackagesAsync(CancellationToken.None);

            if (generation != _loadGeneration)
            {
                return;
            }

            var repositoryEntries = repositories.List();
            var hidden = configStore.Current.HiddenRepositories.ToArray();
            var hideLocal = configStore.Current.HideLocalUserPackages;

            var catalogsTask = Task.Run(() => BuildCatalogs(
                repositories.CachedRepositories(repositoryEntries),
                repositoryEntries.Count,
                loadedUserPackages,
                hidden,
                hideLocal));

            var editorsTask = Task.Run(opener.AllEditors);

            await Task.WhenAll(catalogsTask, editorsTask);

            if (generation != _loadGeneration)
            {
                return;
            }

            _project = project;
            _userPackages = loadedUserPackages;
            _allowPrerelease = settings.Current.ShowPrereleasePackages;
            ForgetCatalogs();
            (_catalog, _offeredCatalog, _catalogComplete) = catalogsTask.Result;
            ReloadUnityArguments();
            ProjectType = _project.DetectProjectType();
            OnPropertyChanged(nameof(HeaderSubtitle));

            ReloadEditors(editorsTask.Result);
            ReloadPackages();
            ReloadBackups();

            RefreshUnityState();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (generation != _loadGeneration)
            {
                return;
            }

            logger.LogWarning(exception, "读取项目失败：{Project}", path);

            _project = null;
            _customPackages.Clear();
            SelectedEditor = null;
            ProjectType = ProjectType.Unknown;
            Packages.Clear();
            Backups.Clear();
            InstalledEditors.Clear();
            NotifySelectionChanged();
            OnPropertyChanged(nameof(HasBackups));
            OnPropertyChanged(nameof(HeaderSubtitle));
            Status = localization.Format("ProjectDetail.Unreadable", exception.Message);
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                IsBusy = false;
            }
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
        if (_project is not { } project)
        {
            return;
        }

        var generation = _loadGeneration;
        var path = ProjectPath;
        var name = ProjectName;
        var version = project.UnityVersion;

        UnityHubInstallLink = null;
        Status = localization["Unity.StartingHub"];

        var outcome = await opener.OpenAsync(
            path,
            SelectedEditor?.Editor,
            opener.ArgumentsFor(path),
            CancellationToken.None);

        if (generation != _loadGeneration || _project is null)
        {
            return;
        }

        if (outcome.Result == UnityOpenResult.NoEditor)
        {
            UnityHubInstallLink = UnityHubLink.ForInstall(version, project.UnityRevision);
        }

        Status = UnityLaunchStatus.For(outcome, localization, name, version.ToString());

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
        if (_project is null || IsBusy)
        {
            return;
        }

        var options = dialogs.ShowBackupOptions();

        if (options is null)
        {
            return;
        }

        await RunBusyAsync(
            "备份项目失败：{Project}",
            ProjectName,
            async () =>
            {
                var skipped = new List<string>();

                var record = await backupRunner.RunAsync(
                    ProjectPath,
                    ProjectName,
                    options.Note,
                    options.IncludeLibrary,
                    skipped,
                    CancellationToken.None);

                ReloadBackups();
                Status = skipped.Count == 0
                    ? localization.Format("ProjectDetail.BackupDone", record.FileName)
                    : localization.Format("ProjectDetail.BackupDoneSkipped", record.FileName, skipped.Count);
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
        if (_project is null || PendingRestore is not { } entry || IsBusy)
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

            Status = outcome.Leftovers.Count == 0
                ? localization.Format("ProjectDetail.Restore.Done", Path.GetFileName(outcome.SafetyBackup))
                : localization.Format(
                    "ProjectDetail.Restore.DoneWithLeftover",
                    Path.GetFileName(outcome.SafetyBackup),
                    string.Join("、", outcome.Leftovers));
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
        if (_project is null || IsBusy || _changingPackages)
        {
            return;
        }

        Status = null;
        _changingPackages = true;

        try
        {
            var changes = await PlanResolveAsync();

            if (changes is null || changes.PackageChanges.Count == 0)
            {
                return;
            }

            var messageInlines = ApplyInlines(changes);

            if (!dialogs.ShowConfirm(
                    localization["ProjectDetail.ApplyChanges.Title"],
                    InlinesToText(messageInlines),
                    localization["ProjectDetail.Apply"],
                    width: 680,
                    messageInlines: messageInlines))
            {
                return;
            }

            IsBusy = true;

            try
            {
                await ApplyChangesAsync(changes, localization["ProjectDetail.Applied"]);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "应用包变更失败：{Project}", ProjectPath);

                Status = localization.Format("ProjectDetail.ApplyFailed", exception.Message);

                await LoadAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }
        finally
        {
            _changingPackages = false;
        }
    }

    private async Task<PendingProjectChanges?> PlanResolveAsync()
    {
        if (_project is null)
        {
            return null;
        }

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
            return null;
        }

        return outcome.Changes;
    }

    [RelayCommand]
    private async Task InstallPackage(PackageRowViewModel row)
    {
        if (row.Package is not { } package)
        {
            return;
        }

        await ConfirmAndApplyAsync(
            upgradeDependents => PlanAddAsync(
                [package],
                AddPackageOperation.InstallToDependencies,
                upgradeDependents,
                reportFailure: !upgradeDependents),
            localization.Format("ProjectDetail.Install.Done", row.DisplayName, package.Version));
    }

    [RelayCommand(CanExecute = nameof(CanInstallSelected))]
    private async Task InstallSelected()
    {
        var packages = Packages
            .Where(x => x.IsSelected && x.CanAdd)
            .Select(x => x.Package)
            .OfType<PackageInfo>()
            .ToList();

        if (packages.Count == 0)
        {
            return;
        }

        await ConfirmAndApplyAsync(
            upgradeDependents => PlanAddAsync(
                packages,
                AddPackageOperation.InstallToDependencies,
                upgradeDependents,
                reportFailure: !upgradeDependents),
            localization.Format("ProjectDetail.Batch.Installed", packages.Count));
    }

    [RelayCommand(CanExecute = nameof(CanUpgradeSelected))]
    private Task UpgradeSelected() => UpgradePackagesAsync(
        Packages.Where(x => x.IsSelected && x.HasNewerVersion).ToList());

    [RelayCommand(CanExecute = nameof(CanUpgradeAll))]
    private Task UpgradeAll() => UpgradePackagesAsync(
        Packages.Where(x => x.HasNewerVersion).ToList());

    private async Task UpgradePackagesAsync(IReadOnlyList<PackageRowViewModel> rows)
    {
        var catalog = Catalog();
        var packages = new List<PackageInfo>();

        foreach (var row in rows)
        {
            if (row.NewerVersion is not { } newer
                || !PackageVersion.TryParse(newer, out var version)
                || catalog.Find(row.Name, VersionSelector.ForSpecificVersion(version)) is not { } package)
            {
                continue;
            }

            packages.Add(package);
        }

        if (packages.Count == 0)
        {
            return;
        }

        await ConfirmAndApplyAsync(
            upgradeDependents => PlanAddAsync(
                packages,
                AddPackageOperation.UpgradeLocked,
                upgradeDependents,
                reportFailure: !upgradeDependents),
            localization.Format("ProjectDetail.Batch.Upgraded", packages.Count));
    }

    [RelayCommand]
    private async Task Remove(PackageRowViewModel row)
    {
        if (!row.CanRemove)
        {
            return;
        }

        await RemovePackagesAsync([row.Name]);
    }

    private async Task<bool> RemovePackagesAsync(IReadOnlyList<string> names)
    {
        if (_project is null || IsBusy)
        {
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

    public bool HasSelected => Packages.Any(x => x.IsSelected);

    public bool CanInstallSelected => Packages.Any(x => x.IsSelected && x.CanAdd);

    public bool CanUpgradeSelected => Packages.Any(x => x.IsSelected && x.HasNewerVersion);

    public bool CanReinstallSelected => Packages.Any(x => x.IsSelected && x.CanReinstall);

    public bool CanRemoveSelected => Packages.Any(x => x.IsSelected && x.CanRemove);

    public bool CanUpgradeAll => Packages.Any(x => x.HasNewerVersion);

    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private async Task RemoveSelected()
    {
        var names = Packages
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
    private async Task Reinstall(PackageRowViewModel row)
    {
        if (!row.CanReinstall)
        {
            return;
        }

        await ReinstallPackagesAsync([row.Name]);
    }

    [RelayCommand(CanExecute = nameof(CanReinstallSelected))]
    private async Task ReinstallSelected()
    {
        var names = Packages
            .Where(x => x.IsSelected && x.CanReinstall)
            .Select(x => x.Name)
            .ToList();

        if (names.Count == 0)
        {
            return;
        }

        await ReinstallPackagesAsync(names);
    }

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private void ClearSelection()
    {
        foreach (var row in Packages)
        {
            row.IsSelected = false;
        }
    }

    private async Task ReinstallPackagesAsync(IReadOnlyList<string> names)
    {
        if (_project is null || IsBusy || _changingPackages)
        {
            return;
        }

        Status = null;
        _changingPackages = true;

        try
        {
            var outcome = await PackageOperations.ReinstallAsync(
                _project,
                Catalog(),
                names,
                CancellationToken.None);

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

            var changes = outcome.Changes!;
            var messageInlines = ApplyInlines(changes);

            if (!dialogs.ShowConfirm(
                    localization["ProjectDetail.ApplyChanges.Title"],
                    InlinesToText(messageInlines),
                    localization["ProjectDetail.Apply"],
                    width: 680,
                    messageInlines: messageInlines))
            {
                return;
            }

            IsBusy = true;

            try
            {
                await ApplyChangesAsync(
                    changes,
                    localization.Format("ProjectDetail.Batch.Reinstalled", names.Count));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "重装包失败：{Project}", ProjectPath);

                Status = localization.Format("ProjectDetail.ApplyFailed", exception.Message);

                await LoadAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }
        finally
        {
            _changingPackages = false;
        }
    }

    /// <summary>解析出待装列表再应用，用于新建项目。</summary>
    private async Task ResolveAndApplyAsync()
    {
        if (_project is null || IsBusy)
        {
            return;
        }

        Status = localization["ProjectDetail.InstallingDependencies"];

        try
        {
            var changes = await PlanResolveAsync();

            if (changes is null || changes.PackageChanges.Count == 0)
            {
                return;
            }

            await ApplyChangesAsync(changes, localization["ProjectDetail.Applied"]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "应用包变更失败：{Project}", ProjectPath);

            Status = localization.Format("ProjectDetail.ApplyFailed", exception.Message);

            await LoadAsync();
        }
    }

    private async Task ApplyChangesAsync(PendingProjectChanges changes, string successMessage)
    {
        var leftover = await progress.RunAsync(
            localization["Progress.Apply"],
            _ => ProjectChangesApplier.ApplyAsync(_project!, installer, changes, CancellationToken.None));

        _project = await projects.OpenAsync(ProjectPath, CancellationToken.None);

        ReloadPackages();

        Status = leftover.Count > 0
            ? localization.Format("ProjectDetail.AppliedWithLeftovers", leftover.Count)
            : successMessage;
    }

    private TransientAction Search =>
        _search ??= new TransientAction(TimeSpan.FromMilliseconds(SearchDelayMilliseconds), RefreshPackageFilter);

    partial void OnSearchTextChanged(string value) => Search.Run();

    private void RefreshPackageFilter() => PackageView.Refresh();

    private List<string> ConflictLines(
        PendingProjectChanges changes,
        IReadOnlyCollection<string>? explained = null)
    {
        var skip = explained is null
            ? null
            : new HashSet<string>(explained, StringComparer.OrdinalIgnoreCase);

        var lines = new List<string>();

        foreach (var (name, conflict) in changes.Conflicts)
        {
            if (conflict.ConflictsWithUnity)
            {
                lines.Add(localization.Format("ProjectDetail.Conflict.Unity", name));
            }

            var conflicting = skip is null
                ? conflict.ConflictingPackages
                : conflict.ConflictingPackages.Where(x => !skip.Contains(x)).ToList();

            if (conflicting.Count > 0)
            {
                lines.Add(localization.Format(
                    "ProjectDetail.Conflict.Packages",
                    name,
                    Join(conflicting)));
            }

            foreach (var unlocked in conflict.UnlockedNames)
            {
                lines.Add(localization.Format("ProjectDetail.Conflict.Unlocked", unlocked));
            }
        }

        return lines;
    }

    private List<string> BlockingConflicts(
        PendingProjectChanges changes,
        IReadOnlyCollection<string>? explained = null)
    {
        var skip = explained is null
            ? null
            : new HashSet<string>(explained, StringComparer.OrdinalIgnoreCase);

        var lines = new List<string>();

        foreach (var (name, conflict) in changes.Conflicts)
        {
            if (conflict.ConflictsWithUnity)
            {
                lines.Add(localization.Format("ProjectDetail.Conflict.Unity", name));
            }

            var conflicting = skip is null
                ? conflict.ConflictingPackages
                : conflict.ConflictingPackages.Where(x => !skip.Contains(x)).ToList();

            if (conflicting.Count > 0)
            {
                lines.Add(localization.Format(
                    "ProjectDetail.Conflict.Packages",
                    name,
                    Join(conflicting)));
            }
        }

        return lines;
    }

    private static readonly Regex InlinePlaceholder = new(@"\{(\d)\}", RegexOptions.Compiled);

    private static IReadOnlyList<Inline> FormatInlines(
        string format,
        params (string Text, bool Bold)[] args)
    {
        var inlines = new List<Inline>();
        var position = 0;

        foreach (Match match in InlinePlaceholder.Matches(format))
        {
            if (match.Index > position)
            {
                inlines.Add(new Run(format[position..match.Index]));
            }

            var (text, bold) = args[int.Parse(match.Groups[1].Value)];

            inlines.Add(new Run(text)
            {
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            });

            position = match.Index + match.Length;
        }

        if (position < format.Length)
        {
            inlines.Add(new Run(format[position..]));
        }

        return inlines;
    }

    private static string InlinesToText(IEnumerable<Inline> inlines) =>
        string.Concat(inlines.Select(x => x switch
        {
            Run run => run.Text,
            LineBreak => Environment.NewLine,
            _ => "",
        }));

    private static void AppendLine(List<Inline> target, IReadOnlyList<Inline> line)
    {
        if (target.Count > 0)
        {
            target.Add(new LineBreak());
        }

        foreach (var inline in line)
        {
            target.Add(inline);
        }
    }

    private List<Inline> ApplyInlines(
        PendingProjectChanges changes,
        IReadOnlyCollection<string>? explained = null)
    {
        var inlines = new List<Inline>();

        foreach (var (name, change) in changes.PackageChanges)
        {
            AppendLine(inlines, ApplyLineInlines(name, change));
        }

        foreach (var line in ConflictLines(changes, explained))
        {
            AppendLine(inlines, [new Run(line)]);
        }

        foreach (var asset in changes.RemoveLegacyFiles.Concat(changes.RemoveLegacyFolders))
        {
            AppendLine(
                inlines,
                FormatInlines(
                    localization["ProjectDetail.Change.RemoveLegacyAsset"],
                    (asset.Path, false),
                    (asset.PackageName, true)));
        }

        return inlines;
    }

    private IReadOnlyList<Inline> ApplyLineInlines(string name, PackageChange change)
    {
        if (change is not Install { Package: { } package })
        {
            return change switch
            {
                Install => FormatInlines(
                    localization["ProjectDetail.Change.AddDependency"],
                    (name, true)),
                Remove remove => FormatInlines(
                    localization[$"ProjectDetail.Change.Remove.{remove.Reason}"],
                    (name, true)),
                _ => [new Run(name)],
            };
        }

        var display = DisplayNameOf(name, package);
        var old = _project?.GetLocked(name)?.Version;

        if (old is null)
        {
            return FormatInlines(
                localization["ProjectDetail.Change.InstallLine"],
                (display, true),
                (package.Version.ToString(), true));
        }

        var comparison = package.Version.CompareTo(old);

        return comparison switch
        {
            > 0 => FormatInlines(
                localization["ProjectDetail.Change.UpgradeLine"],
                (display, true),
                (old.ToString(), false),
                (package.Version.ToString(), true)),
            < 0 => FormatInlines(
                localization["ProjectDetail.Change.DowngradeLine"],
                (display, true),
                (old.ToString(), false),
                (package.Version.ToString(), true)),
            _ => FormatInlines(
                localization["ProjectDetail.Change.ReinstallLine"],
                (display, true),
                (package.Version.ToString(), true)),
        };
    }

    private string DisplayNameOf(string name, PackageInfo package)
    {
        var installed = _project?.AllInstalledPackages
            .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.DisplayName;

        if (!string.IsNullOrWhiteSpace(installed))
        {
            return installed;
        }

        return string.IsNullOrWhiteSpace(package.Manifest.DisplayName)
            ? name
            : package.Manifest.DisplayName;
    }

    private IReadOnlyList<Inline> AffectedInlines(
        IReadOnlyList<AffectedDependent> affected,
        PendingProjectChanges? cascade)
    {
        var inlines = new List<Inline>();

        foreach (var dependent in affected)
        {
            AppendLine(inlines, AffectedLineInlines(dependent, cascade));
        }

        return inlines;
    }

    private IReadOnlyList<Inline> AffectedLineInlines(
        AffectedDependent dependent,
        PendingProjectChanges? cascade)
    {
        var display = InstalledDisplayName(dependent.Name);

        if (cascade?.PackageChanges
                .FirstOrDefault(x => string.Equals(
                    x.Key,
                    dependent.Name,
                    StringComparison.OrdinalIgnoreCase))
                .Value is Install { Package: { } package })
        {
            var comparison = package.Version.CompareTo(dependent.Version);

            if (comparison > 0)
            {
                return FormatInlines(
                    localization["ProjectDetail.ApplyChanges.AffectedUpgradeLine"],
                    (display, true),
                    (dependent.Version.ToString(), false),
                    (package.Version.ToString(), true));
            }

            if (comparison < 0)
            {
                return FormatInlines(
                    localization["ProjectDetail.ApplyChanges.AffectedDowngradeLine"],
                    (display, true),
                    (dependent.Version.ToString(), false),
                    (package.Version.ToString(), true));
            }
        }

        return FormatInlines(
            localization["ProjectDetail.ApplyChanges.AffectedLine"],
            (display, true),
            (dependent.Version.ToString(), false));
    }

    private string InstalledDisplayName(string name)
    {
        var display = _project?.AllInstalledPackages
            .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.DisplayName;

        return string.IsNullOrWhiteSpace(display) ? name : display;
    }

    private string TypeName(ProjectType type) => localization[$"ProjectType.{type}"];

    private void ReloadPackages()
    {
        Packages.Clear();
        _customPackages.Clear();

        if (_project is not null)
        {
            var catalog = Catalog();
            var unlockedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var customRows = new List<PackageRowViewModel>();

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
                var row = InstalledRow(
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
                    Packages.Add(row);
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

                    customRows.Add(InstalledRow(
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
                customRows.Add(InstalledRow(
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
                Packages.Add(row);
            }

            var installed = _project.AllPackages
                .Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var offered = OfferedCatalog();
            var selector = OfferedVersionSelector();
            var available = new List<PackageRowViewModel>();

            foreach (var name in offered.PackageNames)
            {
                if (installed.Contains(name))
                {
                    continue;
                }

                var candidates = offered.GetVersions(name)
                    .Where(x => selector.Satisfies(x.Manifest))
                    .OrderByDescending(x => x.Version)
                    .ToList();

                if (candidates.Count == 0)
                {
                    continue;
                }

                var row = PackageRowViewModel.Available(
                    candidates,
                    candidates[0].Manifest.DisplayName ?? name);

                row.PropertyChanged += OnPackageChanged;
                available.Add(row);
            }

            foreach (var row in available
                         .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                         .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                Packages.Add(row);
            }
        }

        OnPropertyChanged(nameof(NeedsResolve));
        NotifySelectionChanged();
        PackageView.Refresh();
    }

    private PackageRowViewModel InstalledRow(
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

        var row = PackageRowViewModel.Installed(
            name,
            displayName,
            version,
            isUnlocked,
            isCustom,
            isMissing,
            versions,
            NewerVersion(catalog, name, version, isUnlocked || isCustom),
            isCustom ? null : chosen => _ = ChangeVersionAsync(name, displayName, version, chosen));

        row.PropertyChanged += OnPackageChanged;

        return row;
    }

    private void OnPackageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PackageRowViewModel.IsSelected))
        {
            return;
        }

        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelected));
        OnPropertyChanged(nameof(CanInstallSelected));
        OnPropertyChanged(nameof(CanUpgradeSelected));
        OnPropertyChanged(nameof(CanReinstallSelected));
        OnPropertyChanged(nameof(CanRemoveSelected));
        OnPropertyChanged(nameof(CanUpgradeAll));

        InstallSelectedCommand.NotifyCanExecuteChanged();
        UpgradeSelectedCommand.NotifyCanExecuteChanged();
        ReinstallSelectedCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        UpgradeAllCommand.NotifyCanExecuteChanged();
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

    public async Task UpgradeToVersionAsync(PackageRowViewModel row)
    {
        if (row.NewerVersion is { } version)
        {
            await ChangeVersionAsync(row.Name, row.DisplayName, row.Version, version);
        }
    }

    private async Task ChangeVersionAsync(
        string name,
        string displayName,
        string currentVersion,
        string targetVersion)
    {
        if (_project is null || IsBusy || _changingPackages)
        {
            return;
        }

        if (!PackageVersion.TryParse(currentVersion, out var current)
            || !PackageVersion.TryParse(targetVersion, out var target)
            || current == target)
        {
            ReloadPackages();
            return;
        }

        var upgrading = target > current;

        displayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        var applied = await ConfirmAndApplyAsync(
            upgradeDependents => PlanSwitchVersionAsync(
                name,
                targetVersion,
                upgradeDependents,
                reportFailure: !upgradeDependents),
            localization.Format(
                upgrading ? "ProjectDetail.Upgrade.Done" : "ProjectDetail.SwitchVersion.Done",
                displayName,
                targetVersion));

        if (!applied)
        {
            ReloadPackages();
        }
    }

    private async Task<bool> ConfirmAndApplyAsync(
        Func<bool, Task<PendingProjectChanges?>> plan,
        string successMessage)
    {
        if (_project is null || IsBusy || _changingPackages)
        {
            return false;
        }

        _changingPackages = true;

        try
        {
            Status = null;

            var changes = await plan(false);

            if (changes is null
                || (changes.PackageChanges.Count == 0
                    && changes.RemoveLegacyFiles.Count == 0
                    && changes.RemoveLegacyFolders.Count == 0))
            {
                return false;
            }

            var affected = PackageOperations.FindAffectedDependents(
                _project,
                changes,
                _allowPrerelease);

            var explained = affected.Select(x => x.Name).ToList();

            if (BlockingConflicts(changes, explained) is { Count: > 0 } blocking)
            {
                dialogs.ShowNotice(
                    localization["ProjectDetail.VersionChange.ConflictTitle"],
                    localization["ProjectDetail.VersionChange.ConflictHint"]
                        + Environment.NewLine
                        + Environment.NewLine
                        + string.Join(Environment.NewLine, blocking),
                    localization["Dialog.Confirm"]);

                return false;
            }

            PendingProjectChanges? cascade = null;

            if (affected.Count > 0)
            {
                cascade = await plan(true);

                if (cascade is not null
                    && (PackageOperations.FindAffectedDependents(
                            _project,
                            cascade,
                            _allowPrerelease).Count > 0
                        || BlockingConflicts(cascade) is { Count: > 0 }))
                {
                    cascade = null;
                }
            }

            var messageInlines = ApplyInlines(changes, explained);
            var message = InlinesToText(messageInlines);
            var detailTitle = affected.Count == 0
                ? null
                : localization["ProjectDetail.ApplyChanges.Incompatible"];
            var detailBodyInlines = affected.Count == 0
                ? null
                : AffectedInlines(affected, cascade);
            var detailBody = detailBodyInlines is null
                ? null
                : InlinesToText(detailBodyInlines);

            var applied = changes;

            if (cascade is null)
            {
                if (!dialogs.ShowConfirm(
                        localization["ProjectDetail.ApplyChanges.Title"],
                        message,
                        localization["ProjectDetail.Apply"],
                        detailTitle: detailTitle,
                        detailBody: detailBody,
                        width: 680,
                        messageInlines: messageInlines,
                        detailBodyInlines: detailBodyInlines))
                {
                    return false;
                }
            }
            else
            {
                var choice = dialogs.ShowChoice(
                    localization["ProjectDetail.ApplyChanges.Title"],
                    message,
                    localization["ProjectDetail.Apply.OnlyThis"],
                    localization["ProjectDetail.Apply.All"],
                    detailTitle: detailTitle,
                    detailBody: detailBody,
                    width: 680,
                    messageInlines: messageInlines,
                    detailBodyInlines: detailBodyInlines);

                if (choice == ConfirmChoice.Cancel)
                {
                    return false;
                }

                if (choice == ConfirmChoice.Extra)
                {
                    applied = cascade;
                }
            }

            IsBusy = true;

            try
            {
                await ApplyChangesAsync(applied, successMessage);
                return true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "应用包变更失败：{Project}", ProjectPath);

                Status = localization.Format("ProjectDetail.ApplyFailed", exception.Message);

                await LoadAsync();
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }
        finally
        {
            _changingPackages = false;
        }
    }

    private async Task<PendingProjectChanges?> PlanAddAsync(
        IReadOnlyList<PackageInfo> packages,
        AddPackageOperation operation,
        bool upgradeDependents,
        bool reportFailure = true)
    {
        if (_project is null || packages.Count == 0)
        {
            return null;
        }

        packages = [.. packages.DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase)];

        if (operation == AddPackageOperation.UpgradeLocked)
        {
            packages = [.. packages.Where(x => _project.GetLocked(x.Name) is not null)];

            if (packages.Count == 0)
            {
                return null;
            }
        }

        try
        {
            var outcome = await PackageOperations.AddAsync(
                _project,
                Catalog(),
                packages,
                operation,
                _allowPrerelease,
                CancellationToken.None,
                upgradeDependents);

            if (outcome.Status != AddPackageStatus.Success)
            {
                logger.LogWarning(
                    "包操作失败：{Project} {Status} 缺少 {Missing}",
                    ProjectPath,
                    outcome.Status,
                    Join(Describe(outcome.MissingDependencies)));

                if (reportFailure)
                {
                    if (outcome.MissingDependencies.Count > 0)
                    {
                        ReportMissingDependencies(outcome.MissingDependencies);
                    }
                    else
                    {
                        Status = localization.Format("ProjectDetail.PackageOperationFailed", outcome.Status);
                    }
                }

                return null;
            }

            return outcome.Changes;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "包操作失败：{Project}", ProjectPath);

            if (reportFailure)
            {
                Status = localization.Format("ProjectDetail.PackageOperationFailed", exception.Message);
            }

            return null;
        }
    }

    private async Task<PendingProjectChanges?> PlanSwitchVersionAsync(
        string name,
        string version,
        bool upgradeDependents = false,
        bool reportFailure = true)
    {
        if (_project is null || !PackageVersion.TryParse(version, out var parsed))
        {
            return null;
        }

        try
        {
            var outcome = await PackageOperations.SwitchVersionAsync(
                _project,
                Catalog(),
                name,
                parsed,
                _allowPrerelease,
                CancellationToken.None,
                upgradeDependents);

            if (outcome.Status == AddPackageStatus.VersionNotFound)
            {
                logger.LogWarning("指定的包版本不存在：{Project} {Package} {Version}", ProjectPath, name, version);

                if (reportFailure)
                {
                    Status = localization.Format("ProjectDetail.VersionUnavailable", name, version);
                }

                return null;
            }

            if (outcome.Status != AddPackageStatus.Success)
            {
                logger.LogWarning(
                    "切换包版本失败：{Project} {Package} {Version} 缺少 {Missing}",
                    ProjectPath,
                    name,
                    version,
                    ReportMissingDependencies(outcome.MissingDependencies));
                return null;
            }

            return outcome.Changes;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "切换包版本失败：{Project} {Package} {Version}", ProjectPath, name, version);

            if (reportFailure)
            {
                Status = localization.Format("ProjectDetail.VersionSwitchFailed", exception.Message);
            }

            return null;
        }
    }

    private VersionSelector OfferedVersionSelector() =>
        VersionSelector.LatestFor(_project?.UnityVersion, _allowPrerelease);

    private ICollectionView CreatePackageView()
    {
        var view = CollectionViewSource.GetDefaultView(Packages);

        view.Filter = MatchesSearch;

        return view;
    }

    private bool MatchesSearch(object item)
    {
        if (item is not PackageRowViewModel row)
        {
            return false;
        }

        var query = SearchText.Trim();

        return query.Length == 0
            || ApproximateMatch.Rank(query, row.Name, row.DisplayName) is not null;
    }
}

public sealed partial class PackageRowViewModel : ObservableObject
{
    private readonly IReadOnlyList<PackageInfo> _candidates;
    private readonly Action<string>? _onVersionChosen;

    private PackageRowViewModel(
        string name,
        string displayName,
        string version,
        bool isInstalled,
        bool isUnlocked,
        bool isCustom,
        bool isMissing,
        IReadOnlyList<string> availableVersions,
        IReadOnlyList<PackageInfo> candidates,
        string? newerVersion,
        Action<string>? onVersionChosen)
    {
        Name = name;
        DisplayName = displayName;
        Version = version;
        IsInstalled = isInstalled;
        IsUnlocked = isUnlocked;
        IsCustom = isCustom;
        IsMissing = isMissing;
        AvailableVersions = availableVersions;
        _candidates = candidates;
        _onVersionChosen = onVersionChosen;
        _selectedVersion = isInstalled ? version : availableVersions[0];
        NewerVersion = newerVersion;
    }

    public static PackageRowViewModel Installed(
        string name,
        string displayName,
        string version,
        bool isUnlocked,
        bool isCustom,
        bool isMissing,
        IReadOnlyList<string> availableVersions,
        string? newerVersion,
        Action<string>? onVersionChosen) =>
        new(
            name,
            displayName,
            version,
            true,
            isUnlocked,
            isCustom,
            isMissing,
            availableVersions,
            [],
            newerVersion,
            onVersionChosen);

    public static PackageRowViewModel Available(
        IReadOnlyList<PackageInfo> candidates,
        string displayName) =>
        new(
            candidates[0].Name,
            displayName,
            candidates[0].Version.ToString(),
            false,
            false,
            false,
            false,
            [.. candidates.Select(x => x.Version.ToString())],
            candidates,
            null,
            null);

    public string Name { get; }

    public string DisplayName { get; }

    public string Version { get; }

    public bool IsInstalled { get; }

    public bool IsUnlocked { get; }

    public bool IsCustom { get; }

    public bool IsMissing { get; }

    public bool CanAdd => !IsInstalled && _candidates.Count > 0;

    public bool CanRemove => IsInstalled && !IsUnlocked && !IsMissing;

    public bool CanReinstall => IsInstalled && !IsUnlocked && !IsCustom;

    public IReadOnlyList<string> AvailableVersions { get; }

    public bool CanChooseVersion =>
        (!IsInstalled || (!IsUnlocked && !IsCustom)) && AvailableVersions.Count > 1;

    public bool HasVersions => AvailableVersions.Count > 0;

    public bool CanSelect => IsInstalled
        ? CanRemove || CanReinstall || HasNewerVersion
        : CanAdd;

    public string? NewerVersion { get; }

    public bool HasNewerVersion => NewerVersion is not null;

    public PackageInfo? Package
    {
        get
        {
            if (IsInstalled || _candidates.Count == 0)
            {
                return null;
            }

            var index = -1;

            for (var i = 0; i < AvailableVersions.Count; i++)
            {
                if (AvailableVersions[i] == SelectedVersion)
                {
                    index = i;
                    break;
                }
            }

            return _candidates[Math.Max(0, index)];
        }
    }

    [ObservableProperty]
    private string _selectedVersion;

    partial void OnSelectedVersionChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (IsInstalled && value == Version)
        {
            return;
        }

        _onVersionChosen?.Invoke(value);
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
