using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Collections;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Unity;
using VRCLearn.Infrastructure.Shell;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.ViewModels;

public sealed partial class ProjectsViewModel : PageViewModelBase, IPageViewModel
{
    private readonly ProjectService _service;
    private readonly ProjectDetailViewModel _detail;
    private readonly NavigationService _navigation;
    private readonly LocalizationService _localization;
    private readonly GuiConfigStore _configStore;
    private readonly DialogService _dialogs;
    private readonly UnityProjectOpener _opener;
    private readonly ProjectBackupService _backups;
    private readonly ProjectBackupRunner _backupRunner;
    private readonly AutoBackupService _autoBackups;
    private readonly ProgressService _progress;
    private readonly ILogger<ProjectsViewModel> _logger;
    private readonly IReadOnlyList<Option> _sortingKeys;
    private readonly IReadOnlyList<Option> _sortingDirections;
    private readonly HashSet<string> _runningProjects = new(StringComparer.OrdinalIgnoreCase);

    public ProjectsViewModel(
        ProjectService service,
        ProjectDetailViewModel detail,
        NavigationService navigation,
        LocalizationService localization,
        GuiConfigStore configStore,
        DialogService dialogs,
        UnityProjectOpener opener,
        ProjectBackupService backups,
        ProjectBackupRunner backupRunner,
        AutoBackupService autoBackups,
        ProgressService progress,
        ToastService toasts,
        ILogger<ProjectsViewModel> logger)
        : base(toasts)
    {
        _logger = logger;
        _service = service;
        _detail = detail;
        _navigation = navigation;
        _localization = localization;
        _configStore = configStore;
        _dialogs = dialogs;
        _opener = opener;
        _backups = backups;
        _backupRunner = backupRunner;
        _autoBackups = autoBackups;
        _progress = progress;

        _sortingKeys =
        [
            new("name", () => localization["Projects.Sort.Key.Name"]),
            new("type", () => localization["Projects.Sort.Key.Type"]),
            new("unity", () => localization["Projects.Sort.Key.Unity"]),
            new("createdAt", () => localization["Projects.Sort.Key.Created"]),
            new("lastModified", () => localization["Projects.Sort.Key.Modified"]),
        ];
        _sortingDirections =
        [
            new("Ascending", () => localization["Projects.Sort.Ascending"]),
            new("Descending", () => localization["Projects.Sort.Descending"]),
        ];

        _selectedSorting = ProjectSorting.Normalize(_configStore.Current.ProjectSorting);

        _localization.PropertyChanged += (_, _) =>
        {
            LocalizedOptions.Refresh(_sortingKeys);
            LocalizedOptions.Refresh(_sortingDirections);
            _ = ReloadAsync();
        };
    }

    protected override ILogger Logger => _logger;

    protected override LocalizationService Localization => _localization;

    public IReadOnlyList<Option> SortingKeys => _sortingKeys;

    public IReadOnlyList<Option> SortingDirections => _sortingDirections;

    /// <summary>排序键 + 方向，形如 <c>nameReversed</c>。</summary>
    [ObservableProperty]
    private string _selectedSorting;

    partial void OnSelectedSortingChanged(string value)
    {
        _configStore.Current.ProjectSorting = value;
        _configStore.Save();

        OnPropertyChanged(nameof(SortingKey));
        OnPropertyChanged(nameof(SortingDirection));

        _logger.LogInformation("项目排序：{Sorting}", value);

        _ = ReloadAsync();
    }

    public string SortingKey
    {
        get => ProjectSorting.KeyOf(SelectedSorting);
        set => SelectedSorting = ProjectSorting.WithDirection(value, ProjectSorting.IsReversed(SelectedSorting));
    }

    public string SortingDirection
    {
        get => ProjectSorting.IsReversed(SelectedSorting) ? "Descending" : "Ascending";
        set => SelectedSorting = ProjectSorting.WithDirection(ProjectSorting.KeyOf(SelectedSorting), value == "Descending");
    }

