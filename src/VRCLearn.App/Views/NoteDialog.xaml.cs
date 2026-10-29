using System.Windows;

namespace VRCLearn.App.Views;

public partial class NoteDialog : DialogWindowBase
{
    public NoteDialog(string title, string current, string confirmText, string hint)
    {
        InitializeComponent();

        Title = title;
        ConfirmButton.Content = confirmText;
        HintText.Text = hint;

        NoteBox.Text = current;

        Loaded += (_, _) =>
        {
            NoteBox.Focus();
            NoteBox.SelectAll();
        };
    }

    public string Note => NoteBox.Text.Trim();

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
