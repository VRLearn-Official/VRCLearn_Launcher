using System.Windows;

namespace VRCLearn.App.Views;

public partial class BackupOptionsDialog : DialogWindowBase
{
    public BackupOptionsDialog(string title, string confirmText)
    {
        InitializeComponent();

        Title = title;
        ConfirmButton.Content = confirmText;

        Loaded += (_, _) => NoteBox.Focus();
    }

    public string Note => NoteBox.Text.Trim();

    public bool IncludeLibrary => IncludeLibraryBox.IsChecked == true;

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
