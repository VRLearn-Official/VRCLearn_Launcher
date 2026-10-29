using System.IO;
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

    private void OnAddPackagesClick(object sender, RoutedEventArgs e)
    {
        var files = DialogService.Instance.PickFiles(
            LocalizationService.Instance["Resources.Edit.PackagesAdd"],
            "unitypackage (*.unitypackage)|*.unitypackage|*|*");

        if (files.Length > 0)
        {
            ViewModel.AddPackages(files);
        }
    }

    private void OnPickManualClick(object sender, RoutedEventArgs e)
    {
        var file = DialogService.Instance.PickFile(
            LocalizationService.Instance["Resources.Edit.ManualPick"],
            "*.pdf;*.md;*.txt;*.doc;*.docx;*.rtf;*.odt;*.html;*.htm|*.pdf;*.md;*.txt;*.doc;*.docx;*.rtf;*.odt;*.html;*.htm|*|*",
            "",
            this);

        if (file is not null)
        {
            ViewModel.Manual = file;
        }
    }

    private void OnPickCoverClick(object sender, RoutedEventArgs e)
    {
        var file = DialogService.Instance.PickFile(
            LocalizationService.Instance["Resources.Edit.CoverPick"],
            "图片 (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|*|*",
            "",
            this);

        if (file is null)
        {
            return;
        }

        try
        {
            ViewModel.SetCover(CoverImageScaler.RenderSquare(file));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FormatException or NotSupportedException)
        {
            ViewModel.Status = LocalizationService.Instance.Format(
                "Resources.Edit.CoverInvalid",
                exception.Message);
        }
    }

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
