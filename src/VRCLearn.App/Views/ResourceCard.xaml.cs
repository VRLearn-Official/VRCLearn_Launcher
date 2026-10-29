using System.Windows.Controls;
using VRCLearn.App.ViewModels;

namespace VRCLearn.App.Views;

public partial class ResourceCard : UserControl
{
    public ResourceCard()
    {
        InitializeComponent();

        Loaded += (_, _) => _ = LoadCoverAsync();
        DataContextChanged += (_, _) => _ = LoadCoverAsync();
    }

    private Task LoadCoverAsync() =>
        DataContext is ResourceItemViewModel item ? item.LoadCoverAsync() : Task.CompletedTask;
}
