using System.Windows;
using System.Windows.Controls;

namespace VRCLearn.App.Views;

public partial class LicensesDialog : DialogWindowBase
{
    private static readonly (string Name, string Marker)[] SectionMarkers =
    [
        ("WPF-UI", "1. WPF-UI 4.3.0"),
        ("CommunityToolkit.Mvvm", "2. CommunityToolkit.Mvvm 8.4.2"),
        ("Microsoft.Extensions", "3. Microsoft.Extensions."),
        (".NET 桌面运行时", "4. .NET 10 Desktop Runtime"),
        ("ALCOM（vrc-get）", "5. ALCOM (vrc-get)"),
    ];

    private readonly IReadOnlyList<Section> _sections;

    public LicensesDialog(string title, string text, string closeText)
    {
        InitializeComponent();

        Title = title;
        BodyText.Text = text;
        CloseButton.Content = closeText;

        _sections = SectionMarkers
            .Select(x => new Section(x.Name, text.IndexOf(x.Marker, StringComparison.Ordinal)))
            .Where(x => x.Offset >= 0)
            .ToList();

        Sections.ItemsSource = _sections.Select(x => x.Name).ToList();

        Loaded += (_, _) =>
        {
            BodyText.UpdateLayout();
            Sections.SelectedIndex = 0;
        };
    }

    private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = Sections.SelectedIndex;

        if (index < 0 || index >= _sections.Count)
        {
            return;
        }

        var section = _sections[index];
        var line = BodyText.GetLineIndexFromCharacterIndex(section.Offset);

        if (line >= 0)
        {
            BodyText.ScrollToLine(line);
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private sealed record Section(string Name, int Offset);
}
