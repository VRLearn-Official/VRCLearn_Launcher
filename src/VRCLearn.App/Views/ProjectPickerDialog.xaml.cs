using System.Windows;
using System.Windows.Controls;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class ProjectPickerDialog : DialogWindowBase
{
    public ProjectPickerDialog(
        IReadOnlyList<ProjectChoice> projects,
        ProjectPickerChrome chrome)
    {
        InitializeComponent();

        Title = chrome.Title;
        HintText.Text = chrome.Hint;
        CountText.Text = chrome.Summary;
        CountText.Visibility = chrome.Summary.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        WarningText.Text = chrome.Note;
        EmptyText.Text = chrome.EmptyText;
        ImportButton.Content = chrome.ConfirmText;

        ProjectList.ItemsSource = projects;
        ProjectList.SelectedItem = projects.FirstOrDefault(x => x.IsSelectable);
    }

    public string? SelectedPath { get; private set; }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ImportButton.IsEnabled = ProjectList.SelectedItem is ProjectChoice { IsSelectable: true };

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (ProjectList.SelectedItem is not ProjectChoice { IsSelectable: true } choice)
        {
            return;
        }

        SelectedPath = choice.Path;
        DialogResult = true;
    }
}
