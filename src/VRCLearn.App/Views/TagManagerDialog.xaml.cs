using System.Windows.Input;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class TagManagerDialog : DialogWindowBase
{
    public TagManagerDialog()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            ViewModel.Load();
            NewTagBox.Focus();
        };
    }

    private TagManagerViewModel ViewModel => (TagManagerViewModel)DataContext;

    private void OnNewTagKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ViewModel.AddTagCommand.Execute(null);
        e.Handled = true;
    }
}
