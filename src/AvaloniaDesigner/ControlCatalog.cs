using System.Reflection;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace AvaloniaDesigner;

public sealed record ControlDescriptor(Type Type, string Category, string? UnavailableReason = null)
{
    public string Name => Type.Name;
}

public sealed record PropertyDescriptor(string Name, Type Type, string Category);

public sealed class ControlCatalog
{
    public IReadOnlyList<ControlDescriptor> Controls { get; }
    private readonly Dictionary<string, Type> types;

    public ControlCatalog()
    {
        var assemblies = new[] { typeof(Control).Assembly, typeof(DataGrid).Assembly, typeof(ColorPicker).Assembly };
        Controls = assemblies.SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => typeof(Control).IsAssignableFrom(type) && !type.IsAbstract && !type.IsGenericType
                && !typeof(TopLevel).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(type => type.Name)
            .Select(type => new ControlDescriptor(type, Category(type), UnavailableReason(type.Name))).ToArray();
        types = Controls.GroupBy(control => control.Name).ToDictionary(group => group.Key, group => group.First().Type);
        types[nameof(Window)] = typeof(Window);
    }

    public Type? Resolve(XElement element) => element.Name.Namespace == DesignDocument.Ui
        ? types.GetValueOrDefault(element.Name.LocalName) : null;

    public bool IsContainer(XElement element)
    {
        var type = Resolve(element);
        return type != null && (typeof(Panel).IsAssignableFrom(type) || typeof(Decorator).IsAssignableFrom(type)
            || typeof(ContentControl).IsAssignableFrom(type) || typeof(ItemsControl).IsAssignableFrom(type));
    }

