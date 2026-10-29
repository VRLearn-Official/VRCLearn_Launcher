using System.Windows;
using VRCLearn.App.Services;
using VRCLearn.Core.Projects;

namespace VRCLearn.App.Views;

public partial class RenameDialog : DialogWindowBase
{
    public RenameDialog(string title, string currentName, string confirmText)
    {
        InitializeComponent();

        Title = title;
        ConfirmButton.Content = confirmText;

        NameBox.Text = currentName;

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string NewName => NameBox.Text.Trim();

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        var name = NewName;

        if (name.Length == 0)
        {
            StatusText.Text = LocalizationService.Instance["Templates.Status.NameEmpty"];
            return;
        }

        if (!ProjectService.IsValidName(name))
        {
            StatusText.Text = LocalizationService.Instance.Format("Templates.Status.NameInvalid", name);
            return;
        }

        DialogResult = true;
    }
}
