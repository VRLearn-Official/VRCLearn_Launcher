using System.Windows;
using System.Windows.Media;
using VRCLearn.Infrastructure.Storage;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace VRCLearn.App.Services;

public sealed class ThemeService
{
    private const WindowBackdropType Backdrop = WindowBackdropType.Mica;

    /// <summary>点亮态使用强调色的控件键。</summary>
    private static readonly string[] AccentKeys =
    [
        "ToggleSwitchFillOn",
        "ToggleSwitchStrokeOn",
        "ComboBoxItemPillFillBrush",
        "ListViewItemPillFillBrush",
        "CheckBoxCheckBackgroundFillChecked",
        "RadioButtonOuterEllipseCheckedStroke",
        "SliderThumbBackground",
        "ProgressBarForeground",
        "ProgressRingForegroundThemeBrush",
        "ToggleButtonBackgroundChecked",
        "NavigationViewSelectionIndicatorForeground",
    ];

    private static readonly string[] AccentPointerOverKeys =
    [
        "ToggleSwitchFillOnPointerOver",
        "ToggleSwitchStrokeOnPointerOver",
        "CheckBoxCheckBackgroundFillCheckedPointerOver",
        "SliderThumbBackgroundPointerOver",
    ];

    private static readonly string[] AccentPressedKeys =
    [
        "ToggleSwitchFillOnPressed",
        "ToggleSwitchStrokeOnPressed",
        "CheckBoxCheckBackgroundFillCheckedPressed",
        "RadioButtonOuterEllipseCheckedStrokePointerOver",
    ];

    private readonly GuiConfigStore _configs;

    private Window? _watched;

    public ThemeService(GuiConfigStore configs)
    {
        _configs = configs;

        ApplicationThemeManager.Changed += (_, _) => ApplyButtonColor(_configs.Current.ButtonColor);
    }

    public void Apply(string theme)
    {
        if (_watched is not null)
        {
            SystemThemeWatcher.UnWatch(_watched);
            _watched = null;
        }

        if (theme == "Light")
        {
            ApplyTheme(ApplicationTheme.Light);
            return;
        }

        if (theme == "Dark")
        {
            ApplyTheme(ApplicationTheme.Dark);
            return;
        }

        if (Application.Current?.MainWindow is { } window)
        {
            SystemThemeWatcher.Watch(window, Backdrop, true);
            _watched = window;
        }

        ApplyTheme(FromSystemTheme(ApplicationThemeManager.GetSystemTheme()));
    }

    /// <summary>应用整套强调色，并补齐按钮与控件笔刷。</summary>
    public void ApplyButtonColor(string palette)
    {
        if (ButtonPalette.Accent(palette) is { } accent)
        {
            ApplicationAccentColorManager.Apply(accent, ApplicationThemeManager.GetAppTheme());
        }
        else
        {
            ApplicationAccentColorManager.ApplySystemAccent();
        }

        var primary = ApplicationAccentColorManager.PrimaryAccent;

        Set("AccentButtonBackground", WithAlpha(primary, 0xFF));
        Set("AccentButtonBackgroundPointerOver", WithAlpha(primary, 0xE5));
        Set("AccentButtonBackgroundPressed", WithAlpha(primary, 0xCC));
        Set("DangerButtonBackground", ButtonPalette.Danger(palette, primary));

        foreach (var key in AccentKeys)
        {
            Set(key, WithAlpha(primary, 0xFF));
        }

        foreach (var key in AccentPointerOverKeys)
        {
            Set(key, WithAlpha(primary, 0xE5));
        }

        foreach (var key in AccentPressedKeys)
        {
            Set(key, WithAlpha(primary, 0xCC));
        }

        Copy("AccentTextFillColorPrimaryBrush", "ComboBoxBorderBrushFocused", "ToggleButtonForegroundCheckedPointerOver");
        Copy("AccentTextFillColorSecondaryBrush", "ToggleButtonBackgroundCheckedPressed");
    }

    private static void Set(string key, Color color) =>
        Application.Current.Resources[key] = new SolidColorBrush(color);

    private static void Copy(string from, params string[] targets)
    {
        if (Application.Current.Resources[from] is not SolidColorBrush brush)
        {
            return;
        }

        foreach (var target in targets)
        {
            Set(target, brush.Color);
        }
    }

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private static ApplicationTheme FromSystemTheme(SystemTheme systemTheme) =>
        systemTheme is SystemTheme.Dark
            or SystemTheme.HC1
            or SystemTheme.HC2
            or SystemTheme.HCBlack
            ? ApplicationTheme.Dark
            : ApplicationTheme.Light;

    private static void ApplyTheme(ApplicationTheme theme) =>
        ApplicationThemeManager.Apply(theme, Backdrop, updateAccent: true);
}
