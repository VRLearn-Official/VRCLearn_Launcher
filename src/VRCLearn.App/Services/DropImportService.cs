using System.IO;
using Microsoft.Extensions.Logging;
using VRCLearn.App.ViewModels;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Templates;
using VRCLearn.Infrastructure.Plugins;

namespace VRCLearn.App.Services;

public sealed record DropPlan(
    IReadOnlyList<string> Projects,
    IReadOnlyList<string> Backups,
    IReadOnlyList<string> Plugins,
    IReadOnlyList<string> Templates)
{
    public bool HasAny =>
        Projects.Count > 0 || Backups.Count > 0 || Plugins.Count > 0 || Templates.Count > 0;
}

public sealed class DropImportService(
    ProjectsViewModel projects,
    BackupsViewModel backups,
    TemplatesViewModel templates,
    PluginsViewModel plugins,
    NavigationService navigation,
    LocalizationService localization,
    ToastService toasts,
    ILogger<DropImportService> logger)
{
    private bool _busy;

    public bool IsBusy => _busy;

    public static DropPlan Detect(IReadOnlyList<string> paths)
    {
        var projectPaths = new List<string>();
        var backupPaths = new List<string>();
        var pluginPaths = new List<string>();
        var templatePaths = new List<string>();

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.Exists(path))
                {
                    if (ProjectService.IsProject(path))
                    {
                        projectPaths.Add(path);
                    }

                    continue;
                }

                if (!File.Exists(path))
                {
                    continue;
                }

                switch (Path.GetExtension(path).ToLowerInvariant())
                {
                    case ProjectBackup.Extension:
                        backupPaths.Add(path);
                        break;
                    case PluginPackageInstaller.Extension:
                        pluginPaths.Add(path);
                        break;
                    case TemplateFormat.Extension:
                        templatePaths.Add(path);
                        break;
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
            {
            }
        }

        return new DropPlan(projectPaths, backupPaths, pluginPaths, templatePaths);
    }

    public static bool CanAccept(IReadOnlyList<string> paths) => Detect(paths).HasAny;

    public async Task ImportAsync(IReadOnlyList<string> paths)
    {
        if (_busy)
        {
            return;
        }

        var plan = Detect(paths);

        if (!plan.HasAny)
        {
            toasts.Show(localization["Drop.Invalid"]);

            return;
        }

        _busy = true;

        try
        {
            if (plan.Projects.Count > 0)
            {
                await TryAsync("项目", async () =>
                {
                    navigation.GoTo("projects");

                    foreach (var path in plan.Projects)
                    {
                        await projects.AddAsync(path);
                    }
                });
            }

            if (plan.Backups.Count > 0)
            {
                await TryAsync("备份", async () =>
                {
                    navigation.GoTo("backups");
                    await backups.ImportFilesAsync(plan.Backups);
                });
            }

            if (plan.Plugins.Count > 0)
            {
                await TryAsync("插件", async () =>
                {
                    navigation.GoTo("plugins");
                    await plugins.InstallAsync(plan.Plugins);
                });
            }

            if (plan.Templates.Count > 0)
            {
                await TryAsync("模板", async () =>
                {
                    navigation.GoTo("templates");
                    await templates.ImportAsync(plan.Templates);
                });
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task TryAsync(string group, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "拖放导入失败：{Group}", group);
        }
    }
}
