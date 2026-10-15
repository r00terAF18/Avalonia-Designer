using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;
using Xunit.Abstractions;
using Avalonia.Media;

[assembly: AvaloniaTestApplication(typeof(AvaloniaDesigner.Tests.TestApp))]

namespace AvaloniaDesigner.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class PreviewTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public void StarterRendersActualControlsAndDoesNotLeakDesignerMetadata()
    {
        var catalog = new ControlCatalog();
        var document = new DesignDocument(StarterDocument.Source, catalog);
        var renderer = new PreviewRenderer(catalog);
        var preview = renderer.Render(document.Xml);
        Assert.IsType<UserControl>(preview.Root);
        Assert.Equal(document.Outline().Count(), preview.Controls.Count);
        Assert.Contains(preview.Controls.Values, control => control is Button button && Equals(button.Content, "Get started"));
        Assert.DoesNotContain("studio:", document.Source);
    }

    [AvaloniaFact]
    public void InvalidXamlPropertyIsRejectedBeforeDocumentChanges()
    {
        var catalog = new ControlCatalog();
        var renderer = new PreviewRenderer(catalog);
        var document = new DesignDocument(StarterDocument.Blank, catalog);
        document.Validate = xml => renderer.Render(xml);
        var path = document.Insert("0/0", "Button");
        var before = document.Source;
        Assert.ThrowsAny<Exception>(() => document.SetProperty(path, "Width", "banana"));
        Assert.Equal(before, document.Source);
    }

    [AvaloniaFact]
    public void WindowPreviewPreservesOriginalRootAndCodeBehindMetadata()
    {
        var document = new DesignDocument("""
            <Window xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="Example.MainWindow" Title="Example"><Button Content="Hello" /></Window>
            """, new ControlCatalog());
        var preview = new PreviewRenderer(new ControlCatalog()).Render(document.Xml);
        Assert.IsType<UserControl>(preview.Root);
        Assert.Contains("x:Class=\"Example.MainWindow\"", document.Source);
        Assert.Equal("Window", document.Xml.Root!.Name.LocalName);
    }

    [AvaloniaFact]
    public void EditorCanAddEditUndoSwitchThemesAndRenderFrame()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.False(window.FindControl<Border>("ErrorPanel")!.IsVisible, window.FindControl<TextBlock>("ErrorLabel")!.Text);
            if (Environment.GetEnvironmentVariable("STUDIO_SCREENSHOT") is { } screenshot)
                window.CaptureRenderedFrame()!.Save(screenshot);
            var tools = window.FindControl<ListBox>("ToolList")!;
            tools.SelectedItem = tools.ItemsSource!.Cast<ControlDescriptor>().Single(control => control.Name == "Button");
            Click(window, "AddButton");
            Assert.Contains("Content=\"Button\"", window.FindControl<TextBox>("SourceEditor")!.Text);
            Click(window, "UndoButton");
            Assert.DoesNotContain("Content=\"Button\"", window.FindControl<TextBox>("SourceEditor")!.Text);
            Click(window, "RedoButton");
            window.FindControl<ComboBox>("ThemePicker")!.SelectedIndex = 1;
            window.FindControl<ComboBox>("VariantPicker")!.SelectedIndex = 1;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(window.CaptureRenderedFrame());
            Click(window, "UndoButton");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void InvalidSourceDraftSurvivesAndBlocksVisualEdits()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var source = window.FindControl<TextBox>("SourceEditor")!;
            source.Text = "<invalid";
            Click(window, "ApplySourceButton");
            Assert.True(window.FindControl<Border>("ErrorPanel")!.IsVisible);
            Assert.Equal("<invalid", source.Text);
            Click(window, "DeleteButton");
            Assert.Equal("<invalid", source.Text);
            Click(window, "RevertSourceButton");
            Assert.Contains("Make something beautiful.", source.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CanvasPointerGesturesMoveResizeAndUndo()
    {
        var window = new MainWindow();
        window.Show();
        var source = window.FindControl<TextBox>("SourceEditor")!;
        var original = source.Text;
        try
        {
            source.Text = """
                <UserControl xmlns="https://github.com/avaloniaui"><Canvas><Button Canvas.Left="64" Canvas.Top="64" Width="120" Height="40" Content="Drag me" /></Canvas></UserControl>
                """;
            Click(window, "ApplySourceButton");
            window.UpdateLayout();
            var artboard = window.FindControl<Grid>("Artboard")!;
            var start = artboard.TranslatePoint(new Point(100, 80), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Point(24, 32));
            window.MouseUp(start + new Point(24, 32), MouseButton.Left);
            output.WriteLine(source.Text);
            Assert.Contains("Canvas.Left=\"88\"", source.Text);
            Assert.Contains("Canvas.Top=\"96\"", source.Text);
            window.UpdateLayout();
            var handle = artboard.TranslatePoint(new Point(208, 136), window)!.Value;
            window.MouseDown(handle, MouseButton.Left);
            window.MouseMove(handle + new Point(40, 24));
            window.MouseUp(handle + new Point(40, 24), MouseButton.Left);
            Assert.Contains("Width=\"160\"", source.Text);
            Assert.Contains("Height=\"64\"", source.Text);
            Click(window, "UndoButton");
            Assert.Contains("Width=\"120\"", source.Text);
        }
        finally
        {
            source.Text = original;
            Click(window, "ApplySourceButton");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CatalogControlsCanBeLoadedByTheRealXamlEngine()
    {
        var catalog = new ControlCatalog();
        var renderer = new PreviewRenderer(catalog);
        var failures = new List<string>();
        foreach (var descriptor in catalog.Controls.Where(control => control.UnavailableReason == null))
        {
            try
            {
                var document = new DesignDocument(StarterDocument.Blank, catalog);
                document.Insert("0/0", descriptor.Name);
                var preview = renderer.Render(document.Xml);
                var host = new Window { Content = preview.Root, Width = 800, Height = 600 };
                try { host.Show(); host.UpdateLayout(); }
                finally { host.Close(); }
            }
            catch (Exception error) { failures.Add(descriptor.Name + ": " + PreviewRenderer.ErrorMessage(error)); }
        }
        var insertable = catalog.Controls.Count(control => control.UnavailableReason == null);
        output.WriteLine($"Loaded {insertable - failures.Count}/{insertable} insertable catalog controls; {catalog.Controls.Count - insertable} explicitly require unsupported hosting.");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [AvaloniaTheory]
    [InlineData("ResponsiveForm.axaml")]
    [InlineData("BlankCanvas.axaml")]
    public void ShippedSamplesRender(string filename)
    {
        var catalog = new ControlCatalog();
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", filename));
        var preview = new PreviewRenderer(catalog).Render(DesignDocument.Parse(source));
        var window = new Window { Content = preview.Root, Width = 800, Height = 600 };
        try { window.Show(); window.UpdateLayout(); Assert.NotNull(window.CaptureRenderedFrame()); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ClassStyleChangesTheRenderedButton()
    {
        var catalog = new ControlCatalog();
        var document = new DesignDocument(StarterDocument.Blank, catalog);
        var path = document.Insert("0/0", "Button");
        path = document.SetClassStyle(path, "primaryAction", "Background", "#7864E8");
        var preview = new PreviewRenderer(catalog).Render(document.Xml);
        var window = new Window { Content = preview.Root, Width = 800, Height = 600 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var brush = Assert.IsAssignableFrom<ISolidColorBrush>(((Button)preview.Controls[path]).Background);
            Assert.Equal(Color.Parse("#7864E8"), brush.Color);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ToolboxDropInsertsAtSnappedPositionAndUndoRestoresDocument()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            window.UpdateLayout();
            var source = window.FindControl<TextBox>("SourceEditor")!;
            var before = source.Text;
            var target = window.FindControl<Border>("InputLayer")!;
            using var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText("studio-control:Button"));
            var position = new Point(702, 502);
            var screenPosition = target.TranslatePoint(position, window)!.Value;
            window.DragDrop(screenPosition, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
            window.DragDrop(screenPosition, RawDragEventType.DragOver, data, DragDropEffects.Copy);
            window.DragDrop(screenPosition, RawDragEventType.Drop, data, DragDropEffects.Copy);
            Assert.Contains("Canvas.Left=\"704\"", source.Text);
            Assert.Contains("Canvas.Top=\"504\"", source.Text);
            Assert.Contains("Content=\"Button\"", source.Text);
            Click(window, "UndoButton");
            Assert.Equal(before, source.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EscapeCancelsGestureAndReleasesPointerCapture()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            window.UpdateLayout();
            var source = window.FindControl<TextBox>("SourceEditor")!;
            var before = source.Text;
            var layer = window.FindControl<Border>("InputLayer")!;
            IPointer? pointer = null;
            layer.AddHandler(InputElement.PointerPressedEvent, (_, args) => pointer = args.Pointer, RoutingStrategies.Bubble, handledEventsToo: true);
            var position = layer.TranslatePoint(new Point(50, 100), window)!.Value;
            window.MouseDown(position, MouseButton.Left);
            Assert.Equal("Border", window.FindControl<TextBlock>("SelectionTitle")!.Text);
            window.MouseMove(position + new Point(32, 32));
            Assert.Same(layer, pointer!.Captured);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            Assert.Null(pointer.Captured);
            window.MouseUp(position + new Point(32, 32), MouseButton.Left);
            Assert.Equal(before, source.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void UserCanChangeViewportPresetsAndUpdateDimensions()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            window.UpdateLayout();
            var presetPicker = window.FindControl<ComboBox>("PreviewViewportPresetPicker")!;
            var artboard = window.FindControl<Grid>("Artboard")!;
            var widthBox = window.FindControl<TextBox>("ViewportWidthBox")!;
            var heightBox = window.FindControl<TextBox>("ViewportHeightBox")!;
            var sizeLabel = window.FindControl<TextBlock>("SizeLabel")!;

            // Default is 800x600
            Assert.Equal(800, artboard.Width);
            Assert.Equal(600, artboard.Height);
            Assert.Equal("800", widthBox.Text);
            Assert.Equal("600", heightBox.Text);
            Assert.Equal("800 × 600", sizeLabel.Text);

            // Select Phone preset (index 6: 390x844)
            presetPicker.SelectedIndex = 6;
            window.UpdateLayout();
            Assert.Equal(390, artboard.Width);
            Assert.Equal(844, artboard.Height);
            Assert.Equal("390", widthBox.Text);
            Assert.Equal("844", heightBox.Text);
            Assert.Equal("390 × 844", sizeLabel.Text);

            // Select Desktop HD preset (index 2: 1280x720)
            presetPicker.SelectedIndex = 2;
            window.UpdateLayout();
            Assert.Equal(1280, artboard.Width);
            Assert.Equal(720, artboard.Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void UserCanTypeCustomViewportDimensionsAndFlipOrientation()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            window.UpdateLayout();
            var artboard = window.FindControl<Grid>("Artboard")!;
            var widthBox = window.FindControl<TextBox>("ViewportWidthBox")!;
            var heightBox = window.FindControl<TextBox>("ViewportHeightBox")!;
            var presetPicker = window.FindControl<ComboBox>("PreviewViewportPresetPicker")!;

            // Custom dimensions: 950 x 550
            widthBox.Text = "950";
            widthBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            heightBox.Text = "550";
            heightBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

            window.UpdateLayout();
            Assert.Equal(950, artboard.Width);
            Assert.Equal(550, artboard.Height);
            Assert.Equal(presetPicker.ItemCount - 1, presetPicker.SelectedIndex); // Custom

            // Flip orientation: 550 x 950
            Click(window, "FlipViewportButton");
            window.UpdateLayout();
            Assert.Equal(550, artboard.Width);
            Assert.Equal(950, artboard.Height);
            Assert.Equal("550", widthBox.Text);
            Assert.Equal("950", heightBox.Text);

            // Reset viewport: 800 x 600
            Click(window, "ResetViewportButton");
            window.UpdateLayout();
            Assert.Equal(800, artboard.Width);
            Assert.Equal(600, artboard.Height);
            Assert.Equal(0, presetPicker.SelectedIndex);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void UserCanDragViewportCornerHandleToResize()
    {
        var window = new MainWindow();
        window.Width = 1600; window.Height = 1100;
        window.Show();
        try
        {
            window.UpdateLayout();
            var artboard = window.FindControl<Grid>("Artboard")!;
            var corner = window.FindControl<Border>("ViewportResizeCorner")!;
            var scroll = window.FindControl<ScrollViewer>("DesignScroll")!;

            Assert.Equal(800, artboard.Width);
            Assert.Equal(600, artboard.Height);

            var startPos = corner.TranslatePoint(new Point(8, 8), window);
                        if (startPos.HasValue)
            {
                                window.MouseDown(startPos.Value, MouseButton.Left);
                window.MouseMove(startPos.Value + new Point(80, 48));
                window.MouseUp(startPos.Value + new Point(80, 48), MouseButton.Left);
            }

            window.UpdateLayout();
                        Assert.Equal(880, artboard.Width);
            Assert.Equal(648, artboard.Height);
        }
        finally { window.Close(); }
    }

    private static void Click(Window window, string name) => window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
