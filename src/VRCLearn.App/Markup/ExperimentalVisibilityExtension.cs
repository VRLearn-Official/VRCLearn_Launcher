using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Markup;

public sealed class ExperimentalVisibilityExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if ((Application.Current?.MainWindow?.DataContext as MainWindowViewModel)?.Experimental is not { } experimental)
        {
            return Visibility.Collapsed;
        }

        var binding = new Binding(nameof(ExperimentalFeaturesService.IsEnabled))
        {
            Source = experimental,
            Converter = new BooleanToVisibilityConverter(),
        };

        return binding.ProvideValue(serviceProvider);
    }
}
