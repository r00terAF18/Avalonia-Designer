using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace AvaloniaDesigner;

public sealed partial class MainWindow
{
    private bool buildingInspector;

    private void BuildInspector()
    {
        if (buildingInspector) return;
        buildingInspector = true;
        try
        {
            PropertyPanel.Children.Clear();
            var element = document.Find(selectedPath);
            SelectionTitle.Text = element.Name.LocalName;
            var filter = PropertySearch.Text ?? "";
            var descriptors = catalog.Properties(element).ToList();
            foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration && attribute.Name.Namespace == XNamespace.None))
                if (descriptors.All(property => property.Name != attribute.Name.LocalName))
                    descriptors.Add(new PropertyDescriptor(attribute.Name.LocalName, typeof(string), "AXAML"));
            var properties = descriptors.Where(property => property.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var group in properties.GroupBy(property => property.Category).OrderBy(group => group.Key switch
                { "Content" => 0, "Layout" => 1, "Position" => 2, "Appearance" => 3, _ => 4 }))
            {
                if (group.Key == "Advanced" && filter.Length == 0)
                {
                    var advanced = new StackPanel { Spacing = 8 };
                    foreach (var property in group) advanced.Children.Add(PropertyRow(element, property));
                    PropertyPanel.Children.Add(new Expander { Header = $"Advanced ({group.Count()})", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
                    continue;
                }
                var title = new TextBlock { Text = group.Key.ToUpperInvariant(), Margin = new Thickness(2, 14, 0, 4), FontSize = 10, Foreground = Brush.Parse("#A797FF") };
                PropertyPanel.Children.Add(title);
                foreach (var property in group) PropertyPanel.Children.Add(PropertyRow(element, property));
            }
        }
        catch (InvalidOperationException) { SelectionTitle.Text = "Select a control"; }
        finally { buildingInspector = false; }
    }

    private Control PropertyRow(XElement element, PropertyDescriptor property)
    {
        var path = selectedPath;
        var current = (string?)element.Attribute(property.Name);
        if (property.Name == "Name") current ??= (string?)element.Attribute(DesignDocument.Xaml + "Name");
        var container = new StackPanel { Spacing = 4 };
        var label = new TextBlock { Text = property.Name, FontSize = 11, Foreground = Brush.Parse(current == null ? "#959CB1" : "#D2C9FF") };
        container.Children.Add(label);
        var isColor = typeof(IBrush).IsAssignableFrom(property.Type) || property.Type == typeof(Color);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(isColor ? "*,30,30" : "*,30") };
        var editor = new TextBox { Text = current ?? "", Watermark = "Default / inherited", FontSize = 12, MinHeight = 30, Padding = new Thickness(8, 5) };
        ToolTip.SetTip(editor, property.Type.Name + " · AXAML values and resource expressions supported");
        var propertyType = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
        if ((propertyType.IsEnum || propertyType == typeof(bool)) && current?.StartsWith('{') != true)
        {
            var choices = new[] { "Default / inherited" }.Concat(propertyType == typeof(bool) ? ["True", "False"] : Enum.GetNames(propertyType)).ToArray();
            var picker = new ComboBox { ItemsSource = choices, SelectedIndex = current == null ? 0 : Array.FindIndex(choices, value => value.Equals(current, StringComparison.OrdinalIgnoreCase)), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12, MinHeight = 30 };
            picker.SelectionChanged += (_, _) =>
            {
                if (buildingInspector || picker.SelectedIndex < 0) return;
                RunEdit(() => document.SetProperty(path, property.Name, picker.SelectedIndex == 0 ? null : picker.SelectedItem?.ToString()));
            };
            row.Children.Add(picker);
        }
        else
        {
            void Apply()
            {
                if (buildingInspector || (editor.Text ?? "") == (current ?? "")) return;
                var value = editor.Text ?? "";
                if (RunEdit(() => document.SetProperty(path, property.Name, value))) current = value;
            }
            editor.LostFocus += (_, _) => Apply();
            editor.KeyDown += (_, args) => { if (args.Key == Key.Enter) { args.Handled = true; Apply(); } };
            row.Children.Add(editor);
        }
        var reset = new Button { Content = "↺", Padding = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };
        ToolTip.SetTip(reset, "Remove local value");
        reset.Click += (_, _) => RunEdit(() => document.SetProperty(path, property.Name, null));
        Grid.SetColumn(reset, 1);
        row.Children.Add(reset);
        if (isColor)
        {
            var chooseColor = new Button { Content = "●", Padding = new Thickness(5), Foreground = Color.TryParse(current, out var parsed) ? new SolidColorBrush(parsed) : Brush.Parse("#A797FF"), Background = Brushes.Transparent };
            ToolTip.SetTip(chooseColor, "Choose a solid color");
            chooseColor.Click += async (_, _) =>
            {
                var chosen = await ChooseColor(current);
                if (chosen != null) RunEdit(() => document.SetProperty(path, property.Name, chosen));
            };
            Grid.SetColumn(chooseColor, 2);
            row.Children.Add(chooseColor);
        }
        container.Children.Add(row);
        return container;
    }

    private async Task<string?> ChooseColor(string? current)
    {
        var dialog = new Window { Title = "Choose a color", Width = 450, Height = 550, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var colorView = new ColorView { Color = Color.TryParse(current, out var color) ? color : Color.Parse("#7864E8") };
        var panel = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel" };
        var apply = new Button { Content = "Apply color" };
        apply.Classes.Add("primary");
        cancel.Click += (_, _) => dialog.Close();
        apply.Click += (_, _) => dialog.Close(colorView.Color.ToString());
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(colorView);
        dialog.Content = panel;
        return await dialog.ShowDialog<string?>(this);
    }

    private async Task CreateStyle()
    {
        if (sourceDirty) { ShowError("Apply or revert the AXAML draft before creating a style."); return; }
        var path = selectedPath;
        var element = document.Find(path);
        var properties = catalog.Properties(element).Where(property => property.Name is not ("Name" or "Classes") && !property.Name.Contains('.')).Select(property => property.Name).ToArray();
        var dialog = new Window { Title = "Create a reusable style", Width = 440, Height = 390, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        var className = new TextBox { Text = char.ToLowerInvariant(element.Name.LocalName[0]) + element.Name.LocalName[1..] + "Style", Watermark = "Class name" };
        var propertyPicker = new ComboBox { ItemsSource = properties, SelectedItem = properties.Contains("Background") ? "Background" : properties.FirstOrDefault(), HorizontalAlignment = HorizontalAlignment.Stretch };
        var value = new TextBox { Text = "#7864E8", Watermark = "AXAML value or resource expression" };
        panel.Children.Add(new TextBlock { Text = "Style " + element.Name.LocalName, FontSize = 20 });
        panel.Children.Add(new TextBlock { Text = "Adds a reusable class and setter to this view. The selected property's local value will be removed so the style can apply.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(className);
        panel.Children.Add(propertyPicker);
        panel.Children.Add(value);
        var errorText = new TextBlock { Foreground = Brush.Parse("#FFAFBF"), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(errorText);
        var apply = new Button { Content = "Apply style", HorizontalAlignment = HorizontalAlignment.Right };
        apply.Classes.Add("primary");
        apply.Click += (_, _) =>
        {
            if (RunEdit(() => selectedPath = document.SetClassStyle(path, className.Text ?? "", propertyPicker.SelectedItem?.ToString() ?? "", value.Text ?? ""), "Reusable style applied")) dialog.Close();
            else errorText.Text = ErrorLabel.Text;
        };
        panel.Children.Add(apply);
        dialog.Content = panel;
        await dialog.ShowDialog(this);
    }
}
