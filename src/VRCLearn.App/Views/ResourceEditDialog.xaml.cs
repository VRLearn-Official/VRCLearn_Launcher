using System.Windows;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class ResourceEditDialog : DialogWindowBase
{
    public ResourceEditDialog()
    {
        InitializeComponent();

        Loaded += (_, _) => Title = LocalizationService.Instance[
            ViewModel.IsNew ? "Resources.Edit.NewTitle" : "Resources.Edit.Title"];
    }

    public ResourceEditOutcome? Outcome { get; private set; }

    private ResourceEditViewModel ViewModel => (ResourceEditViewModel)DataContext;

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.BuildResult() is not { } result)
        {
            return;
        }

        Outcome = new ResourceEditOutcome(result, false);
        DialogResult = true;
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        Outcome = new ResourceEditOutcome(null, true);
        DialogResult = true;
    }
}
