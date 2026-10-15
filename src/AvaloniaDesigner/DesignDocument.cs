using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;

namespace AvaloniaDesigner;

public sealed record OutlineEntry(string Path, string Title, int Depth);

public sealed class DesignDocument
{
    public static readonly XNamespace Ui = "https://github.com/avaloniaui";
    public static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private readonly ControlCatalog catalog;
    private readonly List<string> history = [];
    private int historyIndex;
    private string savedSource;

    public XDocument Xml { get; private set; }
    public string Source => Xml.ToString();
    public bool IsDirty => Source != savedSource;
    public bool CanUndo => historyIndex > 0;
    public bool CanRedo => historyIndex < history.Count - 1;
    public Action<XDocument>? Validate { get; set; }

    public DesignDocument(string source, ControlCatalog catalog)
    {
        this.catalog = catalog;
        Xml = Parse(source);
        savedSource = Source;
        history.Add(Source);
    }

    public static XDocument Parse(string source)
    {
        if (source.Length > 2_000_000) throw new InvalidOperationException("Views larger than 2 MB are not supported.");
        using var reader = XmlReader.Create(new StringReader(source), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader, LoadOptions.SetLineInfo);
        if (document.Root == null || document.Root.Name.Namespace != Ui)
            throw new InvalidOperationException("The root must be an Avalonia control in the Avalonia XML namespace.");
        return document;
    }

    public static string PathOf(XElement element) => element.Parent == null ? "0"
        : PathOf(element.Parent) + "/" + element.Parent.Elements().TakeWhile(sibling => sibling != element).Count();

    public static XElement Find(XDocument document, string path)
    {
        var current = document.Root ?? throw new InvalidOperationException("The document has no root.");
        foreach (var part in path.Split('/').Skip(1))
            current = current.Elements().ElementAtOrDefault(int.Parse(part, CultureInfo.InvariantCulture))
                ?? throw new InvalidOperationException("The selected control no longer exists.");
        return current;
    }

    public XElement Find(string path) => Find(Xml, path);

    public IEnumerable<OutlineEntry> Outline() => ControlElements(Xml).Select(element => new OutlineEntry(
        PathOf(element), element.Name.LocalName + ((element.Attribute("Name") ?? element.Attribute(Xaml + "Name")) is { } name ? "  ·  " + name.Value : ""),
        element.Ancestors().Count(ancestor => catalog.Resolve(ancestor) != null)));

    public IEnumerable<XElement> ControlElements(XDocument document) => document.Root!.DescendantsAndSelf()
        .Where(element => catalog.Resolve(element) != null && !element.Ancestors().Any(IsResourceOrTemplate));

    private static bool IsResourceOrTemplate(XElement element) => element.Name.LocalName is "Style" or "ControlTheme"
        or "DataTemplate" or "ControlTemplate" or "ItemsPanelTemplate"
        || element.Name.LocalName.EndsWith(".Resources") || element.Name.LocalName.EndsWith(".Styles")
        || element.Name.LocalName.EndsWith(".DataTemplates") || element.Name.LocalName.EndsWith(".Template")
        || element.Name.LocalName.EndsWith(".ItemsPanel");

    public void Replace(string source) => Commit(Parse(source));
    public void MarkSaved() => savedSource = Source;

    public void Edit(Action<XDocument> edit)
    {
        var next = new XDocument(Xml);
        edit(next);
        Commit(next);
    }

    private void Commit(XDocument next)
    {
        var source = next.ToString();
        if (source == Source) return;
        Validate?.Invoke(next);
        history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
        history.Add(source);
        if (history.Count > 101) history.RemoveAt(0);
        historyIndex = history.Count - 1;
        Xml = next;
    }

    public void Undo()
    {
        if (CanUndo) Xml = Parse(history[--historyIndex]);
    }

    public void Redo()
    {
        if (CanRedo) Xml = Parse(history[++historyIndex]);
    }

    public void SetProperty(string path, string name, string? value) => Edit(document =>
    {
        var element = Find(document, path);
        var attributeName = name == "x:Name" ? Xaml + "Name" : XName.Get(name);
        if (name is "Name" or "x:Name")
        {
            element.Attribute(name == "Name" ? Xaml + "Name" : XName.Get("Name"))?.Remove();
            if (!string.IsNullOrEmpty(value) && document.Descendants().Any(other => other != element
                && (string?) (other.Attribute("Name") ?? other.Attribute(Xaml + "Name")) == value))
                throw new InvalidOperationException("A control already has that name.");
        }
        element.SetAttributeValue(attributeName, value);
    });

    public string Insert(string parentPath, string controlName, double left = 24, double top = 24)
    {
        var result = parentPath;
        Edit(document =>
        {
            var parent = Find(document, parentPath);
            var element = catalog.Create(controlName);
            AddChild(parent, element);
            if (parent.Name.LocalName == "Canvas") SetPosition(element, left, top);
            result = PathOf(element);
        });
        return result;
    }

    public void Delete(string path) => Edit(document =>
    {
        if (path == "0") throw new InvalidOperationException("The root cannot be deleted.");
        Find(document, path).Remove();
    });

