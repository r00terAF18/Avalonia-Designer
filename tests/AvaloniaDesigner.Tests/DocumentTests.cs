using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace AvaloniaDesigner.Tests;

public sealed class DocumentTests
{
    private readonly ControlCatalog catalog = new();
    private DesignDocument Blank() => new(StarterDocument.Blank, catalog);

    [Fact]
    public void InsertEditUndoRedoPreservesSavedState()
    {
        var document = Blank();
        var path = document.Insert("0/0", "Button", 32, 48);
        Assert.Equal("32", (string?)document.Find(path).Attribute("Canvas.Left"));
        Assert.True(document.IsDirty);
        document.SetProperty(path, "Content", "Hello & <world>");
        Assert.Equal("Hello & <world>", (string?)DesignDocument.Parse(document.Source).Descendants(DesignDocument.Ui + "Button").Single().Attribute("Content"));
        document.Undo();
        Assert.Equal("Button", (string?)document.Find(path).Attribute("Content"));
        document.Undo();
        Assert.False(document.IsDirty);
        document.Redo();
        Assert.True(document.IsDirty);
        document.MarkSaved();
        Assert.False(document.IsDirty);
        document.SetProperty(path, "Width", "200");
        document.Undo();
        Assert.False(document.IsDirty);
        document.SetProperty(path, "Width", "300");
        Assert.False(document.CanRedo);
    }

    [Fact]
    public void FailedValidationDoesNotMutateDocumentOrHistory()
    {
        var document = Blank();
        var original = document.Source;
        document.Validate = _ => throw new InvalidOperationException("Invalid property");
        Assert.Throws<InvalidOperationException>(() => document.Insert("0/0", "Button"));
        Assert.Equal(original, document.Source);
        Assert.False(document.CanUndo);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void EditsPreserveResourcesStylesBindingsAndComments()
    {
        var source = """
            <UserControl xmlns="https://github.com/avaloniaui">
              <!-- Keep this -->
              <UserControl.Resources><SolidColorBrush x:Key="Accent" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">Red</SolidColorBrush></UserControl.Resources>
              <UserControl.Styles><Style Selector="Button"><Setter Property="Background" Value="{DynamicResource Accent}" /></Style></UserControl.Styles>
              <StackPanel><TextBlock Text="{Binding Title}" /></StackPanel>
            </UserControl>
            """;
        var document = new DesignDocument(source, catalog);
        document.Insert("0/2", "Button");
        Assert.Contains("<!-- Keep this -->", document.Source);
        Assert.Contains("{Binding Title}", document.Source);
        Assert.Contains("{DynamicResource Accent}", document.Source);
        Assert.Equal(4, document.Outline().Count());
    }

    [Fact]
    public void SingleChildContainersNeverDiscardExistingContent()
    {
        var document = Blank();
        var border = document.Insert("0/0", "Border");
        document.Insert(border, "TextBlock");
        var before = document.Source;
        Assert.Throws<InvalidOperationException>(() => document.Insert(border, "Button"));
        Assert.Equal(before, document.Source);
    }

    [Fact]
    public void ReparentRejectsCyclesAndCanMoveIntoNestedPanel()
    {
        var document = Blank();
        var panel = document.Insert("0/0", "StackPanel");
        var child = document.Insert(panel, "Border");
        Assert.Throws<InvalidOperationException>(() => document.Reparent(panel, child));
        var button = document.Insert("0/0", "Button");
        var moved = document.Reparent(button, panel);
        Assert.Equal("Button", document.Find(moved).Name.LocalName);
        Assert.Equal("StackPanel", document.Find(moved).Parent!.Name.LocalName);
    }

    [Fact]
    public void DuplicateCreatesUniqueNamesAndReorderIsUndoable()
    {
        var document = Blank();
        var original = document.Insert("0/0", "Button");
        document.SetProperty(original, "Name", "Submit");
        var duplicate = document.Duplicate(original);
        Assert.Equal("SubmitCopy", (string?)document.Find(duplicate).Attribute("Name"));
        var reordered = document.Reorder(duplicate, -1);
        Assert.Equal(original, reordered);
        Assert.Equal("SubmitCopy", (string?)document.Find(reordered).Attribute("Name"));
        document.Delete(reordered);
        document.Undo();
        Assert.Equal(2, document.Xml.Descendants(DesignDocument.Ui + "Button").Count());
    }

    [Fact]
    public void ExplicitContentPropertiesAcceptChildrenInTheirExistingSlot()
    {
        var document = new DesignDocument("""
            <UserControl xmlns="https://github.com/avaloniaui"><UserControl.Content><StackPanel><StackPanel.Children /></StackPanel></UserControl.Content></UserControl>
            """, catalog);
        var path = document.Insert("0/0/0", "Button");
        Assert.Equal("StackPanel.Children", document.Find(path).Parent!.Name.LocalName);
        Assert.Equal(3, document.Outline().Count());
    }

    [Fact]
    public void RejectsDtdAndDuplicateNames()
    {
        Assert.Throws<XmlException>(() => DesignDocument.Parse("<!DOCTYPE a [<!ENTITY a 'bad'>]><a>&a;</a>"));
        var document = Blank();
        var first = document.Insert("0/0", "Button");
        var second = document.Insert("0/0", "TextBox");
        document.SetProperty(first, "Name", "Shared");
        Assert.Throws<InvalidOperationException>(() => document.SetProperty(second, "Name", "Shared"));
    }

    [Fact]
    public void ClassStylesPreserveSelectionAndReplaceLocalValues()
    {
        var document = Blank();
        var button = document.Insert("0/0", "Button");
        document.SetProperty(button, "Background", "Red");
        var styled = document.SetClassStyle(button, "primaryAction", "Background", "Blue");
        Assert.Equal("Button", document.Find(styled).Name.LocalName);
        Assert.Null(document.Find(styled).Attribute("Background"));
        Assert.Equal("primaryAction", (string?)document.Find(styled).Attribute("Classes"));
        Assert.Contains("Selector=\"Button.primaryAction\"", document.Source);
        document.SetClassStyle(styled, "primaryAction", "Background", "Green");
        Assert.Single(document.Xml.Descendants(DesignDocument.Ui + "Setter"));
        Assert.Equal("Green", (string?)document.Xml.Descendants(DesignDocument.Ui + "Setter").Single().Attribute("Value"));
        document.Undo();
        Assert.Contains("Value=\"Blue\"", document.Source);
    }

    [Fact]
    public async Task AtomicSaveRoundTripsWithoutLeavingTemporaryFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "avalonia-studio-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "View.axaml");
            await DocumentFiles.SaveAtomic(path, StarterDocument.Blank);
            await DocumentFiles.SaveAtomic(path, StarterDocument.Source);
            Assert.Equal(StarterDocument.Source, await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(folder));
        }
        finally { Directory.Delete(folder, true); }
    }
}
