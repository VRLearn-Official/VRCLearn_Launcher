using System.Globalization;
using System.Windows;
using VRCLearn.App.Services;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.App.Views;

public partial class AutoBackupDialog : DialogWindowBase
{
    private readonly GuiConfigStore _configs;
    private readonly AutoBackupService _autoBackups;
    private readonly DialogService _dialogs;
    private readonly LocalizationService _localization;

    public AutoBackupDialog(
        GuiConfigStore configs,
        AutoBackupService autoBackups,
        DialogService dialogs,
        LocalizationService localization)
    {
        InitializeComponent();

        _configs = configs;
        _autoBackups = autoBackups;
        _dialogs = dialogs;
        _localization = localization;

        EnabledSwitch.IsChecked = configs.Current.AutoBackupEnabled;
        MinuteBox.Text = configs.Current.AutoBackupIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        RetentionBox.Text = configs.Current.AutoBackupRetention.ToString(CultureInfo.InvariantCulture);

        Loaded += (_, _) => EnabledSwitch.Focus();
    }

    public void Save()
    {
        StatusText.Text = "";

        if (!int.TryParse(
                MinuteBox.Text.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var minutes)
            || minutes is < 15 or > 1440)
        {
            StatusText.Text = _localization["AutoBackup.InvalidMinutes"];

            return;
        }

        if (!int.TryParse(
                RetentionBox.Text.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var retention)
            || retention is < 1 or > 100)
        {
            StatusText.Text = _localization["AutoBackup.InvalidRetention"];

            return;
        }

        var enabled = EnabledSwitch.IsChecked == true;

        if (enabled
            && !_dialogs.ShowConfirm(
                _localization["AutoBackup.WarnTitle"],
                _localization["AutoBackup.WarnMessage"],
                _localization["AutoBackup.WarnButton"],
                owner: this))
        {
            return;
        }

        var config = _configs.Current;

        config.AutoBackupEnabled = enabled;
        config.AutoBackupIntervalMinutes = minutes;
        config.AutoBackupRetention = retention;

        _configs.Save();
        _autoBackups.RestartSchedule();

        DialogResult = true;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => Save();
}
