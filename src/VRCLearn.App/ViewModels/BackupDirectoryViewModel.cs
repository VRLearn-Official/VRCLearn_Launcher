using Microsoft.Extensions.Logging;
using VRCLearn.App.Services;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Vpm;

namespace VRCLearn.App.ViewModels;

public sealed class BackupDirectoryViewModel(
    AppPaths paths,
    ILauncherSettingsStore settings,
    IBackupStore backups,
    DialogService dialogs,
    LocalizationService localization,
    ProgressService progress,
    ToastService toasts,
    ILogger<BackupDirectoryViewModel> logger) : PageViewModelBase(toasts)
{
    public string Current =>
        string.IsNullOrWhiteSpace(settings.Current.ProjectBackupPath)
            ? paths.BackupDirectory
            : settings.Current.ProjectBackupPath;

    public bool HasCustom => !string.IsNullOrWhiteSpace(settings.Current.ProjectBackupPath);

    public void Refresh()
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(HasCustom));
    }

    protected override ILogger Logger => logger;

    protected override LocalizationService Localization => localization;

    public Task ChangeAsync(string destination) => MoveAsync(
        destination,
        "Settings.BackupPath.ConfirmTitle",
        "Settings.BackupPath.ConfirmMessage",
        "Settings.BackupPath.ConfirmButton");

    public Task RestoreAsync() => MoveAsync(
        paths.BackupDirectory,
        "Settings.BackupPath.ResetTitle",
        "Settings.BackupPath.ResetMessage",
        "Settings.BackupPath.ResetButton");

    private async Task MoveAsync(string destination, string titleKey, string messageKey, string buttonKey)
    {
        if (IsBusy || PathUtil.SamePath(Current, destination))
        {
            return;
        }

        if (PathUtil.IsUnder(Current, destination) || PathUtil.IsUnder(destination, Current))
        {
            Status = localization["Settings.BackupPath.Overlapping"];

            return;
        }

        var count = backups.CountMigratable();

        if (!dialogs.ShowConfirm(
                localization[count > 0 ? titleKey : "Settings.BackupPath.EmptyTitle"],
                count > 0
                    ? localization.Format(messageKey, count, destination)
                    : localization.Format("Settings.BackupPath.EmptyMessage", destination),
                localization[count > 0 ? buttonKey : "Settings.BackupPath.EmptyButton"]))
        {
            return;
        }

        if (count == 0)
        {
            Persist(destination);
            Status = localization["Settings.BackupPath.Switched"];

            return;
        }

        IsBusy = true;
        Status = localization["Settings.BackupPath.Migrating"];

        try
        {
            var result = await progress.RunAsync(
                localization["Progress.BackupMigration"],
                reporter => Task.Run(() => backups.Migrate(destination, reporter)));

            if (result.Status is BackupMigrationStatus.OverlappingPaths)
            {
                Status = localization["Settings.BackupPath.Overlapping"];

                return;
            }

            if (result.Status is BackupMigrationStatus.Failed)
            {
                logger.LogWarning("迁移备份失败：{Destination} {Error}", destination, result.Error);

                Status = localization.Format("Settings.BackupPath.Failed", result.Error ?? "");

                return;
            }

            Persist(destination);

            Status = result.Files > 0
                ? localization.Format("Settings.BackupPath.Migrated", result.Files, SizeText.Format(result.Bytes))
                : localization["Settings.BackupPath.Switched"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Persist(string destination)
    {
        settings.Current.ProjectBackupPath = PathUtil.SamePath(destination, paths.BackupDirectory)
            ? ""
            : destination;
        settings.Save();
        Refresh();
    }
}
