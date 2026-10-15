using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Themes.Simple;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaDesigner;

public sealed partial class MainWindow : Window
{
    private readonly ControlCatalog catalog = new();
    private readonly PreviewRenderer renderer;
    private DesignDocument document;
    private Dictionary<string, Control> renderedControls = [];
    private string selectedPath = "0/1";
    private string? filePath;
    private string? savedDiskText;
    private bool updating;
    private bool updatingViewport;
    private bool sourceDirty;
    private bool allowClose;
    private bool confirmingClose;
    private double zoom = 1;
    private PreviewResult? validatedPreview;
    private PreviewResult? lastValidPreview;
    private string? copyPayload;

    // Debounce timer for auto-applying AXAML source edits.
    private readonly DispatcherTimer debounceTimer;

    // Zoom steps matching ZoomPicker indices: 50%, 75%, 100%, 125%, 150%
    private static readonly double[] ZoomSteps = [0.5, 0.75, 1.0, 1.25, 1.5];

    // Standard viewport size presets (index matches combobox items 0..8)
    public static readonly (string Name, double Width, double Height)[] ViewportPresets =
    [
        ("Desktop · 800×600", 800, 600),
        ("Laptop · 1366×768", 1366, 768),
        ("Desktop HD · 1280×720", 1280, 720),
        ("Desktop FHD · 1920×1080", 1920, 1080),
        ("Tablet Portrait · 768×1024", 768, 1024),
        ("Tablet Landscape · 1024×768", 1024, 768),
        ("Phone · 390×844", 390, 844),
        ("Mobile Large · 412×915", 412, 915),
        ("Dialog · 480×320", 480, 320),
    ];

    public MainWindow()
    {
        InitializeComponent();
        renderer = new PreviewRenderer(catalog);
        document = CreateDocument(StarterDocument.Source);
        ConfigureLists();
        ConfigureCommands();
        ConfigureSurface();
        ConfigureThemes();
        ConfigureViewport();

        debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        debounceTimer.Tick += (_, _) =>
        {
            debounceTimer.Stop();
            AutoApplySource();
        };

        SourceEditor.PropertyChanged += (_, args) =>
        {
            if (updating || args.Property != TextBox.TextProperty) return;
            var text = SourceEditor.Text ?? "";
            sourceDirty = text != document.Source;
            UpdateTitle();
            if (sourceDirty)
            {
                debounceTimer.Stop();
                debounceTimer.Start();
            }
            else
            {
                debounceTimer.Stop();
            }
        };

        PropertySearch.TextChanged += (_, _) => BuildInspector();
        LayoutUpdated += (_, _) => UpdateSelectionBounds();
        AddHandler(KeyDownEvent, HandleShortcuts, RoutingStrategies.Tunnel);

        // Ctrl+scroll wheel zoom on the design scroll viewer.
        DesignScroll.AddHandler(PointerWheelChangedEvent, OnDesignScrollWheel, RoutingStrategies.Tunnel);

        Closing += async (_, args) =>
        {
            if (allowClose || (!document.IsDirty && !sourceDirty)) return;
            args.Cancel = true;
            if (confirmingClose) return;
            confirmingClose = true;
            try
            {
                if (await ConfirmDiscard()) { allowClose = true; Close(); }
            }
            finally { confirmingClose = false; }
        };
        Refresh();
    }

    private DesignDocument CreateDocument(string source)
    {
        var result = new DesignDocument(source, catalog);
        result.Validate = xml => validatedPreview = renderer.Render(xml, BaseUri());
        return result;
    }

    private Uri? BaseUri() => filePath == null ? null : new Uri(filePath);