    public ObservableCollection<ProjectItemViewModel> Projects { get; } = [];

    public bool HasProjects => Projects.Count > 0;

    private void ShowStatus(string? text) => Status = text;

    public void Release()
    {
        if (IsBusy)
        {
            return;
        }

        Projects.Clear();

        OnPropertyChanged(nameof(HasProjects));
    }

    public void RefreshUnityStates()
    {
        foreach (var project in Projects)
        {
            var running = _opener.Status(project.ProjectPath) != UnityProjectStatus.Closed;
            project.IsUnityRunning = running;

            if (running)
            {
                if (_runningProjects.Add(project.ProjectPath))
                {
                    _autoBackups.ProjectStarted();
                }
            }
            else
            {
                _runningProjects.Remove(project.ProjectPath);
            }
        }

        var remaining = _autoBackups.TimeUntilNextBackup();

        foreach (var project in Projects)
        {
            if (!project.IsUnityRunning)
            {
                project.SetAutoBackup(false, "");
                continue;
            }

            if (_autoBackups.HasFailed(project.ProjectPath))
            {
                project.SetAutoBackup(true, _localization["Projects.AutoBackupFailed"]);
                continue;
            }

            if (remaining is not { } value)
            {
                project.SetAutoBackup(false, "");
                continue;
            }

            project.SetAutoBackup(
                false,
                _localization.Format("Projects.AutoBackup", CountdownText(value)));
        }
    }

    private static string CountdownText(TimeSpan remaining) =>
        remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{remaining.Minutes:00}:{remaining.Seconds:00}";

