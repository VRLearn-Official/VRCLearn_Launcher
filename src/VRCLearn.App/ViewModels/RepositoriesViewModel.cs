using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Collections;
using VRCLearn.Core.Repositories;
using VRCLearn.Core.Vpm;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.ViewModels;

public sealed partial class RepositoriesViewModel : PageViewModelBase, IPageViewModel, IReorderableList
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders = new Dictionary<string, string>();

    private readonly RepositoryService _service;
    private readonly LocalizationService _localization;
    private readonly GuiConfigStore _configStore;
    private readonly DialogService _dialogs;
    private readonly NavigationService _navigation;
    private readonly ILogger<RepositoriesViewModel> _logger;

    public RepositoriesViewModel(
        RepositoryService service,
        LocalizationService localization,
        GuiConfigStore configStore,
        DialogService dialogs,
        NavigationService navigation,
        UserPackagesViewModel userPackages,
        ToastService toasts,
        ILogger<RepositoriesViewModel> logger)
        : base(toasts)
    {
        _service = service;
        _localization = localization;
        _configStore = configStore;
        _dialogs = dialogs;
        _navigation = navigation;
        _logger = logger;
        UserPackages = userPackages;
        _localization.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(RefreshStatusText));
            Reload();
        };
    }

    protected override ILogger Logger => _logger;

    protected override LocalizationService Localization => _localization;

    public UserPackagesViewModel UserPackages { get; }

    public ObservableCollection<RepositoryItemViewModel> Repositories { get; } = [];

    public event Action<RefreshSummary>? Refreshed;

    [ObservableProperty]
    private string _newRepositoryUrl = "";

    public bool HasRepositories => Repositories.Count > 0;

    /// <summary>深链 <c>vcc://vpm/addRepo</c>：问一句再添加。</summary>
    public async Task AddFromLinkAsync(RepositoryInfo repository)
    {
        var url = repository.Url.AbsoluteUri;

        if (_service.List().Any(x => x.Url == repository.Url))
        {
            _dialogs.ShowNotice(
                _localization["Repositories.LinkAlreadyTitle"],
                _localization.Format("Repositories.LinkAlreadyMessage", url),
                _localization["Dialog.Confirm"]);

            return;
        }

        var message = repository.Headers.Count == 0
            ? _localization.Format("Repositories.LinkMessage", url)
            : _localization.Format("Repositories.LinkMessageWithHeaders", url, repository.Headers.Count);

        if (!_dialogs.ShowConfirm(_localization["Repositories.LinkTitle"], message, _localization["Repositories.Add"]))
        {
            return;
        }

        _navigation.GoTo("repositories");

        Status = MessageFor(await AddRepositoryAsync(url, repository.Headers), url);
    }

    [RelayCommand]
    private async Task AddAsync() => Status = MessageFor(await AddRepositoryAsync(NewRepositoryUrl), NewRepositoryUrl);

    [RelayCommand]
    private void Remove(RepositoryItemViewModel repository)
    {
        if (!_dialogs.ShowConfirm(
                _localization["Repositories.Remove.Title"],
                _localization.Format("Repositories.Remove.Message", repository.Title),
                _localization["Repositories.Remove"],
                danger: true))
        {
            return;
        }

        var status = _service.Remove(repository.Index, repository.Id);

        Status = status switch
        {
            RemoveRepositoryStatus.Success => _localization.Format("Repositories.Status.Removed", repository.Title),
            RemoveRepositoryStatus.Stale => _localization["Repositories.Status.Stale"],
            RemoveRepositoryStatus.SaveFailed => _localization["Repositories.Status.SaveFailed"],
            _ => _localization["Repositories.Status.IndexOutOfRange"],
        };

        Reload();
    }

    /// <summary>把一条挪到 <paramref name="insertIndex"/> 前面；下标按拖起前那份列表，末尾 = 列表长度。</summary>
    public void Move(RepositoryItemViewModel dragged, int insertIndex)
    {
        var status = _service.Reorder(dragged.Index, insertIndex, [.. Repositories.Select(x => x.Id)]);

        Status = status switch
        {
            ReorderRepositoryStatus.Success => null,
            ReorderRepositoryStatus.SaveFailed => _localization["Repositories.Status.SaveFailed"],
            _ => _localization["Repositories.Status.Stale"],
        };

        Reload();
    }

    void IReorderableList.Move(object item, int insertBefore) =>
        Move((RepositoryItemViewModel)item, insertBefore);

    int IReorderableList.IndexOf(object item) => ((RepositoryItemViewModel)item).Index;

    [RelayCommand]
    private void ToggleHidden(RepositoryItemViewModel repository)
    {
        if (repository.Id is not { } id)
        {
            return;
        }

        var shown = _configStore.Current.HiddenRepositories.Toggle(id);

        _logger.LogInformation("{Action}仓库：{Repository}", shown ? "取消隐藏" : "隐藏", repository.Title);

        _configStore.Save();
        Reload();
    }

    [RelayCommand]
    private void Import() => _dialogs.ShowImportRepositories();

    [RelayCommand]
    private async Task AddUserPackage()
    {
        var selected = _dialogs.PickFolder(_localization["Packages.User.Add"]);

        if (selected is not null)
        {
            await UserPackages.AddAsync(selected);
        }
    }

    [RelayCommand]
    private void ExportRepositories()
    {
        var target = _dialogs.PickSaveFile(
            RepositoriesFile.Name,
            RepositoriesFile.Name,
            $"{RepositoriesFile.Name}|*.txt|*|*");

        if (target is not null)
        {
            File.WriteAllText(target, Export());
        }
    }

    [RelayCommand]
    private void BrowseImportFile()
    {
        var selected = _dialogs.PickFile("", $"{RepositoriesFile.Name}|*.txt", RepositoriesFile.Name);

        if (selected is not null)
        {
            ImportFilePath = selected;
        }
    }

    [ObservableProperty]
    private bool _importFromFile = true;

    [ObservableProperty]
    private bool _importFromLink;

    [ObservableProperty]
    private string _importFilePath = "";

    public void ReportMissingFile(string path) =>
        Status = _localization.Format("Repositories.Status.FileMissing", path);

    public string Export() => _service.Export();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isRefreshing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefreshStatusText))]
    private int _refreshCurrent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefreshStatusText))]
    private int _refreshTotal;

    public string RefreshStatusText => RefreshTotal == 0
        ? _localization["Repositories.Refreshing"]
        : _localization.Format("Repositories.RefreshingProgress", RefreshCurrent, RefreshTotal);

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => RefreshRepositoriesAsync(announce: true);

    public async Task RefreshOnStartupAsync(CancellationToken cancellationToken)
    {
        if (_service.List().Any(x => x.Url is not null))
        {
            await RefreshRepositoriesAsync(announce: false, cancellationToken);
        }
    }

    private bool CanRefresh() => !IsRefreshing;

    private async Task RefreshRepositoriesAsync(bool announce, CancellationToken cancellationToken = default)
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        RefreshCurrent = 0;
        RefreshTotal = 0;

        try
        {
            var progress = new Progress<RepositoryRefreshProgress>(value =>
            {
                RefreshCurrent = value.Current;
                RefreshTotal = value.Total;
            });

            var summary = await _service.RefreshAsync(cancellationToken, progress);

            foreach (var result in summary.Results)
            {
                var name = string.IsNullOrWhiteSpace(result.Name)
                    ? result.Url.AbsoluteUri
                    : result.Name;

                if (result.Outcome == RefreshOutcome.Failed)
                {
                    _logger.LogWarning(
                        "刷新仓库失败：{Repository}（{Url}）：{Detail}",
                        name,
                        result.Url,
                        result.Detail ?? "");
                }
                else
                {
                    _logger.LogInformation(
                        "刷新仓库：{Repository}（{Url}）{Outcome}",
                        name,
                        result.Url,
                        result.Outcome == RefreshOutcome.Updated ? "已更新" : "未变化");
                }
            }

            _logger.LogInformation(
                "刷新仓库完成：更新 {Updated} 个，未变化 {NotModified} 个，失败 {Failed} 个",
                summary.Updated,
                summary.NotModified,
                summary.Failed);

            Status = announce
                ? _localization.Format("Repositories.Status.Refreshed", summary.Updated, summary.NotModified, summary.Failed)
                : summary.Failed > 0
                    ? _localization.Format("Repositories.Status.RefreshFailed", summary.Failed)
                    : null;

            Reload();

            Refreshed?.Invoke(summary);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "刷新仓库失败");

            Status = _localization.Format("Page.OperationFailed", exception.Message);
        }
        finally
        {
            IsRefreshing = false;
            RefreshCurrent = 0;
            RefreshTotal = 0;
        }
    }

    public async Task ImportAsync(string content)
    {
        await WithBusyAsync(async () =>
        {
            var summary = await _service.ImportAsync(content, CancellationToken.None);

            if (summary.Failed > 0 || summary.UnparseableLines > 0)
            {
                _logger.LogWarning(
                    "导入仓库：{Failed} 个失败，{Unparseable} 行无法解析",
                    summary.Failed,
                    summary.UnparseableLines);
            }

            Status = _localization.Format("Repositories.Status.Imported", summary.Added, summary.AlreadyAdded, summary.Failed, summary.UnparseableLines);
        });

        Reload();
    }

    [ObservableProperty]
    private string _importUrl = "";

    [RelayCommand]
    private async Task DownloadAndImport()
    {
        if (!Uri.TryCreate(ImportUrl.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            Status = _localization["Repositories.Status.InvalidUrl"];
            return;
        }

        var outcome = await WithBusyOrThrowAsync(() => _service.ImportFromUrlAsync(url, CancellationToken.None));

        if (outcome.Summary is null)
        {
            _logger.LogWarning("下载仓库文件失败：{Url} {Detail}", url.AbsoluteUri, outcome.Detail);
        }

        Status = outcome.Summary is { } summary
            ? _localization.Format("Repositories.Status.Imported", summary.Added, summary.AlreadyAdded, summary.Failed, summary.UnparseableLines)
            : _localization.Format("Repositories.Status.ImportDownloadFailed", url.AbsoluteUri, outcome.Detail ?? "");

        Reload();
    }

    private async Task<AddRepositoryOutcome?> AddRepositoryAsync(
        string urlText,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        if (!Uri.TryCreate(urlText.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var outcome = await WithBusyOrThrowAsync(() => _service.AddAsync(url, null, headers ?? NoHeaders, CancellationToken.None));

        if (outcome.Status == AddRepositoryStatus.Success)
        {
            NewRepositoryUrl = "";
        }

        Reload();

        return outcome;
    }

    private string MessageFor(AddRepositoryOutcome? outcome, string url)
    {
        if (outcome is { Status: AddRepositoryStatus.Unreachable or AddRepositoryStatus.InvalidContent or AddRepositoryStatus.SaveFailed })
        {
            _logger.LogWarning("添加仓库失败：{Url} {Status} {Detail}", url, outcome.Status, outcome.Detail);
        }

        return outcome?.Status switch
        {
            AddRepositoryStatus.Success => _localization.Format("Repositories.Status.Added", url),
            AddRepositoryStatus.AlreadyAdded => _localization.Format("Repositories.Status.AlreadyAdded", url),
            AddRepositoryStatus.Unreachable => _localization.Format("Repositories.Status.Unreachable", url, outcome.Detail ?? ""),
            AddRepositoryStatus.InvalidContent => _localization.Format("Repositories.Status.InvalidContent", url),
            AddRepositoryStatus.SaveFailed => _localization["Repositories.Status.SaveFailed"],
            _ => _localization["Repositories.Status.InvalidUrl"],
        };
    }

    public void Release()
    {
        if (IsBusy || IsRefreshing)
        {
            return;
        }

        Repositories.Clear();
    }

    public void Reload()
    {
        var hidden = _configStore.Current.HiddenRepositories;
        var rows = new List<RepositoryItemViewModel>();

        foreach (var (entry, cached) in _service.ListWithCached())
        {
            var summary = cached is null
                ? _localization["Repositories.NotLoaded"]
                : _localization.Format("Repositories.PackageCount", cached.Repository.Packages.Count);

            var isHidden = entry.Id is not null && hidden.Contains(entry.Id);

            rows.Add(new RepositoryItemViewModel(
                entry.Index,
                entry.Id,
                entry.Url?.AbsoluteUri,
                entry.Name,
                summary,
                isHidden,
                _localization[isHidden ? "Repositories.Show" : "Repositories.Hide"]));
        }

        if (rows.Count != Repositories.Count
            || rows.Zip(Repositories).Any(pair => !pair.First.HasSameContent(pair.Second)))
        {
            Repositories.Clear();
            foreach (var row in rows)
            {
                Repositories.Add(row);
            }

            OnPropertyChanged(nameof(HasRepositories));
        }

        _ = UserPackages.ReloadAsync();
    }
}