    public string Duplicate(string path)
    {
        var result = path;
        Edit(document =>
        {
            var original = Find(document, path);
            var parent = original.Ancestors().FirstOrDefault(element => catalog.Resolve(element) != null)
                ?? throw new InvalidOperationException("The root cannot be duplicated.");
            var type = catalog.Resolve(parent)!;
            if (!typeof(Panel).IsAssignableFrom(type) && !typeof(ItemsControl).IsAssignableFrom(type))
                throw new InvalidOperationException("Duplicate requires a panel or an items control parent.");
            var copy = new XElement(original);
            var names = document.Descendants().Attributes().Where(attribute => attribute.Name == "Name" || attribute.Name == Xaml + "Name")
                .Select(attribute => attribute.Value).ToHashSet();
            foreach (var attribute in copy.DescendantsAndSelf().Attributes().Where(attribute => attribute.Name == "Name" || attribute.Name == Xaml + "Name"))
            {
                var name = attribute.Value + "Copy";
                while (!names.Add(name)) name += "Copy";
                attribute.Value = name;
            }
            if (parent.Name.LocalName == "Canvas") SetPosition(copy, Number(copy, "Canvas.Left", 0) + 16, Number(copy, "Canvas.Top", 0) + 16);
            original.AddAfterSelf(copy);
            result = PathOf(copy);
        });
        return result;
    }

    public string Reparent(string path, string parentPath)
    {
        var result = path;
        Edit(document =>
        {
            var element = Find(document, path);
            var parent = Find(document, parentPath);
            if (path == "0" || parent == element || parent.Ancestors().Contains(element))
                throw new InvalidOperationException("A control cannot be moved into itself or its descendants.");
            element.Remove();
            AddChild(parent, element);
            result = PathOf(element);
        });
        return result;
    }

    public string Reorder(string path, int direction)
    {
        var result = path;
        Edit(document =>
        {
            var element = Find(document, path);
            if (path == "0") return;
            var neighbor = direction < 0 ? element.ElementsBeforeSelf().LastOrDefault(sibling => catalog.Resolve(sibling) != null)
                : element.ElementsAfterSelf().FirstOrDefault(sibling => catalog.Resolve(sibling) != null);
            if (neighbor == null) return;
            element.Remove();
            if (direction < 0) neighbor.AddBeforeSelf(element); else neighbor.AddAfterSelf(element);
            result = PathOf(element);
        });
        return result;
    }

    public string SetClassStyle(string path, string className, string property, string value)
    {
        if (!Regex.IsMatch(className, "^[A-Za-z_][A-Za-z0-9_-]*$"))
            throw new InvalidOperationException("Use a class name starting with a letter, followed by letters, numbers, underscores or hyphens.");
        var result = path;
        Edit(document =>
        {
            var element = Find(document, path);
            if (!catalog.Properties(element).Any(descriptor => descriptor.Name == property))
                throw new InvalidOperationException("Choose an editable property of the selected control.");
            var root = document.Root!;
            var styles = root.Element(Ui + root.Name.LocalName + ".Styles");
            if (styles == null)
            {
                styles = new XElement(Ui + root.Name.LocalName + ".Styles");
                root.AddFirst(styles);
            }
            var selector = element.Name.LocalName + "." + className;
            var style = styles.Elements(Ui + "Style").FirstOrDefault(candidate => (string?)candidate.Attribute("Selector") == selector);
            if (style == null)
            {
                style = new XElement(Ui + "Style", new XAttribute("Selector", selector));
                styles.Add(style);
            }
            var setter = style.Elements(Ui + "Setter").FirstOrDefault(candidate => (string?)candidate.Attribute("Property") == property);
            if (setter == null)
            {
                setter = new XElement(Ui + "Setter", new XAttribute("Property", property));
                style.Add(setter);
            }
            setter.SetAttributeValue("Value", value);
            var classes = ((string?)element.Attribute("Classes") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!classes.Contains(className)) classes.Add(className);
            element.SetAttributeValue("Classes", string.Join(' ', classes));
            element.Attribute(property)?.Remove();
            result = PathOf(element);
        });
        return result;
    }

    private void AddChild(XElement parent, XElement child)
    {
        var type = catalog.Resolve(parent) ?? throw new InvalidOperationException("This element is not a supported container.");
        var multiple = typeof(Panel).IsAssignableFrom(type) || typeof(ItemsControl).IsAssignableFrom(type);
        if (!catalog.IsContainer(parent)) throw new InvalidOperationException("Select a layout container first.");
        if (parent.Attribute("ItemsSource") != null) throw new InvalidOperationException("Remove ItemsSource before adding individual items.");
        var property = typeof(Panel).IsAssignableFrom(type) ? "Children" : typeof(ItemsControl).IsAssignableFrom(type) ? "Items"
            : typeof(Decorator).IsAssignableFrom(type) ? "Child" : "Content";
        var slot = parent.Elements().FirstOrDefault(element => element.Name.LocalName.EndsWith("." + property)) ?? parent;
        if (!multiple && slot.Elements().Any(element => !element.Name.LocalName.Contains('.')))
            throw new InvalidOperationException($"{parent.Name.LocalName} accepts one child. Add to its existing panel, or replace its content in AXAML.");
        if (parent.Attribute(property)?.Value.StartsWith('{') == true)
            throw new InvalidOperationException("Remove the content binding before inserting a child.");
        parent.Attribute(property)?.Remove();
        slot.Add(child);
    }

    public static double Number(XElement element, string name, double fallback) =>
        double.TryParse((string?)element.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : fallback;

    public static void SetPosition(XElement element, double left, double top)
    {
        element.SetAttributeValue("Canvas.Left", Math.Round(left, 1).ToString(CultureInfo.InvariantCulture));
        element.SetAttributeValue("Canvas.Top", Math.Round(top, 1).ToString(CultureInfo.InvariantCulture));
    }
}
