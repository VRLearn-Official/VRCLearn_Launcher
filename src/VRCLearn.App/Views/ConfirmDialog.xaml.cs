using System.Windows;
using System.Windows.Documents;
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
        string? detailBody = null,
        double width = 480,
        IReadOnlyList<Inline>? messageInlines = null,
        IReadOnlyList<Inline>? detailBodyInlines = null,
        string? warning = null)
    {
        InitializeComponent();

        Title = title;
        TitleText.Text = title;
        Width = width;
        ConfirmButton.Content = confirmText;

        if (messageInlines is null)
        {
            MessageText.Text = message;
        }
        else
        {
            MessageText.Inlines.Clear();

            foreach (var inline in messageInlines)
            {
                MessageText.Inlines.Add(inline);
            }
        }

        if (detailTitle is not null)
        {
            DetailTitleText.Text = detailTitle;

            if (detailBodyInlines is null)
            {
                DetailBodyText.Text = detailBody ?? "";
            }
            else
            {
                DetailBodyText.Inlines.Clear();

                foreach (var inline in detailBodyInlines)
                {
                    DetailBodyText.Inlines.Add(inline);
                }
            }

            DetailBlock.Visibility = Visibility.Visible;
        }

        if (warning is not null)
        {
            WarningText.Text = warning;
            WarningBlock.Visibility = Visibility.Visible;
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
