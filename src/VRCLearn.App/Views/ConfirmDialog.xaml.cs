using System.Windows;
using Wpf.Ui.Controls;

namespace VRCLearn.App.Views;

public partial class ConfirmDialog : DialogWindowBase
{
    public ConfirmDialog(
        string title,
        string message,
        string confirmText,
        string? extraText = null,
        bool confirmDanger = false,
        bool extraDanger = false,
        string? detailTitle = null,
        string? detailBody = null)
    {
        InitializeComponent();

        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;

        if (detailTitle is not null)
        {
            DetailTitleText.Text = detailTitle;
            DetailBodyText.Text = detailBody ?? "";
            DetailBlock.Visibility = Visibility.Visible;
        }

        if (confirmDanger)
        {
            ConfirmButton.Appearance = ControlAppearance.Danger;
        }

        if (extraText is null)
        {
            ExtraButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            ExtraButton.Content = extraText;

            if (extraDanger)
            {
                ExtraButton.Appearance = ControlAppearance.Danger;
            }
        }
    }

    public bool ExtraClicked { get; private set; }

    private void OnExtraClick(object sender, RoutedEventArgs e)
    {
        ExtraClicked = true;
        DialogResult = false;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
