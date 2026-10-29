using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Plugins;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Http;
using VRCLearn.Core.IO;
using VRCLearn.Core.Packages;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Resources;
using VRCLearn.Core.Templates;
using VRCLearn.Core.Unity;
using VRCLearn.Core.Vpm;
using VRCLearn.Infrastructure.Http;
using VRCLearn.Infrastructure.IO;
using VRCLearn.Infrastructure.Logging;
using VRCLearn.Infrastructure.Plugins;
using VRCLearn.Infrastructure.ProjectTemplates;
using VRCLearn.Infrastructure.Shell;
using VRCLearn.Infrastructure.Storage;
using VRCLearn.Infrastructure.Unity;
using VRCLearn.Infrastructure.Updates;
using VRCLearn.Infrastructure.Windows;

namespace VRCLearn.App;

/// <summary>组装启动器使用的服务容器。</summary>
public static class AppServices
{
    public static void AddLauncher(IServiceCollection services, AppPaths paths, MutableProxy proxy)
    {
        services
            .AddSingleton(paths)
            .AddSingleton<LogLevelFilter>()
            .AddSingleton<FileLoggerProvider>()
            .AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<FileLoggerProvider>())
            .AddSingleton(proxy)
            .AddSingleton(HttpVpmClient.CreateHttpClient(proxy))
            .AddSingleton<IVpmHttpClient, HttpVpmClient>()
            .AddSingleton<ILauncherSettingsStore, LauncherSettingsStore>()
            .AddSingleton<IRepositoryCacheStore, RepositoryCacheStore>()
            .AddSingleton<IFileIoFactory, PhysicalFileIoFactory>()
            .AddSingleton<RepositoryService>()
            .AddSingleton<ProjectService>()
            .AddSingleton<GuiConfigStore>()
            .AddSingleton(new WindowsProtocolRegistration(Environment.ProcessPath!))
            .AddSingleton<UpdateStateStore>()
            .AddSingleton<UpdateService>()
            .AddSingleton<UpdateViewModel>()
            .AddSingleton<LocalizationService>()
            .AddSingleton<ThemeService>()
            .AddSingleton<AnimationService>()
            .AddSingleton<ExperimentalFeaturesService>()
            .AddSingleton<NavigationService>()
            .AddSingleton<DialogService>()
            .AddSingleton<ProgressService>()
            .AddSingleton<ToastService>()
            .AddSingleton<IUnityLauncher, WindowsUnityLauncher>()
            .AddSingleton<IUnityHubLauncher, WindowsUnityHubLauncher>()
            .AddSingleton<IUnityEditorLocator, WindowsUnityEditorLocator>()
            .AddSingleton<UnityEditorRegistration>()
            .AddSingleton<IUnityWindowActivator, WindowsUnityWindowActivator>()
            .AddSingleton<SystemCheckService>()
            .AddSingleton<SystemCheckHostService>()
            .AddSingleton<SettingsHostService>()
            .AddSingleton<HostDownloadService>()
            .AddSingleton<PluginDialogHostService>()
            .AddSingleton<ProjectPickerHostService>()
            .AddSingleton<RepositoryHostService>()
            .AddSingleton<PluginStateStore>()
            .AddSingleton<PluginDirectoryScanner>()
            .AddSingleton<PluginPackageInstaller>()
            .AddSingleton<PluginWindowService>()
            .AddSingleton<PluginManager>()
            .AddSingleton<ProjectBackupRunner>()
            .AddSingleton<AutoBackupService>()
            .AddSingleton<UnityProjectOpener>()
            .AddSingleton<IUserTemplateStore, FileSystemUserTemplateStore>()
            .AddSingleton<IBackupStore, PhysicalBackupStore>()
            .AddSingleton<IResourceStore, ResourceStore>()
            .AddSingleton<ConfigBackupService>()
            .AddSingleton<ProjectBackupService>()
            .AddSingleton<ProjectRestoreService>()
            .AddSingleton<ProjectCopyService>()
            .AddSingleton<IPackageInstaller>(provider =>
            {
                var configs = provider.GetRequiredService<GuiConfigStore>();

                return new PackageInstaller(
                    new PhysicalFileIo(() => PackageCachePath.Effective(configs.Current, paths)),
                    provider.GetRequiredService<IVpmHttpClient>());
            })
            .AddSingleton<PackageCacheViewModel>()
            .AddSingleton<BackupDirectoryViewModel>()
            .AddSingleton<SharedSettingsViewModel>()
            .AddSingleton<SettingsViewModel>()
            .AddSingleton<RepositoriesViewModel>()
            .AddSingleton<ProjectsViewModel>()
            .AddSingleton<ProjectDetailViewModel>()
            .AddSingleton<TemplatesViewModel>()
            .AddSingleton<LogsViewModel>()
            .AddSingleton<BackupsViewModel>()
            .AddSingleton<ResourcesViewModel>()
            .AddSingleton<PluginsViewModel>()
            .AddSingleton<UserPackageService>()
            .AddSingleton<UserPackagesViewModel>()
            .AddSingleton<SetupViewModel>()
            .AddSingleton<DropImportService>()
            .AddTransient<NewProjectViewModel>()
            .AddSingleton<MainWindowViewModel>()
            .AddSingleton<MainWindow>();
    }
}
