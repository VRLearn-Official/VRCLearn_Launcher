using System.IO;
using System.Windows;
using System.Windows.Controls;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;
using VRCLearn.Core.Templates;

namespace VRCLearn.App.Views;

public partial class TemplatesPage : UserControl
{
    private static readonly string ImportPattern = $"*{TemplateFormat.Extension}";

    public TemplatesPage()
    {
        InitializeComponent();
        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true && DataContext is TemplatesViewModel viewModel)
            {
                _ = viewModel.ReloadAsync();
            }
        };
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TemplatesViewModel viewModel)
        {
            return;
        }

        var selected = DialogService.Instance.PickFiles(
            LocalizationService.Instance["Templates.Import"],
            $"{ImportPattern}|{ImportPattern}|*|*");

        if (selected.Length > 0)
        {
            await viewModel.ImportAsync(selected);
        }
    }

    private void OnAddUnityPackageClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TemplatesViewModel viewModel)
        {
            return;
        }

        var selected = DialogService.Instance.PickFiles(
            LocalizationService.Instance["Templates.Editor.UnityPackages"],
            "*.unitypackage|*.unitypackage|*|*");

        if (selected.Length > 0)
        {
            viewModel.AddUnityPackages(selected);
        }
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TemplateOption option }
            || DataContext is not TemplatesViewModel viewModel
            || viewModel.Export(option) is not { } content)
        {
            return;
        }

        var target = DialogService.Instance.PickSaveFile(
            LocalizationService.Instance["Templates.Export"],
            option.Display + viewModel.FileExtension,
            $"*{viewModel.FileExtension}|*{viewModel.FileExtension}|*|*");

        if (target is not null)
        {
            File.WriteAllText(target, content);
        }
    }
}
