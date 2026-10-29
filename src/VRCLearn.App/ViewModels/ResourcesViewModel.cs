using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Resources;
using VRCLearn.Core.Text;
using VRCLearn.Core.Unity;
using VRCLearn.Infrastructure.Shell;

namespace VRCLearn.App.ViewModels;

public sealed record ProjectChoice(string Path, string Name, bool IsUsable, bool IsRunning = false)
{
    public bool IsSelectable => IsUsable && !IsRunning;
}

public sealed partial class ResourcesViewModel : PageViewModelBase, IPageViewModel
{
    private const int SearchDelayMilliseconds = 250;

    private readonly IResourceStore _store;
    private readonly ProjectService _projects;
    private readonly UnityProjectOpener _opener;
    private readonly DialogService _dialogs;
    private readonly LocalizationService _localization;
    private readonly ProgressService _progress;
    private readonly ILogger<ResourcesViewModel> _logger;
    private readonly HashSet<string> _selectedTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly TransientAction _search;

    private IReadOnlyList<ResourceItemViewModel> _all = [];

    public ResourcesViewModel(
        IResourceStore store,
        ProjectService projects,
        UnityProjectOpener opener,
        DialogService dialogs,
        LocalizationService localization,
        ProgressService progress,
        ToastService toasts,
        ILogger<ResourcesViewModel> logger)
        : base(toasts)
    {
        _store = store;
        _projects = projects;
        _opener = opener;
        _dialogs = dialogs;
        _localization = localization;
        _progress = progress;
        _logger = logger;
        _search = new TransientAction(TimeSpan.FromMilliseconds(SearchDelayMilliseconds), ApplyFilter);
    }

    protected override ILogger Logger => _logger;

    protected override LocalizationService Localization => _localization;

    public ObservableCollection<ResourceItemViewModel> Resources { get; } = [];

    [ObservableProperty]
    private string _searchText = "";

    public bool HasResources => Resources.Count > 0;

    public string EmptyText => _all.Count == 0
        ? _localization["Resources.Empty"]
        : _localization["Resources.EmptyFiltered"];

    public int SelectedCount => _all.Count(x => x.IsSelected);

    private bool CanImportSelected() => SelectedCount > 0;

    public void Reload()
    {
        foreach (var item in _all)
        {
            item.PropertyChanged -= OnItemChanged;
        }

        _all =
        [
            .. _store.Current.Resources
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => new ResourceItemViewModel(
                    x,
                    _store.CoverPath(x),
                    ResourceOperations.IsExpired(x, File.Exists))),
        ];

        foreach (var item in _all)
        {
            item.PropertyChanged += OnItemChanged;
        }

        _selectedTags.IntersectWith(_store.Current.Tags);

