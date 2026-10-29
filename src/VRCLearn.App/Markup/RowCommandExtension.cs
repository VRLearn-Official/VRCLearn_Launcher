using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;

namespace VRCLearn.App.Markup;

[MarkupExtensionReturnType(typeof(Binding))]
public sealed class RowCommandExtension : MarkupExtension
{
    public RowCommandExtension()
    {
    }

    public RowCommandExtension(string path) => Path = path;

    public string Path { get; set; } = "";

    public Type AncestorType { get; set; } = typeof(ItemsControl);

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"DataContext.{Path}")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, AncestorType, 1),
        }.ProvideValue(serviceProvider);
}
