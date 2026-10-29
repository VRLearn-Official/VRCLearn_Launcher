using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Packages;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.ViewModels;

public sealed partial class UserPackagesViewModel : PageViewModelBase
{
    private readonly UserPackageService _userPackages;
    private readonly LocalizationService _localization;
    private readonly GuiConfigStore _configStore;
    private readonly ILogger<UserPackagesViewModel> _logger;

    public UserPackagesViewModel(
        UserPackageService userPackages,
        LocalizationService localization,
        GuiConfigStore configStore,
        ToastService toasts,
        ILogger<UserPackagesViewModel> logger)
        : base(toasts)
    {
        _userPackages = userPackages;
        _localization = localization;
        _configStore = configStore;
        _logger = logger;
        _hideUserPackages = configStore.Current.HideLocalUserPackages;

        _localization.PropertyChanged += (_, _) => _ = ReloadAsync();
    }

    protected override ILogger Logger => _logger;

    protected override LocalizationService Localization => _localization;

    public ObservableCollection<UserPackageFolderViewModel> Folders { get; } = [];

    [ObservableProperty]
    private bool _hideUserPackages;

    partial void OnHideUserPackagesChanged(bool value)
    {
        _configStore.Current.HideLocalUserPackages = value;
        _configStore.Save();

        _logger.LogInformation("隐藏本地包：{Value}", value);
    }

    public bool HasNoFolders => Folders.Count == 0;

    public async Task ReloadAsync()
    {
        var folders = await _userPackages.LoadAsync(CancellationToken.None);

        Folders.Clear();

        foreach (var folder in folders)
        {
            Folders.Add(new UserPackageFolderViewModel(folder, _localization));
        }

        OnPropertyChanged(nameof(HasNoFolders));
    }

    public async Task AddAsync(string folder)
    {
        var status = await _userPackages.AddAsync(folder, CancellationToken.None);

        if (status != AddUserPackageStatus.Success)
        {
            _logger.LogWarning("添加本地包失败：{Folder} {Status}", folder, status);
        }

        Status = status == AddUserPackageStatus.Success
            ? _localization.Format("Packages.User.Added", folder)
            : _localization[$"Packages.User.{status}"];

        if (status == AddUserPackageStatus.Success)
        {
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private async Task Remove(UserPackageFolderViewModel folder)
    {
        _userPackages.Remove(folder.Path);
        Status = _localization.Format("Packages.User.Removed", folder.DisplayName);

        await ReloadAsync();
    }
}

public sealed class UserPackageFolderViewModel(UserPackageFolder folder, LocalizationService localization)
{
    public string Path { get; } = folder.Path;

    public bool IsUnreadable { get; } = !folder.IsReadable;

    public string DisplayName { get; } = folder.IsReadable
        ? $"{folder.Manifest!.DisplayName ?? folder.Manifest.Name} {folder.Manifest.Version}"
        : localization["Packages.User.Unreadable"];
}