    private void ConfigureLists()
    {
        ToolList.ItemTemplate = new FuncDataTemplate<ControlDescriptor>((item, _) =>
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,Auto"), Margin = new Thickness(0, 3) };
            row.Children.Add(new TextBlock { Text = item?.Category == "Layout" ? "◦" : "◈", Foreground = Brush.Parse("#A797FF"), FontSize = 16 });
            var title = new TextBlock { Text = item?.Name, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontSize = 12 };
            Grid.SetColumn(title, 1);
            row.Children.Add(title);
            row.Opacity = item?.UnavailableReason == null ? 1 : 0.45;
            ToolTip.SetTip(row, item?.UnavailableReason ?? item?.Type.FullName);
            return row;
        });
        OutlineList.ItemTemplate = new FuncDataTemplate<OutlineEntry>((entry, _) => new TextBlock
        {
            Text = entry?.Title,
            Margin = new Thickness((entry?.Depth ?? 0) * 12, 5, 0, 5),
            FontSize = 12
        });
        ToolSearch.TextChanged += (_, _) => FilterToolbox();
        OutlineList.SelectionChanged += (_, _) =>
        {
            if (!updating && OutlineList.SelectedItem is OutlineEntry entry) SelectControl(entry.Path);
        };
        ToolList.DoubleTapped += (_, _) => AddSelectedControl();
        FilterToolbox();
        ConfigureToolboxDrag();
        ConfigureOutlineDrag();
    }

    private void FilterToolbox()
    {
        var search = ToolSearch.Text ?? "";
        ToolList.ItemsSource = catalog.Controls.Where(control => control.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || control.Category.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(control => CommonControlOrder(control.Name)).ThenBy(control => control.Name).ToArray();
    }

    private static int CommonControlOrder(string name) => name switch
    {
        "Button" => 0, "TextBlock" => 1, "TextBox" => 2, "StackPanel" => 3, "Grid" => 4,
        "Border" => 5, "Canvas" => 6, "CheckBox" => 7, "ComboBox" => 8, "Image" => 9, _ => 10
    };

    private void ConfigureCommands()
    {
        NewButton.Click += async (_, _) =>
        {
            if (!await ConfirmDiscard()) return;
            filePath = null;
            savedDiskText = null;
            document = CreateDocument(StarterDocument.Blank);
            selectedPath = "0/0";
            sourceDirty = false;
            debounceTimer.Stop();
            Refresh();
        };
        OpenButton.Click += async (_, _) => await OpenFile();
        RecentButton.Click += async (_, _) => await ShowRecentFiles();
        SaveButton.Click += async (_, _) => await SaveFile(false);
        SaveAsButton.Click += async (_, _) => await SaveFile(true);
        UndoButton.Click += (_, _) => RunEdit(() => document.Undo(), "Undo");
        RedoButton.Click += (_, _) => RunEdit(() => document.Redo(), "Redo");
        AddButton.Click += (_, _) => AddSelectedControl();
        DeleteButton.Click += (_, _) => DeleteSelection();
        DuplicateButton.Click += (_, _) => RunEdit(() => selectedPath = document.Duplicate(selectedPath), "Control duplicated");
        PasteButton.Click += (_, _) => PasteControl();
        UpButton.Click += (_, _) => RunEdit(() => selectedPath = document.Reorder(selectedPath, -1), "Control reordered");
        DownButton.Click += (_, _) => RunEdit(() => selectedPath = document.Reorder(selectedPath, 1), "Control reordered");
        ApplySourceButton.Click += (_, _) => ApplySource();
        StyleButton.Click += async (_, _) => await CreateStyle();
        RevertSourceButton.Click += (_, _) => { sourceDirty = false; debounceTimer.Stop(); Refresh(); };
        InteractButton.IsCheckedChanged += (_, _) =>
        {
            InputLayer.IsVisible = InteractButton.IsChecked != true;
            SelectionLayer.IsVisible = InputLayer.IsVisible;
            StatusLabel.Text = InputLayer.IsVisible ? "Design mode · drag to move, bottom-right handle to resize" : "Interaction mode · controls are live";
        };
        DismissErrorButton.Click += (_, _) => ErrorPanel.IsVisible = false;
    }

    private void ConfigureThemes()
    {
        ThemePicker.SelectionChanged += (_, _) => ApplyTheme();
        VariantPicker.SelectionChanged += (_, _) => ApplyTheme();
        ZoomPicker.SelectionChanged += (_, _) =>
        {
            zoom = ZoomPicker.SelectedIndex switch { 0 => 0.5, 1 => 0.75, 3 => 1.25, 4 => 1.5, _ => 1 };
            ZoomHost.LayoutTransform = new ScaleTransform(zoom, zoom);
        };
        ApplyTheme();
    }

    private void ConfigureViewport()
    {
        ViewportPicker.SelectionChanged += (_, _) =>
        {
            if (updatingViewport) return;
            updatingViewport = true;
            try
            {
                var index = ViewportPicker.SelectedIndex;
                if (index >= 0 && index < ViewportPresets.Length)
                {
                    var preset = ViewportPresets[index];
                    SetViewportSize(preset.Width, preset.Height, updateInputs: true, updatePresets: false);
                    PreviewViewportPresetPicker.SelectedIndex = index;
                }
                else if (index == ViewportPresets.Length)
                {
                    PreviewViewportPresetPicker.SelectedIndex = index;
                }
            }
            finally { updatingViewport = false; }
        };

        PreviewViewportPresetPicker.SelectionChanged += (_, _) =>
        {
            if (updatingViewport) return;
            updatingViewport = true;
            try
            {
                var index = PreviewViewportPresetPicker.SelectedIndex;
                if (index >= 0 && index < ViewportPresets.Length)
                {
                    var preset = ViewportPresets[index];
                    SetViewportSize(preset.Width, preset.Height, updateInputs: true, updatePresets: false);
                    ViewportPicker.SelectedIndex = index;
                }
                else if (index == ViewportPresets.Length)
                {
                    ViewportPicker.SelectedIndex = index;
                }
            }
            finally { updatingViewport = false; }
        };

        void ApplyCustomViewportFromInputs()
        {
            if (double.TryParse(ViewportWidthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                && double.TryParse(ViewportHeightBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
            {
                SetViewportSize(w, h, updateInputs: true, updatePresets: true);
            }
            else
            {
                ViewportWidthBox.Text = Artboard.Width.ToString(CultureInfo.InvariantCulture);
                ViewportHeightBox.Text = Artboard.Height.ToString(CultureInfo.InvariantCulture);
            }
        }

        ViewportWidthBox.LostFocus += (_, _) => ApplyCustomViewportFromInputs();
        ViewportWidthBox.KeyDown += (_, args) => { if (args.Key == Key.Enter) { args.Handled = true; ApplyCustomViewportFromInputs(); } };
        ViewportHeightBox.LostFocus += (_, _) => ApplyCustomViewportFromInputs();
        ViewportHeightBox.KeyDown += (_, args) => { if (args.Key == Key.Enter) { args.Handled = true; ApplyCustomViewportFromInputs(); } };

        FlipViewportButton.Click += (_, _) => SetViewportSize(Artboard.Height, Artboard.Width, updateInputs: true, updatePresets: true);
        ResetViewportButton.Click += (_, _) => SetViewportSize(800, 600, updateInputs: true, updatePresets: true);
        FitZoomButton.Click += (_, _) => ZoomPicker.SelectedIndex = 2; // 100%
    }

    public void SetViewportSize(double width, double height, bool updateInputs = true, bool updatePresets = true)
    {
        width = Math.Clamp(Math.Round(width), 200, 3840);
        height = Math.Clamp(Math.Round(height), 200, 3840);

        Artboard.Width = width;
        Artboard.Height = height;
        var label = $"{width} × {height}";
        SizeLabel.Text = label;
        StatusViewportBadge.Text = label + " px";

        if (updateInputs)
        {
            ViewportWidthBox.Text = width.ToString(CultureInfo.InvariantCulture);
            ViewportHeightBox.Text = height.ToString(CultureInfo.InvariantCulture);
        }

        if (updatePresets)
        {
            var matchIndex = Array.FindIndex(ViewportPresets, p => Math.Abs(p.Width - width) < 0.5 && Math.Abs(p.Height - height) < 0.5);
            var targetIndex = matchIndex >= 0 ? matchIndex : ViewportPresets.Length;

            if (ViewportPicker.SelectedIndex != targetIndex)
                ViewportPicker.SelectedIndex = targetIndex;
            if (PreviewViewportPresetPicker.SelectedIndex != targetIndex)
                PreviewViewportPresetPicker.SelectedIndex = targetIndex;
        }

        UpdateSelectionBounds();
    }

    private void ApplyTheme()
    {
        PreviewHost.Styles.Clear();
        PreviewHost.Styles.Add(ThemePicker.SelectedIndex == 1 ? new SimpleTheme() : new FluentTheme());
        var theme = ThemePicker.SelectedIndex == 1 ? "Simple" : "Fluent";
        PreviewHost.Styles.Add(new StyleInclude(new Uri("avares://AvaloniaDesigner/"))
        {
            Source = new Uri($"avares://Avalonia.Controls.DataGrid/Themes/{theme}.xaml")
        });
        PreviewHost.Styles.Add(new StyleInclude(new Uri("avares://AvaloniaDesigner/"))
        {
            Source = new Uri($"avares://Avalonia.Controls.ColorPicker/Themes/{theme}/{theme}.xaml")
        });
        PreviewTheme.RequestedThemeVariant = VariantPicker.SelectedIndex == 1 ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    private void Refresh()
    {
        updating = true;
        try
        {
            var entries = document.Outline().ToArray();
            if (entries.All(entry => entry.Path != selectedPath)) selectedPath = entries.FirstOrDefault()?.Path ?? "0";
            OutlineList.ItemsSource = entries;
            OutlineList.SelectedItem = entries.FirstOrDefault(entry => entry.Path == selectedPath);
            if (!sourceDirty) SourceEditor.Text = document.Source;
            var preview = validatedPreview ?? renderer.Render(document.Xml, BaseUri());
            validatedPreview = null;
            lastValidPreview = preview;
            PreviewHost.Child = preview.Root;
            renderedControls = preview.Controls.ToDictionary(pair => pair.Key, pair => pair.Value);
            InputLayer.IsEnabled = true;
            ErrorPanel.IsVisible = false;
            StatusLabel.Text = $"{catalog.Controls.Count} controls available · {entries.Length} elements · AXAML is the source of truth";
        }
        catch (Exception error)
        {
            // Keep the last valid preview visible instead of blanking the artboard.
            if (lastValidPreview != null)
            {
                PreviewHost.Child = lastValidPreview.Root;
                renderedControls = lastValidPreview.Controls.ToDictionary(pair => pair.Key, pair => pair.Value);
            }
            else
            {
                PreviewHost.Child = null;
                renderedControls.Clear();
            }
            InputLayer.IsEnabled = false;
            ShowError(error);
            // Annotate the error panel to indicate the preview is stale.
            if (lastValidPreview != null)
                ErrorLabel.Text = ErrorLabel.Text + "\n(Preview shows last valid state)";
        }
        finally { updating = false; }
        BuildInspector();
        UpdateTitle();
        Dispatcher.UIThread.Post(UpdateSelectionBounds, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Silently attempts to apply the current source editor text.
    /// On success: commits the source, clears sourceDirty, refreshes.
    /// On failure: shows the error panel but leaves the editor text and sourceDirty unchanged.
    /// </summary>
    private void AutoApplySource()
    {
        var text = SourceEditor.Text ?? "";
        if (text == document.Source)
        {
            sourceDirty = false;
            UpdateTitle();
            return;
        }
        try
        {
            document.Replace(text);
            sourceDirty = false;
            Refresh();
        }
        catch (Exception error)
        {
            ShowError(error);
            if (lastValidPreview != null)
                ErrorLabel.Text = ErrorLabel.Text + "\n(Preview shows last valid state)";
        }
    }

    private void UpdateTitle()
    {
        var name = filePath == null ? "Untitled.axaml" : Path.GetFileName(filePath);
        var dirty = document.IsDirty || sourceDirty ? " •" : "";
        FileLabel.Text = name + dirty;
        Title = name + dirty + " — Avalonia Studio";
        SourceLabel.Text = sourceDirty ? "AXAML SOURCE · UNAPPLIED DRAFT" : "AXAML SOURCE";
        UndoButton.IsEnabled = document.CanUndo && !sourceDirty;
        RedoButton.IsEnabled = document.CanRedo && !sourceDirty;
    }

    private void SelectControl(string path)
    {
        selectedPath = path;
        updating = true;
        OutlineList.SelectedItem = document.Outline().FirstOrDefault(entry => entry.Path == path);
        updating = false;
        BuildInspector();
        UpdateSelectionBounds();
    }

    private bool RunEdit(Action action, string? status = null)
    {
        if (sourceDirty) { ShowError("Apply or revert the AXAML draft before editing visually."); return false; }
        try
        {
            action();
            Refresh();
            if (status != null) StatusLabel.Text = status;
            return true;
        }
        catch (Exception error) { ShowError(error); return false; }
    }

    private bool ApplySource()
    {
        try
        {
            document.Replace(SourceEditor.Text ?? "");
            sourceDirty = false;
            debounceTimer.Stop();
            Refresh();
            return true;
        }
        catch (Exception error) { ShowError(error); return false; }
    }

    private string ContainerFor(string path)
    {
        var element = document.Find(path);
        var container = element.AncestorsAndSelf().FirstOrDefault(catalog.IsContainer)
            ?? throw new InvalidOperationException("Select a container in the outline.");
        return DesignDocument.PathOf(container);
    }

    private void AddSelectedControl()
    {
        if (ToolList.SelectedItem is not ControlDescriptor control) { ShowError("Choose a control in the toolbox first."); return; }
        RunEdit(() => selectedPath = document.Insert(ContainerFor(selectedPath), control.Name), $"Added {control.Name}");
    }

    private void CopyControl()
    {
        try
        {
            var element = document.Find(selectedPath);
            copyPayload = element.ToString(SaveOptions.None);
            StatusLabel.Text = $"Copied {element.Name.LocalName}";
        }
        catch (Exception error) { ShowError(error); }
    }

    private void PasteControl()
    {
        if (copyPayload == null) { ShowError("Nothing to paste. Copy a control first."); return; }
        RunEdit(() =>
        {
            var copy = XElement.Parse(copyPayload);
            // Deduplicate x:Name values the same way Duplicate does.
            var names = document.Xml.Descendants().Attributes()
                .Where(a => a.Name == "Name" || a.Name == DesignDocument.Xaml + "Name")
                .Select(a => a.Value).ToHashSet();
            foreach (var attr in copy.DescendantsAndSelf().Attributes()
                .Where(a => a.Name == "Name" || a.Name == DesignDocument.Xaml + "Name").ToList())
            {
                var name = attr.Value + "Copy";
                while (!names.Add(name)) name += "Copy";
                attr.Value = name;
            }
            document.Edit(xml =>
            {
                if (selectedPath == "0")
                {
                    // Root selected — insert as child of root container.
                    var root = xml.Root!;
                    var deepCopy = new XElement(copy);
                    root.Add(deepCopy);
                    selectedPath = DesignDocument.PathOf(deepCopy);
                }
                else
                {
                    var current = DesignDocument.Find(xml, selectedPath);
                    var deepCopy = new XElement(copy);
                    current.AddAfterSelf(deepCopy);
                    selectedPath = DesignDocument.PathOf(deepCopy);
                }
            });
        }, "Pasted");
    }

    private void DeleteSelection() => RunEdit(() => document.Delete(selectedPath), "Control deleted");
    private void ShowError(Exception error) => ShowError(PreviewRenderer.ErrorMessage(error));
    private void ShowError(string message) { ErrorLabel.Text = message; ErrorPanel.IsVisible = true; StatusLabel.Text = "Edit not applied · see details above the source editor"; }

    private void OnDesignScrollWheel(object? sender, PointerWheelEventArgs args)
    {
        if (!args.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        args.Handled = true;

        var delta = args.Delta.Y;
        if (delta == 0) return;

        var currentIndex = ZoomPicker.SelectedIndex;
        var nextIndex = delta > 0 ? currentIndex + 1 : currentIndex - 1;
        nextIndex = Math.Clamp(nextIndex, 0, ZoomSteps.Length - 1);
        if (nextIndex == currentIndex) return;

        ZoomPicker.SelectedIndex = nextIndex;
    }

    private async void HandleShortcuts(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape && gestureStart != null)
        {
            gestureStart = null;
            gestureControl = null;
            gesturePointer?.Capture(null);
            gesturePointer = null;
            Refresh();
            args.Handled = true;
            return;
        }
        var command = args.KeyModifiers.HasFlag(KeyModifiers.Control) || args.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (command)
        {
            switch (args.Key)
            {
                case Key.S: args.Handled = true; await SaveFile(args.KeyModifiers.HasFlag(KeyModifiers.Shift)); return;
                case Key.O: args.Handled = true; await OpenFile(); return;
                case Key.Enter: args.Handled = true; ApplySource(); return;
                case Key.N: args.Handled = true; NewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return;
                case Key.D0:
                case Key.NumPad0:
                    args.Handled = true;
                    ZoomPicker.SelectedIndex = 2; // 100%
                    return;
                case Key.OemPlus:
                case Key.Add:
                    args.Handled = true;
                    ZoomPicker.SelectedIndex = Math.Min(ZoomPicker.SelectedIndex + 1, ZoomSteps.Length - 1);
                    return;
                case Key.OemMinus:
                case Key.Subtract:
                    args.Handled = true;
                    ZoomPicker.SelectedIndex = Math.Max(ZoomPicker.SelectedIndex - 1, 0);
                    return;
            }
        }
        if (args.Source is Visual visual && visual.GetSelfAndVisualAncestors().Any(parent => parent is TextBox)) return;
        if (command && args.Key == Key.Z)
        {
            args.Handled = true;
            RunEdit(() => { if (args.KeyModifiers.HasFlag(KeyModifiers.Shift)) document.Redo(); else document.Undo(); });
        }
        else if (command && args.Key == Key.Y) { args.Handled = true; RunEdit(document.Redo); }
        else if (command && args.Key == Key.D) { args.Handled = true; RunEdit(() => selectedPath = document.Duplicate(selectedPath)); }
        else if (command && args.Key == Key.C) { args.Handled = true; CopyControl(); }
        else if (command && args.Key == Key.V) { args.Handled = true; PasteControl(); }
        else if (args.Key == Key.Delete && InteractButton.IsChecked != true) { args.Handled = true; DeleteSelection(); }
    }
}
