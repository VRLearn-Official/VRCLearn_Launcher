using System.Windows;
using System.Windows.Documents;
using VRCLearn.Infrastructure.Shell;

namespace VRCLearn.App.Views;

public sealed record PluginTrustInfo(
    string Name,
    string Id,
    string Version,
    string Source,
    string SourceUrl,
    string Requires);

public partial class PluginTrustDialog : DialogWindowBase
{
    public PluginTrustDialog(
        string title,
        string warning,
        PluginTrustInfo info,
        string installText,
        string cancelText)
    {
        InitializeComponent();

        Title = TitleText.Text = title;
        WarningText.Text = warning;
        NameText.Text = info.Name;
        IdText.Text = info.Id;
        VersionText.Text = info.Version;
        if (info.SourceUrl.Length > 0)
        {
            var link = new Hyperlink(new Run(info.Source));
            link.SetResourceReference(TextElement.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
            link.ToolTip = info.SourceUrl;
            link.Click += (_, _) => ShellLauncher.OpenUrl(info.SourceUrl);
            SourceText.Inlines.Add(link);
        }
        else
        {
            SourceText.Text = info.Source;
        }
        RequiresText.Text = info.Requires;
        InstallButton.Content = installText;
        CancelButton.Content = cancelText;
    }

    private void OnInstallClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
