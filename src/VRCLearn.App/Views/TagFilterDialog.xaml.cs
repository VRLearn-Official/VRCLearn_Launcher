using System.Windows;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class TagFilterDialog : DialogWindowBase
{
    private readonly List<TagChoice> _choices;

    public TagFilterDialog(IReadOnlyList<string> tags, IReadOnlyCollection<string> selected)
    {
        InitializeComponent();

        var set = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);

        _choices = [.. tags.Select(x => new TagChoice(x, set.Contains(x)))];

        Tags.ItemsSource = _choices;
    }

    public IReadOnlyList<string> SelectedTags { get; private set; } = [];

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        SelectedTags = [.. _choices.Where(x => x.IsSelected).Select(x => x.Name)];
        DialogResult = true;
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        SelectedTags = [];
        DialogResult = true;
    }
}
