using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VRCLearn.App.Plugins;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Repositories;
using VRCLearn.Core.Unity;
using VRCLearn.Infrastructure.Http;
using VRCLearn.Infrastructure;
using VRCLearn.Infrastructure.Logging;
using VRCLearn.Infrastructure.ProjectTemplates;
using VRCLearn.Infrastructure.Shell;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private SingleInstance? _singleInstance;

    [STAThread]
    private static void Main()
    {
        Environment.CurrentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var app = new App();
        app.InitializeComponent();

        app.DispatcherUnhandledException += app.OnUnhandledException;

        AppDomain.CurrentDomain.UnhandledException += app.OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += app.OnTaskUnhandledException;

        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = SingleInstance.Acquire(
            waitForPrevious: e.Args.Contains(SingleInstance.RestartArgument)
                ? TimeSpan.FromSeconds(3)
                : null);
        if (_singleInstance is null)
        {
            WindowActivation.AllowForeground();
            SingleInstance.Handoff(e.Args);
            Shutdown();
            return;
        }

        var paths = AppPaths.Resolve(e.Args);
        Directory.CreateDirectory(paths.Root);
        Directory.CreateDirectory(paths.PrivateDirectory);

        var proxy = new MutableProxy();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddFilter<FileLoggerProvider>(null, LogLevel.Trace));

        AppServices.AddLauncher(services, paths, proxy);

        _services = services.BuildServiceProvider();

        var startupLogger = _services.GetRequiredService<ILogger<App>>();

        MigrateLegacyGuiConfig(paths, startupLogger);

        var config = _services.GetRequiredService<GuiConfigStore>().Current;

        proxy.Configure(config.ProxyEnabled, config.ProxyUrl);
        _services.GetRequiredService<LocalizationService>().Apply(config.Language);
        _services.GetRequiredService<ThemeService>().Apply(config.Theme);
        _services.GetRequiredService<AnimationService>().Apply(config.AnimationEnabled);
        _services.GetRequiredService<LogLevelFilter>().Apply(config.LogsLevel);

        try
        {
            _services.GetRequiredService<PluginManager>().ApplyPendingActions();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            startupLogger.LogWarning(exception, "应用待处理的插件操作失败");
        }

        MainWindow = _services.GetRequiredService<MainWindow>();

        _services.GetRequiredService<WindowsProtocolRegistration>()
            .Apply(config.UseForVccProtocol, config.UseForAlcomTemplateFiles);

        MainWindow.Show();

        _services.GetRequiredService<AutoBackupService>().Start();

        _ = _services.GetRequiredService<IUnityEditorLocator>().RefreshFromHubAsync(CancellationToken.None);

        _services.GetRequiredService<ILogger<App>>()
            .LogInformation(
                "VRCLearn Launcher {Version} started. OS: {OS}, language: {Language}, log level: {Level}, shared directory: {Root}, private directory: {Private}",
                typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                Environment.OSVersion.VersionString,
                config.Language,
                config.LogsLevel,
                paths.Root,
                paths.PrivateDirectory);

        _singleInstance.StartListening(HonourArguments);

        _ = _services.GetRequiredService<UpdateViewModel>().InitializeAsync();

        if (!ShowDeepLink(e.Args))
        {
            _services.GetRequiredService<MainWindowViewModel>().Start();
        }

        InheritAlcomTemplates(_services, paths, startupLogger);
    }

    private void HonourArguments(IReadOnlyList<string> arguments) => Dispatcher.Invoke(() =>
    {
        ActivateMainWindow();
        ShowDeepLink(arguments);
    });

    /// <summary>处理深链或模板文件，返回是否有事要办。</summary>
    private bool ShowDeepLink(IReadOnlyList<string> arguments)
    {
        if (_services is not { } services)
        {
            return false;
        }

        if (ImportTemplateFiles(services, arguments))
        {
            return true;
        }

        foreach (var argument in arguments)
        {
            if (RepositoriesFileParser.ParseDeepLink(argument) is not { } repository)
            {
                continue;
            }

            ActivateMainWindow();
            _ = services.GetRequiredService<RepositoriesViewModel>().AddFromLinkAsync(repository);

            return true;
        }

        return false;
    }

    private bool ImportTemplateFiles(IServiceProvider services, IReadOnlyList<string> arguments)
    {
        var files = arguments
            .Where(x => string.Equals(
                Path.GetExtension(x),
                WindowsProtocolRegistration.ExtensionName,
                StringComparison.OrdinalIgnoreCase))
            .Where(File.Exists)
            .ToList();

        if (files.Count == 0)
        {
            return false;
        }

        ActivateMainWindow();
        services.GetRequiredService<NavigationService>().GoTo("templates");
        _ = services.GetRequiredService<TemplatesViewModel>().ImportAsync(files);

        return true;
    }

    private static string TemplatesDirectory(AppPaths paths) =>
        Path.Combine(paths.PrivateDirectory, "templates");

    /// <summary>问过之后才把 .alcomtemplate 复制成自家格式，原文件保留。</summary>
    private static void InheritAlcomTemplates(IServiceProvider services, AppPaths paths, ILogger logger)
    {
        var configs = services.GetRequiredService<GuiConfigStore>();
        var config = configs.Current;

        if (config.AlcomTemplateChoice.Length > 0)
        {
            return;
        }

        var directory = TemplatesDirectory(paths);
        var found = TemplateCompat.Find(directory);

        if (found.Count == 0)
        {
            return;
        }

        var localization = services.GetRequiredService<LocalizationService>();
        var choice = services.GetRequiredService<DialogService>().ShowChoice(
            localization["AlcomTemplates.Title"],
            localization.Format("AlcomTemplates.Message", found.Count),
            localization["AlcomTemplates.Inherit"],
            localization["AlcomTemplates.Fresh"]);

        if (choice != ConfirmChoice.Confirm)
        {
            config.AlcomTemplateChoice = "fresh";
            configs.Save();

            logger.LogInformation("Kept a fresh template library; {Count} .alcomtemplate file(s) left alone", found.Count);

            return;
        }

        var copied = TemplateCompat.CopyAndUpgrade(directory);

        config.AlcomTemplateChoice = "inherit";
        configs.Save();

        if (copied.Count == 0)
        {
            return;
        }

        logger.LogInformation("Copied {Count} .alcomtemplate template(s) to .vrclearn-template", copied.Count);

        services.GetRequiredService<SharedSettingsViewModel>().UseForAlcomTemplateFiles = true;
    }

    private static void MigrateLegacyGuiConfig(AppPaths paths, ILogger logger)
    {
        var legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VRCLearnLauncher",
            "gui-config.json");

        try
        {
            if (File.Exists(legacy) && !File.Exists(paths.GuiConfigFile))
            {
                File.Copy(legacy, paths.GuiConfigFile);
                logger.LogInformation("Moved the old gui-config.json into {Path}", paths.GuiConfigFile);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not migrate {Legacy}", legacy);
        }
    }

    private void OnUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args)
    {
        args.Handled = true;

        _services?.GetRequiredService<ILogger<App>>().LogError(args.Exception, "未处理的界面异常");

        var localization = _services?.GetRequiredService<LocalizationService>();

        MessageBox.Show(
            localization?["Dialog.UnhandledMessage"] ?? "",
            localization?["Dialog.UnhandledTitle"] ?? "",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs args) =>
        _services?.GetService<ILogger<App>>()?.LogCritical(args.ExceptionObject as Exception, "未处理的进程异常");

    private void OnTaskUnhandledException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        _services?.GetService<ILogger<App>>()?.LogCritical(args.Exception, "未观察到的任务异常");

        args.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.GetService<ILogger<App>>()?.LogInformation("VRCLearn Launcher exiting");

        _services?.GetService<PluginManager>()?.Shutdown();
        _singleInstance?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }

    private void ActivateMainWindow() => Dispatcher.Invoke(() =>
    {
        if (MainWindow is { } window)
        {
            WindowActivation.BringToFront(window);
        }
    });
}
