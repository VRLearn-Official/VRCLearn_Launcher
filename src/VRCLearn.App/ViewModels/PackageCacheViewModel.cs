using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Vpm;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.ViewModels;

/// <summary>包缓存的位置、大小与搬迁流程。</summary>
public sealed partial class PackageCacheViewModel(
    AppPaths paths,
    GuiConfigStore configs,
    RepositoryService repositories,
    DialogService dialogs,
    LocalizationService localization,
    ProgressService progress,
    ToastService toasts,
    ILogger<PackageCacheViewModel> logger) : PageViewModelBase(toasts)
{
    public string Current => PackageCachePath.Effective(configs.Current, paths);

    public bool HasCustom => !string.IsNullOrWhiteSpace(configs.Current.CachePath);

    [ObservableProperty]
    private string _size = "";

    public void RefreshSize() => Size = SizeText.Format(repositories.PackageCacheSize());

    public Task ChangeAsync(string destination) => MoveAsync(
        destination,
        "Settings.CachePath.ConfirmTitle",
        "Settings.CachePath.ConfirmMessage",
        "Settings.CachePath.ConfirmButton");

    public Task RestoreAsync() => MoveAsync(
        paths.RepositoriesDirectory,
        "Settings.CachePath.ResetTitle",
        "Settings.CachePath.ResetMessage",
        "Settings.CachePath.ResetButton");

    [RelayCommand]
    private void Clear()
    {
        if (!dialogs.ShowConfirm(
                localization["Settings.Cache.ClearTitle"],
                localization["Settings.Cache.ClearMessage"],
                localization["Settings.Cache.Clear"],
                danger: true))
        {
            return;
        }

        repositories.ClearPackageCache();

        RefreshSize();
    }

    protected override ILogger Logger => logger;

    protected override LocalizationService Localization => localization;

    private async Task MoveAsync(string destination, string titleKey, string messageKey, string buttonKey)
    {
        if (IsBusy || PathUtil.SamePath(Current, destination))
        {
            return;
        }

        if (!PackageCachePath.IsSafeDestination(paths, destination))
        {
            Status = localization["Settings.CachePath.Unsafe"];

            return;
        }

        if (!dialogs.ShowConfirm(
                localization[titleKey],
                localization.Format(messageKey, destination),
                localization[buttonKey]))
        {
            return;
        }

        IsBusy = true;
        Status = localization["Settings.CachePath.Migrating"];

        try
        {
            var result = await progress.RunAsync(
                localization["Progress.Cache"],
                reporter => Task.Run(() => repositories.MigratePackageCache(destination, reporter)));

            if (result.Status is PackageCacheMigrationStatus.UnsafeDestination)
            {
                Status = localization["Settings.CachePath.Unsafe"];

                return;
            }

            if (result.Status is PackageCacheMigrationStatus.OverlappingPaths)
            {
                Status = localization["Settings.CachePath.Overlapping"];

                return;
            }

            if (result.Status is PackageCacheMigrationStatus.Failed)
            {
                logger.LogWarning("搬迁包缓存失败：{Destination} {Error}", destination, result.Error);

                Status = localization.Format("Settings.CachePath.Failed", result.Error ?? "");

                return;
            }

            configs.Current.CachePath = PathUtil.SamePath(destination, paths.RepositoriesDirectory)
                ? ""
                : destination;

            configs.Save();

            OnPropertyChanged(nameof(Current));
            OnPropertyChanged(nameof(HasCustom));
            RefreshSize();

            Status = localization.Format("Settings.CachePath.Migrated", SizeText.Format(result.Bytes));
        }
        finally
        {
            IsBusy = false;
        }
    }
}