        ApplyFilter();
    }

    public void Release()
    {
        foreach (var item in _all)
        {
            item.PropertyChanged -= OnItemChanged;
        }

        _all = [];
        Resources.Clear();
        _selectedTags.Clear();
        SearchText = "";

        OnPropertyChanged(nameof(HasResources));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(SelectedCount));
        ImportSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value) => _search.Run();

    [RelayCommand]
    private void NewResource()
    {
        if (_dialogs.ShowResourceEditor(null)?.Result is { } result)
        {
            Save(result);
        }
    }

    [RelayCommand]
    private void EditResource(ResourceItemViewModel item)
    {
        if (_dialogs.ShowResourceEditor(item.Definition) is not { } outcome)
        {
            return;
        }

        if (outcome.Remove)
        {
            Remove(item.Definition);
        }
        else if (outcome.Result is { } result)
        {
            Save(result);
        }
    }

    [RelayCommand]
    private void Filter()
    {
        if (_dialogs.ShowTagFilter(_store.Current.Tags, _selectedTags) is not { } selected)
        {
            return;
        }

        _selectedTags.Clear();

        foreach (var tag in selected)
        {
            _selectedTags.Add(tag);
        }

        ApplyFilter();
    }

    [RelayCommand]
    private void ManageTags()
    {
        _dialogs.ShowTagManager();
        Reload();
    }

    [RelayCommand]
    private void OpenManual(ResourceItemViewModel item)
    {
        var manual = item.Definition.Manual.Trim();

        if (manual.Length == 0)
        {
            return;
        }

        try
        {
            if (ResourceManual.IsUrl(manual))
            {
                ShellLauncher.OpenUrl(ResourceManual.ToOpenableUrl(manual));
            }
            else if (!File.Exists(manual))
            {
                Status = _localization["Resources.Manual.Missing"];
            }
            else if (!ResourceManual.IsSupportedFile(manual))
            {
                Status = _localization["Resources.Manual.Unsupported"];
            }
            else
            {
                ShellLauncher.OpenFile(manual);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "打开说明书失败：{Path}", manual);
            Status = _localization.Format("Resources.Manual.OpenFailed", manual);
        }
    }

    [RelayCommand]
    private Task ImportResource(ResourceItemViewModel item) =>
        ImportAsync([item.Definition]);

    [RelayCommand(CanExecute = nameof(CanImportSelected))]
    private Task ImportSelected() =>
        ImportAsync([.. _all.Where(x => x.IsSelected).Select(x => x.Definition)]);

    private void Remove(ResourceDefinition definition)
    {
        if (!_dialogs.ShowConfirm(
                _localization["Resources.Remove.Title"],
                _localization.Format("Resources.Remove.Message", definition.Name),
                _localization["Resources.Remove.Confirm"],
                danger: true))
        {
            return;
        }

        _store.Current.Resources.RemoveAll(x => x.Id == definition.Id);
        _store.DeleteCover(definition);
        _store.Save();
        Reload();
    }

    private void Save(ResourceEditResult result)
    {
        var collection = _store.Current;
        var index = collection.Resources.FindIndex(x => x.Id == result.Definition.Id);

        if (index >= 0)
        {
            collection.Resources[index] = result.Definition;
        }
        else
        {
            collection.Resources.Add(result.Definition);
        }

        if (result.CoverPng is { } png)
        {
            _store.SaveCover(result.Definition, png);
        }

        _store.Save();
        Reload();
    }

    private async Task ImportAsync(IReadOnlyList<ResourceDefinition> definitions)
    {
        if (IsBusy || definitions.Count == 0)
        {
            return;
        }

        var choices = _projects.List()
            .Select(x => new ProjectChoice(
                x,
                PathUtil.NameOf(x),
                ProjectService.IsProject(x),
                _opener.Status(x) != UnityProjectStatus.Closed))
            .OrderByDescending(x => x.IsRunning)
            .ToList();

        if (choices.Count == 0)
        {
            Status = _localization["Resources.Import.NoProject"];
            return;
        }

        if (_dialogs.ShowProjectPicker(choices, definitions.Count) is not { } path)
        {
            return;
        }

        if (!ProjectService.IsProject(path))
        {
            Status = _localization["Resources.Picker.Unreadable"];
            return;
        }

        if (_opener.Status(path) != UnityProjectStatus.Closed)
        {
            Status = _localization["Resources.Import.Running"];
            return;
        }

        UnityProject project;

        try
        {
            project = await _projects.OpenAsync(path, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "打开项目失败：{Path}", path);
            Status = _localization.Format("Resources.Import.Failed", exception.Message);
            return;
        }

        var existing = definitions
            .SelectMany(x => x.UnityPackagePaths)
            .Where(File.Exists)
            .ToList();

        if (existing.Count == 0)
        {
            Status = _localization["Resources.Import.Nothing"];
            return;
        }

        try
        {
            DiskSpace.Ensure(project.ProjectDirectory, existing.Sum(x => new FileInfo(x).Length));
        }
        catch (InsufficientDiskSpaceException exception)
        {
            Status = _localization.FormatDiskSpace(exception);
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = _localization.Format("Resources.Import.Failed", exception.Message);
            return;
        }

        var imported = 0;
        var failed = 0;

        try
        {
            await _progress.RunAsync(_localization["Resources.Import.Progress"], async reporter =>
            {
                var total = definitions.Sum(x => x.UnityPackagePaths.Count);
                var done = 0;

                foreach (var definition in definitions)
                {
                    var outcome = await ResourceImporter.ImportAsync(
                        definition.UnityPackagePaths,
                        project.Io,
                        File.Exists,
                        new Progress<FileProgress>(x =>
                            reporter.Report(new FileProgress(total, done + x.Done))),
                        CancellationToken.None);

                    imported += outcome.Imported;
                    failed += outcome.Failed.Count + outcome.Missing.Count;
                    done += definition.UnityPackagePaths.Count;
                }

                return true;
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "导入资源失败：{Path}", path);
            Status = _localization.Format("Resources.Import.Failed", exception.Message);
            return;
        }

        Status = failed == 0
            ? _localization.Format("Resources.Import.Done", imported)
            : _localization.Format("Resources.Import.Partial", imported, failed);
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var matches = new List<(int Rank, ResourceItemViewModel Item)>();

        foreach (var item in _all)
        {
            if (_selectedTags.Count > 0
                && !item.Tags.Any(x => _selectedTags.Contains(x)))
            {
                continue;
            }

            var rank = 0;

            if (query.Length > 0)
            {
                if (ApproximateMatch.Rank(query, item.Name) is not { } value)
                {
                    continue;
                }

                rank = value;
            }

            matches.Add((rank, item));
        }

        Resources.Clear();

        foreach (var item in matches
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => x.Item))
        {
            Resources.Add(item);
        }

        OnPropertyChanged(nameof(HasResources));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(SelectedCount));
        ImportSelectedCommand.NotifyCanExecuteChanged();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ResourceItemViewModel.IsSelected))
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedCount));
        ImportSelectedCommand.NotifyCanExecuteChanged();
    }
}
