using System.Windows;
using VRCLearn.Core.Projects;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.Services;

public sealed class ProjectBackupRunner(
    ProjectService projects,
    ProjectBackupService backups,
    GuiConfigStore configs,
    ProgressService progress,
    LocalizationService localization)
{
    private readonly SemaphoreSlim _one = new(1, 1);

    public async Task<BackupRecord> RunAsync(
        string projectPath,
        string projectName,
        string note,
        bool auto,
        CancellationToken cancellationToken)
    {
        await _one.WaitAsync(cancellationToken);

        try
        {
            if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            {
                return await dispatcher.InvokeAsync(
                    () => RunCoreAsync(projectPath, projectName, note, auto, cancellationToken)).Task.Unwrap();
            }

            return await RunCoreAsync(projectPath, projectName, note, auto, cancellationToken);
        }
        finally
        {
            _one.Release();
        }
    }

    private Task<BackupRecord> RunCoreAsync(
        string projectPath,
        string projectName,
        string note,
        bool auto,
        CancellationToken cancellationToken) =>
        progress.RunAsync(
            localization["Progress.Backup"],
            async reporter =>
            {
                var project = await projects.OpenAsync(projectPath, cancellationToken);
                var config = configs.Current;

                return await backups.CreateAsync(
                    project,
                    projectName,
                    ProjectBackupService.ParseFormat(config.BackupFormat),
                    DateTime.Now,
                    reporter,
                    cancellationToken,
                    note: note,
                    auto: auto);
            });
}
