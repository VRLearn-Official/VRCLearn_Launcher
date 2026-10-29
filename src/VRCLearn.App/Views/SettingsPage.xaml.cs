using System.ComponentModel;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class SettingsPage : UserControl
{
    private Color _idleBackground;
    private Color _idleForeground;
    private bool _idleCaptured;

    public SettingsPage()
    {
        InitializeComponent();

        Loaded += (_, _) => CaptureIdleColors();

        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is SettingsViewModel old)
            {
                old.Update.PropertyChanged -= OnUpdateChanged;
            }

            if (args.NewValue is SettingsViewModel fresh)
            {
                fresh.Update.PropertyChanged += OnUpdateChanged;
            }
        };

        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is not true || DataContext is not SettingsViewModel viewModel)
            {
                return;
            }

            viewModel.Cache.RefreshSize();

            if (viewModel.ConsumeProxyFocus())
            {
                Dispatcher.BeginInvoke(ScrollToNetwork, System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        };
    }

    private void ScrollToNetwork()
    {
        if (!NetworkSection.IsDescendantOf(Scroller) || !NetworkCard.IsDescendantOf(Scroller))
        {
            return;
        }

        var top = NetworkSection.TransformToAncestor(Scroller).Transform(new Point(0, 0)).Y + Scroller.VerticalOffset;
        var bottom = NetworkCard.TransformToAncestor(Scroller).Transform(new Point(0, 0)).Y
            + Scroller.VerticalOffset
            + NetworkCard.ActualHeight;

        var offset = top - ((Scroller.ViewportHeight - (bottom - top)) / 2);

        Scroller.ScrollToVerticalOffset(Math.Max(0, offset));
    }

    private void OnUpdateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdateViewModel.Phase))
        {
            AnimateUpdateButton();
        }
    }

    private void CaptureIdleColors()
    {
        if (_idleCaptured
            || UpdateButton.Background is not SolidColorBrush background
            || UpdateButton.Foreground is not SolidColorBrush foreground)
        {
            return;
        }

        _idleBackground = background.Color;
        _idleForeground = foreground.Color;
        _idleCaptured = true;
    }

    private void AnimateUpdateButton()
    {
        if (!_idleCaptured || DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var accent = Application.Current.TryFindResource("AccentFillColorDefaultBrush") is SolidColorBrush accentBrush
            ? accentBrush.Color
            : _idleBackground;

        var onAccent = Application.Current.TryFindResource("TextOnAccentFillColorPrimaryBrush") is SolidColorBrush onAccentBrush
            ? onAccentBrush.Color
            : _idleForeground;

        var lit = viewModel.Update.Phase is not UpdatePhase.Idle;

        UpdateButton.Appearance = lit
            ? Wpf.Ui.Controls.ControlAppearance.Primary
            : Wpf.Ui.Controls.ControlAppearance.Secondary;

        Transition(UpdateButton, Control.BackgroundProperty, lit ? accent : _idleBackground, lit);
        Transition(UpdateButton, Control.ForegroundProperty, lit ? onAccent : _idleForeground, lit);
    }

    private static void Transition(Control control, DependencyProperty property, Color target, bool lit)
    {
        var brush = EnsureLocalBrush(control, property);

        var animation = new ColorAnimation(target, AnimationService.Quick) { EasingFunction = AnimationService.Ease };

        if (!lit)
        {
            animation.Completed += (_, _) =>
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                control.ClearValue(property);
            };
        }

        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private static SolidColorBrush EnsureLocalBrush(Control control, DependencyProperty property)
    {
        if (control.ReadLocalValue(property) is SolidColorBrush local)
        {
            return local;
        }

        var clone = (control.GetValue(property) as SolidColorBrush)?.Clone() ?? new SolidColorBrush();
        control.SetValue(property, clone);

        return clone;
    }

    private void OnBrowseUnityHubClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var selected = DialogService.Instance.PickFile(
            LocalizationService.Instance["Settings.UnityHub"],
            "Unity Hub.exe|Unity Hub.exe|*.exe|*.*");

        if (selected is not null)
        {
            viewModel.Shared.UnityHubPath = selected;
        }
    }

    private void OnBrowseProjectDirectoryClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var selected = DialogService.Instance.PickFolder(
            LocalizationService.Instance["Settings.ProjectPath"],
            viewModel.Shared.ActualProjectDirectory);

        if (selected is not null)
        {
            viewModel.Shared.ProjectDirectory = selected;
        }
    }

    private async void OnBrowseBackupDirectoryClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var selected = DialogService.Instance.PickFolder(
            LocalizationService.Instance["Settings.BackupDirectory"],
            viewModel.Backups.Current);

        if (selected is not null)
        {
            await viewModel.Backups.ChangeAsync(selected);
        }
    }

    private async void OnRestoreBackupDirectoryClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            await viewModel.Backups.RestoreAsync();
        }
    }

    private async void OnBrowseCacheDirectoryClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var selected = DialogService.Instance.PickFolder(
            LocalizationService.Instance["Settings.CachePath"],
            viewModel.Cache.Current);

        if (selected is not null)
        {
            await viewModel.Cache.ChangeAsync(selected);
        }
    }

    private async void OnRestoreCacheDirectoryClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            await viewModel.Cache.RestoreAsync();
        }
    }

    private void OnAddUnityEditorClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var selected = DialogService.Instance.PickFile(
            LocalizationService.Instance["Settings.UnityEditors"],
            "Unity.exe|Unity.exe|*.exe|*.*");

        if (selected is not null)
        {
            viewModel.AddUnityEditor(selected);
        }
    }
}