    public async Task AddAsync(string projectPath)
    {
        var status = _service.Add(projectPath);

        ShowStatus(status switch
        {
            AddProjectStatus.Added => _localization.Format("Projects.Status.Added", projectPath),
            AddProjectStatus.AlreadyAdded => _localization.Format("Projects.Status.AlreadyAdded", projectPath),
            _ => _localization.Format("Projects.Status.NotAProject", projectPath),
        });

        if (status == AddProjectStatus.Added)
        {
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private async Task RemoveAsync(ProjectItemViewModel project)
    {
        var choice = _dialogs.ShowChoice(
            _localization["Projects.Remove.ConfirmTitle"],
            _localization.Format("Projects.Remove.ConfirmMessage", project.Name),
            _localization["Projects.Remove.ConfirmButton"],
            _localization["Projects.Remove.DiskButton"],
            extraDanger: true);

        if (choice == ConfirmChoice.Cancel)
        {
            return;
        }

        if (choice == ConfirmChoice.Extra)
        {
            await RemoveFromDiskAsync(project);
            return;
        }

        ShowStatus(_service.Remove(project.ProjectPath) == RemoveProjectStatus.Success
            ? _localization.Format("Projects.Status.Removed", project.Name)
            : _localization["Projects.Status.NotFound"]);

        await ReloadAsync();
    }

    private async Task RemoveFromDiskAsync(ProjectItemViewModel project)
    {
        if (!_dialogs.ShowConfirm(
                _localization["Projects.Remove.DiskTitle"],
                _localization.Format("Projects.Remove.DiskMessage", project.Name),
                _localization["Projects.Remove.DiskButton"],
                danger: true))
        {
            return;
        }

        if (_opener.IsBusy(project.ProjectPath))
        {
            _dialogs.ShowNotice(
                _localization["Projects.Remove.NoticeTitle"],
                _localization["Projects.Remove.UnityBusy"],
                _localization["Dialog.Confirm"]);

            return;
        }

        var status = await WithBusyAsync(() => _progress.RunAsync(
            _localization["Progress.Delete"],
            reporter => Task.Run(() => _service.RemoveFromDisk(project.ProjectPath, reporter))));

        ShowStatus(RemovalStatus(
            project,
            status,
            _localization.Format("Projects.Status.RemovedFromDisk", project.Name)));

        _ = ReloadAsync();
    }

    private string RemovalStatus(ProjectItemViewModel project, RemoveProjectStatus status, string success) =>
        status switch
        {
            RemoveProjectStatus.Success => success,
            RemoveProjectStatus.Failed => _localization.Format("Projects.Status.RemoveFailed", project.Name),
            _ => _localization["Projects.Status.NotFound"],
        };

    [RelayCommand]
    private async Task Archive(ProjectItemViewModel project)
    {
        if (IsBusy)
        {
            return;
        }

        if (_opener.IsBusy(project.ProjectPath))
        {
            _dialogs.ShowNotice(
                _localization["Projects.Archive.NoticeTitle"],
                _localization["Projects.Archive.UnityBusy"],
                _localization["Dialog.Confirm"]);

            return;
        }

        if (!_dialogs.ShowConfirm(
                _localization["Projects.Archive.ConfirmTitle"],
                _localization.Format("Projects.Archive.ConfirmMessage", project.Name),
                _localization["Projects.Archive.ConfirmButton"],
                danger: true))
        {
            return;
        }

        ShowStatus(_localization.Format("Projects.Status.Archiving", project.Name));

        await RunBusyAsync(
            "归档项目失败：{Project}",
            project.Name,
            async () =>
        {
            var unityProject = await _service.OpenAsync(project.ProjectPath, CancellationToken.None);
            var config = _configStore.Current;

            var (record, status) = await _progress.RunAsync(
                _localization["Progress.Archive"],
                async reporter =>
                {
                    var backup = await _backups.CreateAsync(
                        unityProject,
                        project.Name,
                        ProjectBackupService.ParseFormat(config.BackupFormat),
                        DateTime.Now,
                        reporter,
                        CancellationToken.None,
                        archive: true);

                    return (backup, await Task.Run(() => _service.RemoveFromDisk(project.ProjectPath, reporter)));
                });

            ShowStatus(RemovalStatus(
                project,
                status,
                _localization.Format("Projects.Status.Archived", record.FileName)));
        },
            "Projects.Status.BackupFailed");

        _ = ReloadAsync();
    }

    [RelayCommand]
    private async Task Backup(ProjectItemViewModel project)
    {
        if (IsBusy)
        {
            return;
        }

        var note = _dialogs.ShowNote(
            _localization["Backups.Note.Title.New"],
            "",
            _localization["Backups.Note.Start"],
            _localization["Backups.Note.Hint"]);

        if (note is null)
        {
            return;
        }

        ShowStatus(_localization.Format("Projects.Status.BackingUp", project.Name));

        await RunBusyAsync(
            "备份项目失败：{Project}",
            project.Name,
            async () =>
            {
                var record = await _backupRunner.RunAsync(
                    project.ProjectPath,
                    project.Name,
                    note,
                    auto: false,
                    CancellationToken.None);

                ShowStatus(_localization.Format("Projects.Status.BackedUp", record.FileName));
            },
            "Projects.Status.BackupFailed");
    }

    [RelayCommand]
    private void ToggleFavorite(ProjectItemViewModel project)
    {
        var removed = _configStore.Current.FavoriteProjects.Toggle(project.ProjectPath);

        _logger.LogInformation("{Action}项目：{Project}", removed ? "取消收藏" : "收藏", project.Name);

        _configStore.Save();
        _ = ReloadAsync();
    }

    [RelayCommand]
    private void Open(ProjectItemViewModel project)
    {
        _detail.Open(project.ProjectPath);
        _navigation.GoTo("project-detail");
    }

    [RelayCommand]
    private void NewProject() => _dialogs.ShowNewProject(null);

    [RelayCommand]
    private async Task LaunchUnity(ProjectItemViewModel project)
    {
        if (project.Version is not { } version)
        {
            ShowStatus(_localization.Format("Projects.Status.UnityNotFound", project.UnityVersion));
            return;
        }

        ShowStatus(_localization["Unity.StartingHub"]);

        var outcome = await _opener.OpenAsync(
            project.ProjectPath,
            _opener.FindEditor(version),
            _opener.ArgumentsFor(project.ProjectPath),
            CancellationToken.None);

        ShowStatus(UnityLaunchStatus.For(outcome, _localization, project.Name, project.UnityVersion));

        RefreshUnityStates();
    }

    [RelayCommand]
    private void BringUnityToFront(ProjectItemViewModel project)
    {
        var result = _opener.BringToFront(project.ProjectPath);

        ShowStatus(_localization[$"Unity.BringToFront.{result}"]);

        RefreshUnityStates();
    }

    [RelayCommand]
    private static void OpenFolder(ProjectItemViewModel project) => ShellLauncher.OpenFolder(project.ProjectPath);

    [RelayCommand]
    private async Task Rename(ProjectItemViewModel project)
    {
        if (_opener.IsBusy(project.ProjectPath))
        {
            _dialogs.ShowNotice(
                _localization["Projects.Rename.NoticeTitle"],
                _localization["Projects.Status.UnityBusy"],
                _localization["Dialog.Confirm"]);

            return;
        }

        var current = Path.GetFileName(project.ProjectPath);

        var newName = _dialogs.ShowRename(
            _localization["Projects.Rename.Title"],
            current,
            _localization["Projects.Rename.Confirm"]);

        if (newName is null || string.Equals(newName, current, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        switch (_service.Rename(project.ProjectPath, newName))
        {
            case RenameProjectStatus.Success:
                RememberRenamedPath(project.ProjectPath, Path.Combine(Path.GetDirectoryName(project.ProjectPath)!, newName));
                ShowStatus(_localization.Format("Projects.Status.Renamed", newName));

                await ReloadAsync();
                break;

            case RenameProjectStatus.AlreadyExists:
                ShowStatus(_localization.Format("Projects.Status.RenameExists", newName));
                break;

            case RenameProjectStatus.InvalidName:
                ShowStatus(_localization.Format("Templates.Status.NameInvalid", newName));
                break;

            case RenameProjectStatus.Failed:
                ShowStatus(_localization["Projects.Status.RenameFailed"]);

                await ReloadAsync();
                break;

            default:
                ShowStatus(_localization["Projects.Status.NotFound"]);

                await ReloadAsync();
                break;
        }
    }

    private void RememberRenamedPath(string oldPath, string newPath)
    {
        var favorites = _configStore.Current.FavoriteProjects;
        var index = favorites.FindIndex(x => string.Equals(x, oldPath, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            favorites[index] = newPath;
        }

        if (_configStore.Current.ProjectUnityArguments.Remove(oldPath, out var arguments))
        {
            _configStore.Current.ProjectUnityArguments[newPath] = arguments;
        }

        _configStore.Save();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        ShowStatus(null);
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        await WithBusyAsync(async () =>
        {
            var items = new List<ProjectItemViewModel>();
            var favorites = _configStore.Current.FavoriteProjects;

            foreach (var path in _service.List())
            {
                var item = new ProjectItemViewModel(path);

                await item.LoadAsync(
                    _service,
                    _localization,
                    favorites.Contains(path),
                    _opener.Status(path) != UnityProjectStatus.Closed,
                    CancellationToken.None);

                if (item.IsUnreadable)
                {
                    _logger.LogWarning("项目读不出来：{Project} {Summary}", path, item.Summary);
                }

                items.Add(item);
            }

            Projects.Clear();

            foreach (var item in ProjectSorting.Apply(items, SelectedSorting).OrderByDescending(x => x.IsFavorite))
            {
                Projects.Add(item);
            }
        });

        OnPropertyChanged(nameof(HasProjects));
    }
}
