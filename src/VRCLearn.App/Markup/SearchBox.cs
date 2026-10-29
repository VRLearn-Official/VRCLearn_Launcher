using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace VRCLearn.App.Markup;

public static class SearchBox
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SearchBox),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty ViewProperty =
        DependencyProperty.RegisterAttached("View", typeof(ICollectionView), typeof(SearchBox));

    private static readonly DependencyProperty TypingProperty =
        DependencyProperty.RegisterAttached("Typing", typeof(bool), typeof(SearchBox));

    /// <summary>正在回填第一个结果。</summary>
    private static readonly DependencyProperty FillingProperty =
        DependencyProperty.RegisterAttached("Filling", typeof(bool), typeof(SearchBox));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ComboBox combo)
        {
            return;
        }

        combo.Loaded -= OnLoaded;
        combo.MouseLeftButtonUp -= OnMouseUp;
        combo.PreviewTextInput -= OnTextInput;
        combo.PreviewKeyDown -= OnPreviewKeyDown;
        combo.RemoveHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnTextChanged));

        if (e.NewValue is not true)
        {
            return;
        }

        combo.Loaded += OnLoaded;
        combo.MouseLeftButtonUp += OnMouseUp;
        combo.PreviewTextInput += OnTextInput;
        combo.PreviewKeyDown += OnPreviewKeyDown;
        combo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnTextChanged));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox combo
            || combo.GetValue(ViewProperty) is not null
            || combo.ItemsSource is not { } source)
        {
            return;
        }

        var view = new CollectionViewSource { Source = source }.View;
        view.Filter = item => !(bool)combo.GetValue(TypingProperty) || Matches(item, combo.Text);

        combo.SetValue(ViewProperty, view);
        combo.IsSynchronizedWithCurrentItem = false;
        combo.ItemsSource = view;
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ComboBox combo
            || combo.GetValue(ViewProperty) is not ICollectionView view
            || combo.IsDropDownOpen)
        {
            return;
        }

        combo.SetValue(TypingProperty, false);
        view.Refresh();
        combo.IsDropDownOpen = true;

        KeepCaretAtClick(combo, e);
    }

    private static void KeepCaretAtClick(ComboBox combo, MouseButtonEventArgs e)
    {
        combo.ApplyTemplate();

        if (combo.Template.FindName("PART_EditableTextBox", combo) is not TextBox box)
        {
            return;
        }

        var point = e.GetPosition(box);

        combo.Dispatcher.BeginInvoke(
            () =>
            {
                var index = box.GetCharacterIndexFromPoint(point, snapToText: true);

                if (index >= 0)
                {
                    box.Select(index, 0);
                }
            },
            DispatcherPriority.Input);
    }

    private static void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is ComboBox combo)
        {
            combo.SetValue(TypingProperty, true);
        }
    }

    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not ComboBox combo
            || combo.GetValue(ViewProperty) is not ICollectionView view
            || (bool)combo.GetValue(FillingProperty))
        {
            return;
        }

        view.Refresh();

        if (view.IsEmpty)
        {
            combo.IsDropDownOpen = false;
        }
        else if ((bool)combo.GetValue(TypingProperty) && combo.IsKeyboardFocusWithin)
        {
            combo.IsDropDownOpen = true;
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBox combo
            || e.Key != Key.Enter
            || combo.GetValue(ViewProperty) is not ICollectionView view
            || view.Cast<object>().FirstOrDefault() is not { } first)
        {
            return;
        }

        combo.SetValue(FillingProperty, true);
        combo.Text = TextOf(combo, first);
        combo.SetValue(FillingProperty, false);
        combo.SetValue(TypingProperty, false);
        combo.IsDropDownOpen = false;

        e.Handled = true;
    }

    private static bool Matches(object? item, string text) =>
        text.Length == 0 || (TextOf(null, item) is { } value && value.Contains(text, StringComparison.OrdinalIgnoreCase));

    private static string TextOf(ComboBox? combo, object? item)
    {
        if (item is null)
        {
            return "";
        }

        if (combo?.DisplayMemberPath is { Length: > 0 } path
            && item.GetType().GetProperty(path) is { } property)
        {
            return property.GetValue(item)?.ToString() ?? "";
        }

        return item.ToString() ?? "";
    }
}
