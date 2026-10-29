using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using VRCLearn.App.ViewModels;
using VRCLearn.App.Views;
using VRCLearn.Core.Resources;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.Services;

/// <summary>带额外按钮的确认框选了什么。</summary>
public enum ConfirmChoice
{
    Cancel,

    Extra,

    Confirm,
}

public sealed class DialogService
{
    private readonly IServiceProvider _services;
    private readonly AnimationService _animations;
    private readonly ILogger<DialogService> _logger;
    private int _scrimDepth;
    private Window? _scrimWindow;

    public DialogService(IServiceProvider services, AnimationService animations, ILogger<DialogService> logger)
    {
        _services = services;
        _animations = animations;
        _logger = logger;

        Instance = this;
    }

    public static DialogService Instance { get; private set; } = null!;

    /// <param name="preset">非空表示从模板行点进来、已预选好。</param>
    /// <returns>是否真的创建了。</returns>
    public bool ShowNewProject(TemplateOption? preset)
    {
        var viewModel = _services.GetRequiredService<NewProjectViewModel>();

        viewModel.Preset = preset;

        _logger.LogInformation("打开新建项目对话框");

        return Show(new NewProjectDialog { DataContext = viewModel });
    }

    public void ShowImportRepositories()
    {
        _logger.LogInformation("打开导入仓库对话框");

        Show(new ImportRepositoriesDialog { DataContext = _services.GetRequiredService<RepositoriesViewModel>() });
    }

    /// <param name="danger">确认按钮会不可逆地丢数据时置真，按钮显示为红色。</param>
    public bool ShowConfirm(
        string title,
        string message,
        string confirmText,
        bool danger = false,
        Window? owner = null,
        string? detailTitle = null,
        string? detailBody = null)
    {
        Record(title, message);

        var dialog = new ConfirmDialog(
            title,
            message,
            confirmText,
            confirmDanger: danger,
            detailTitle: detailTitle,
            detailBody: detailBody);

        Prepare(dialog, owner);

        return dialog.ShowDialog() == true;
    }

    public async Task RunBusyAsync(string title, string message, Func<Task> work)
    {
        var dialog = new BusyDialog(title, message);

        Prepare(dialog);

        ExceptionDispatchInfo? failure = null;

        dialog.Loaded += async (_, _) =>
        {
            try
            {
                await work();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                if (dialog.IsVisible)
                {
                    dialog.Close();
                }
            }
        };

        dialog.ShowDialog();

        failure?.Throw();
    }

    public void ShowAutoBackup()
    {
        _logger.LogInformation("打开自动备份设置");

        Show(new AutoBackupDialog(
            _services.GetRequiredService<GuiConfigStore>(),
            _services.GetRequiredService<AutoBackupService>(),
            this,
            _services.GetRequiredService<LocalizationService>()));
    }

    public ConfirmChoice ShowChoice(
        string title,
        string message,
        string confirmText,
        string? extraText = null,
        bool extraDanger = false,
        bool confirmDanger = false,
        Window? owner = null)
    {
        Record(title, message);

        var dialog = new ConfirmDialog(
            title,
            message,
            confirmText,
            extraText,
            confirmDanger: confirmDanger,
            extraDanger: extraDanger);

        Prepare(dialog, owner);
        dialog.ShowDialog();

        if (dialog.ExtraClicked)
        {
            return ConfirmChoice.Extra;
        }

        return dialog.DialogResult == true ? ConfirmChoice.Confirm : ConfirmChoice.Cancel;
    }

    public ResourceEditOutcome? ShowResourceEditor(ResourceDefinition? existing)
    {
        var store = _services.GetRequiredService<IResourceStore>();

        var viewModel = new ResourceEditViewModel(
            existing,
            store.Current.Tags,
            _services.GetRequiredService<LocalizationService>());

        if (existing is not null)
        {
            var cover = store.CoverPath(existing);

            try
            {
                if (cover.Length > 0 && File.Exists(cover))
                {
                    viewModel.ShowCover(File.ReadAllBytes(cover));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "读取资源封面失败：{Path}", cover);
            }
        }

        var dialog = new ResourceEditDialog { DataContext = viewModel };

        Prepare(dialog);

        return dialog.ShowDialog() == true ? dialog.Outcome : null;
    }

    public IReadOnlyList<string>? ShowTagFilter(
        IReadOnlyList<string> tags,
        IReadOnlyCollection<string> selected)
    {
        var dialog = new TagFilterDialog(tags, selected);

        Prepare(dialog);

        return dialog.ShowDialog() == true ? dialog.SelectedTags : null;
    }

    public void ShowTagManager()
    {
        var dialog = new TagManagerDialog
        {
            DataContext = new TagManagerViewModel(
                _services.GetRequiredService<IResourceStore>(),
                this,
                _services.GetRequiredService<LocalizationService>()),
        };

        Show(dialog);
    }

