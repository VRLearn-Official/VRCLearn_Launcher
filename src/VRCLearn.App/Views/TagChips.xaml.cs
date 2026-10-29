using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VRCLearn.App.Views;

public partial class TagChips : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(TagChips));

    public static readonly DependencyProperty IsRemovableProperty = DependencyProperty.Register(
        nameof(IsRemovable),
        typeof(bool),
        typeof(TagChips));

    public static readonly DependencyProperty RemoveCommandProperty = DependencyProperty.Register(
        nameof(RemoveCommand),
        typeof(ICommand),
        typeof(TagChips));

    public TagChips() => InitializeComponent();

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public bool IsRemovable
    {
        get => (bool)GetValue(IsRemovableProperty);
        set => SetValue(IsRemovableProperty, value);
    }

    public ICommand? RemoveCommand
    {
        get => (ICommand?)GetValue(RemoveCommandProperty);
        set => SetValue(RemoveCommandProperty, value);
    }

    private void OnRemoveTag(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: { } tag } && RemoveCommand is { } command)
        {
            command.Execute(tag);
        }

        e.Handled = true;
    }
}