    public IEnumerable<PropertyDescriptor> Properties(XElement element)
    {
        var type = Resolve(element);
        if (type == null) return [];
        var parent = element.Ancestors().FirstOrDefault(ancestor => Resolve(ancestor) != null)?.Name.LocalName;
        string[] attached = parent switch
        {
            "Grid" => ["Grid.Row", "Grid.Column", "Grid.RowSpan", "Grid.ColumnSpan"],
            "Canvas" => ["Canvas.Left", "Canvas.Top"],
            "DockPanel" => ["DockPanel.Dock"],
            _ => []
        };
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0)
            .Where(property => IsEditable(property.PropertyType) || property.Name is "Content" or "Header")
            .Select(property => new PropertyDescriptor(property.Name, property.PropertyType, PropertyCategory(property.Name)))
            .Concat(attached.Select(name => new PropertyDescriptor(name, name == "DockPanel.Dock" ? typeof(Dock) : typeof(double), "Position")))
            .Append(new PropertyDescriptor("Classes", typeof(string), "Appearance"));
        return properties.DistinctBy(property => property.Name).OrderBy(property => property.Category).ThenBy(property => property.Name);
    }

    public XElement Create(string name)
    {
        if (!types.ContainsKey(name) || name == nameof(Window)) throw new InvalidOperationException("Choose a control from the toolbox.");
        if (UnavailableReason(name) is { } reason) throw new InvalidOperationException(reason);
        var element = new XElement(DesignDocument.Ui + name);
        void Set(string property, string value) => element.SetAttributeValue(property, value);
        switch (name)
        {
            case "TextBlock": Set("Text", "Text block"); Set("FontSize", "16"); break;
            case "TextBox": Set("Watermark", "Enter text…"); Set("MinWidth", "180"); break;
            case "Button": Set("Content", "Button"); Set("Padding", "18,9"); break;
            case "CheckBox": case "RadioButton": case "ToggleButton": Set("Content", name); break;
            case "ToggleSwitch": Set("OnContent", "On"); Set("OffContent", "Off"); break;
            case "Slider": case "ProgressBar": Set("Value", "40"); Set("Width", "180"); break;
            case "NumericUpDown": Set("Value", "10"); Set("Width", "160"); break;
            case "Border": Set("BorderBrush", "#9383FF"); Set("BorderThickness", "1"); Set("CornerRadius", "8"); Set("MinWidth", "160"); Set("MinHeight", "100"); break;
            case "Grid": Set("RowDefinitions", "*,*"); Set("ColumnDefinitions", "*,*"); Set("MinWidth", "240"); Set("MinHeight", "160"); break;
            case "Canvas": case "StackPanel": case "WrapPanel": case "DockPanel":
                Set("MinWidth", "200"); Set("MinHeight", "120"); Set("Background", "Transparent");
                if (name == "StackPanel") Set("Spacing", "8");
                break;
            case "ComboBox": case "ListBox":
                Set("Width", "180");
                element.Add(new XElement(DesignDocument.Ui + (name == "ComboBox" ? "ComboBoxItem" : "ListBoxItem"), new XAttribute("Content", "First item")));
                element.Add(new XElement(DesignDocument.Ui + (name == "ComboBox" ? "ComboBoxItem" : "ListBoxItem"), new XAttribute("Content", "Second item")));
                Set("SelectedIndex", "0"); break;
            case "TabControl":
                element.Add(new XElement(DesignDocument.Ui + "TabItem", new XAttribute("Header", "Tab"), new XElement(DesignDocument.Ui + "TextBlock", new XAttribute("Text", "Tab content")))); break;
            case "Expander": Set("Header", "Details"); Set("IsExpanded", "True"); break;
            case "Rectangle": case "Ellipse": Set("Width", "100"); Set("Height", "80"); Set("Fill", "#9383FF"); break;
            case "Image": Set("Width", "160"); Set("Height", "100"); break;
            case "DataGrid": Set("Width", "320"); Set("Height", "180"); break;
            case "DateTimePickerPanel": case "OverlayLayer": Set("Width", "240"); Set("Height", "160"); break;
            default: Set("MinWidth", "80"); Set("MinHeight", "32"); break;
        }
        return element;
    }

    private static string? UnavailableReason(string name) => name switch
    {
        "DataGridRowGroupHeader" => "DataGridRowGroupHeader requires a DataGrid owner. Use a DataGrid and customize its template in AXAML.",
        "ExperimentalAcrylicBorder" => "ExperimentalAcrylicBorder requires platform composition that the embedded preview cannot reliably provide. Use Border for portable previews.",
        _ => null
    };

    private static bool IsEditable(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || type == typeof(TimeSpan) || type == typeof(DateTimeOffset) || type == typeof(Uri)
            || type == typeof(Avalonia.Thickness) || type == typeof(Avalonia.CornerRadius)
            || typeof(IBrush).IsAssignableFrom(type) || type == typeof(FontFamily)
            || type == typeof(FontWeight) || type == typeof(Color) || type == typeof(RowDefinitions) || type == typeof(ColumnDefinitions);
    }

    private static string Category(Type type) => typeof(Panel).IsAssignableFrom(type) || typeof(Decorator).IsAssignableFrom(type)
        ? "Layout" : typeof(RangeBase).IsAssignableFrom(type) || typeof(TextBox).IsAssignableFrom(type)
        || typeof(Button).IsAssignableFrom(type) || type.Name is "CheckBox" or "ToggleSwitch" or "ComboBox" or "NumericUpDown"
        ? "Input" : type.Namespace?.Contains("Primitives") == true ? "Primitives" : "Display";

    private static string PropertyCategory(string name) => name is "Name" or "Text" or "Content" or "Header" or "Watermark"
        or "Value" or "Minimum" or "Maximum" or "IsChecked" or "IsOn" or "Source" or "SelectedIndex" ? "Content"
        : name is "Width" or "Height" or "MinWidth" or "MinHeight"
        or "MaxWidth" or "MaxHeight" or "Margin" or "Padding" or "HorizontalAlignment" or "VerticalAlignment"
        or "Orientation" or "Spacing" or "RowDefinitions" or "ColumnDefinitions" ? "Layout"
        : name.Contains("Brush") || name.Contains("ground") || name.StartsWith("Font") || name.Contains("Border")
        || name is "Opacity" or "CornerRadius" ? "Appearance" : "Advanced";
}
