using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace AvaloniaDesigner;

public sealed class Design : AvaloniaObject
{
    public static readonly AttachedProperty<string?> PathProperty = AvaloniaProperty.RegisterAttached<Design, Control, string?>("Path");
    public static string? GetPath(Control control) => control.GetValue(PathProperty);
    public static void SetPath(Control control, string? value) => control.SetValue(PathProperty, value);
}

public sealed record PreviewResult(Control Root, IReadOnlyDictionary<string, Control> Controls);

public sealed class PreviewRenderer(ControlCatalog catalog)
{
    public PreviewResult Render(XDocument document, Uri? baseUri = null)
    {
        var clone = new XDocument(document);
        var root = clone.Root!;
        var designNamespace = (XNamespace)"clr-namespace:AvaloniaDesigner;assembly=AvaloniaDesigner";
        root.SetAttributeValue(XNamespace.Xmlns + "studio", designNamespace.NamespaceName);
        var model = new DesignDocument(clone.ToString(), catalog);
        foreach (var element in model.ControlElements(clone))
            element.SetAttributeValue(designNamespace + "Design.Path", DesignDocument.PathOf(element));
        foreach (var attribute in root.DescendantsAndSelf().Attributes(DesignDocument.Xaml + "Class").ToArray()) attribute.Remove();
        if (root.Name.LocalName == "Window")
        {
            root.Name = DesignDocument.Ui + "UserControl";
            foreach (var property in new[] { "Title", "Icon", "WindowStartupLocation", "WindowState", "CanResize", "SystemDecorations", "ShowInTaskbar", "SizeToContent", "ExtendClientAreaToDecorationsHint", "TransparencyLevelHint" })
                root.Attribute(property)?.Remove();
            foreach (var property in root.Elements().Where(element => element.Name.LocalName.StartsWith("Window.")))
                property.Name = DesignDocument.Ui + property.Name.LocalName.Replace("Window.", "UserControl.");
        }
        if (catalog.Resolve(root) == null) throw new InvalidOperationException("The root must be a supported visual control, UserControl, or Window.");
        var loaded = AvaloniaRuntimeXamlLoader.Load(clone.ToString(), typeof(Design).Assembly, uri: baseUri);
        if (loaded is not Control control || control is TopLevel)
            throw new InvalidOperationException("This AXAML does not describe an embeddable view.");
        control.Measure(new Size(1600, 1200));
        control.Arrange(new Rect(control.DesiredSize));
        var controls = control.GetLogicalDescendants().OfType<Control>()
            .Concat(control.GetVisualDescendants().OfType<Control>()).Prepend(control)
            .Where(child => Design.GetPath(child) != null)
            .DistinctBy(child => Design.GetPath(child))
            .ToDictionary(child => Design.GetPath(child)!, child => child);
        return new PreviewResult(control, controls);
    }

    public static string ErrorMessage(Exception error)
    {
        while (error.InnerException != null) error = error.InnerException;
        return error.Message;
    }
}