    public string? ShowProjectPicker(
        IReadOnlyList<ProjectChoice> projects,
        int resourceCount)
    {
        var localization = _services.GetRequiredService<LocalizationService>();

        return ShowProjectPicker(
            projects,
            new ProjectPickerChrome(
                localization["Resources.Picker.Title"],
                localization["Resources.Picker.Hint"],
                localization.Format("Resources.Picker.Count", resourceCount),
                localization["Resources.Picker.Warning"],
                localization["Resources.Picker.Import"],
                localization["Resources.Picker.Empty"]));
    }

    public string? ShowProjectPicker(
        IReadOnlyList<ProjectChoice> projects,
        ProjectPickerChrome chrome,
        Window? owner = null)
    {
        var dialog = new ProjectPickerDialog(projects, chrome);

        Prepare(dialog, owner);

        return dialog.ShowDialog() == true ? dialog.SelectedPath : null;
    }

    public void ShowNotice(string title, string message, string closeText)
    {
        Record(title, message);

        Show(new ConfirmDialog(title, message, closeText));
    }

    public void ShowLicenses(string title, string text, string closeText)
    {
        _logger.LogInformation("打开{Title}", title);

        Show(new LicensesDialog(title, text, closeText));
    }

    public bool ShowPluginTrust(
        string title,
        string warning,
        PluginTrustInfo info,
        string installText,
        string cancelText)
    {
        _logger.LogInformation("插件安装确认：{Title}", title);

        return Show(new PluginTrustDialog(title, warning, info, installText, cancelText));
    }

    /// <summary>填一条备注；取消返回 null，确认时内容可以为空。</summary>
    public string? ShowNote(string title, string current, string confirmText, string hint)
    {
        var dialog = new NoteDialog(title, current, confirmText, hint);

        Prepare(dialog);

        return dialog.ShowDialog() == true ? dialog.Note : null;
    }

    /// <summary>改个名字；取消返回 null。</summary>
    public string? ShowRename(string title, string currentName, string confirmText)
    {
        Record(title, currentName);

        var dialog = new RenameDialog(title, currentName, confirmText);

        Prepare(dialog);

        return dialog.ShowDialog() == true ? dialog.NewName : null;
    }

    private void Record(string title, string message) =>
        _logger.LogInformation("{Title}：{Message}", title, message.ReplaceLineEndings(" | "));

    /// <summary>选一个已存在的目录；取消返回 null。</summary>
    public string? PickFolder(string title, string initialDirectory = "", Window? owner = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            InitialDirectory = initialDirectory,
        };

        return Run(dialog, owner) == true ? dialog.FolderName : null;
    }

    /// <summary>选一个已存在的文件；取消返回 null。</summary>
    public string? PickFile(
        string title,
        string filter,
        string fileName = "",
        Window? owner = null,
        string initialDirectory = "")
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = fileName,
            InitialDirectory = initialDirectory,
        };

        return Run(dialog, owner) == true ? dialog.FileName : null;
    }

    /// <summary>选多个已存在的文件；取消返回空数组。</summary>
    public string[] PickFiles(string title, string filter, Window? owner = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            Multiselect = true,
        };

        return Run(dialog, owner) == true ? dialog.FileNames : [];
    }

    /// <summary>选一个保存位置；取消返回 null。</summary>
    public string? PickSaveFile(string title, string fileName, string filter, Window? owner = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = fileName,
            Filter = filter,
        };

        return Run(dialog, owner) == true ? dialog.FileName : null;
    }

    private static bool? Run(CommonDialog dialog, Window? owner) =>
        dialog.ShowDialog(owner ?? Application.Current.MainWindow);

    private bool Show(Window dialog)
    {
        Prepare(dialog);

        return dialog.ShowDialog() == true;
    }

    private void Prepare(Window dialog, Window? owner = null)
    {
        var target = owner ?? Application.Current.MainWindow;

        dialog.Owner = target;
        _scrimDepth++;
        _scrimWindow ??= target;

        dialog.Closed += (_, _) =>
        {
            _scrimDepth = Math.Max(0, _scrimDepth - 1);
            Scrim(_scrimDepth > 0);
        };

        Scrim(true);

        if (_animations.Enabled)
        {
            dialog.Opacity = 1;
            dialog.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.Zero));

            dialog.Loaded += (_, _) => dialog.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, AnimationService.Quick) { EasingFunction = AnimationService.Ease });
        }
    }

    private void Scrim(bool visible)
    {
        var scrim = _scrimWindow?.FindName("DialogScrim") as FrameworkElement;

        if (!visible)
        {
            _scrimWindow = null;
        }

        if (scrim is null)
        {
            return;
        }

        scrim.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (visible && _animations.Enabled)
        {
            scrim.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, AnimationService.Quick) { EasingFunction = AnimationService.Ease });
        }
    }
}
