using System.Windows;
using System.Windows.Media;

namespace VRCLearn.App.Markup;

internal static class VisualTree
{
    public static T? FindDescendant<T>(DependencyObject node)
        where T : DependencyObject
    {
        if (node is T match)
        {
            return match;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            if (FindDescendant<T>(VisualTreeHelper.GetChild(node, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
