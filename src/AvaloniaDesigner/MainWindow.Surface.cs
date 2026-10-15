using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AvaloniaDesigner;

public sealed partial class MainWindow
{
    private Point? gestureStart;
    private Control? gestureControl;
    private IPointer? gesturePointer;
    private bool resizing;
    private bool moving;
    private double startLeft;
    private double startTop;
    private Size startSize;
    private Rect? selectedBounds;

    private void ConfigureSurface()
    {
        InputLayer.AddHandler(RequestBringIntoViewEvent, (_, args) => args.Handled = true);
        InputLayer.PointerPressed += BeginGesture;
        InputLayer.PointerMoved += UpdateGesture;
        InputLayer.PointerReleased += EndGesture;
        InputLayer.PointerCaptureLost += (_, _) =>
        {
            gesturePointer = null;
            if (gestureStart == null) return;
            gestureStart = null;
            gestureControl = null;
            Refresh();
        };
        InputLayer.KeyDown += (_, args) =>
        {
            if (args.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
            var element = document.Find(selectedPath);
            if (element.Ancestors().FirstOrDefault(ancestor => catalog.Resolve(ancestor) != null)?.Name.LocalName != "Canvas") return;
            var distance = args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 8 : 1;
            var left = DesignDocument.Number(element, "Canvas.Left", 0) + (args.Key == Key.Left ? -distance : args.Key == Key.Right ? distance : 0);
            var top = DesignDocument.Number(element, "Canvas.Top", 0) + (args.Key == Key.Up ? -distance : args.Key == Key.Down ? distance : 0);
            args.Handled = true;
            RunEdit(() => document.Edit(xml => DesignDocument.SetPosition(DesignDocument.Find(xml, selectedPath), left, top)));
        };
        DragDrop.SetAllowDrop(InputLayer, true);
        InputLayer.AddHandler(DragDrop.DragOverEvent, (_, args) =>
        {
            var text = args.DataTransfer.TryGetText();
            args.DragEffects = text?.StartsWith("studio-control:") == true && !sourceDirty ? DragDropEffects.Copy : DragDropEffects.None;
            args.Handled = true;
        });
        InputLayer.AddHandler(DragDrop.DropEvent, (_, args) =>
        {
            var text = args.DataTransfer.TryGetText();
            if (text?.StartsWith("studio-control:") != true) return;
            var position = args.GetPosition(PreviewHost);
            var hit = HitControl(position);
            RunEdit(() =>
            {
                var parentPath = ContainerFor(hit ?? selectedPath);
                var relative = position;
                if (renderedControls.TryGetValue(parentPath, out var parent)) relative = args.GetPosition(parent);
                selectedPath = document.Insert(parentPath, text[15..], Snap(relative.X), Snap(relative.Y));
            }, "Control added");
            args.Handled = true;
        });
    }

    private void ConfigureToolboxDrag()
    {
        Point? pressed = null;
        ControlDescriptor? descriptor = null;
        ToolList.AddHandler(PointerPressedEvent, (_, args) =>
        {
            if (!args.GetCurrentPoint(ToolList).Properties.IsLeftButtonPressed) return;
            pressed = args.GetPosition(ToolList);
            descriptor = ItemAt<ControlDescriptor>(args.Source);
        }, RoutingStrategies.Tunnel);
        ToolList.PointerReleased += (_, _) => { pressed = null; descriptor = null; };
        ToolList.PointerMoved += async (_, args) =>
        {
            if (pressed == null || descriptor == null || !args.GetCurrentPoint(ToolList).Properties.IsLeftButtonPressed
                || Distance(args.GetPosition(ToolList), pressed.Value) < 5) return;
            var name = descriptor.Name;
            pressed = null;
            descriptor = null;
            using var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText("studio-control:" + name));
            try { await DragDrop.DoDragDropAsync(args, data, DragDropEffects.Copy); }
            catch (Exception error) { ShowError(error); }
        };
    }

    private void ConfigureOutlineDrag()
    {
        Point? pressed = null;
        string? sourcePath = null;
        OutlineList.AddHandler(PointerPressedEvent, (_, args) =>
        {
            if (!args.GetCurrentPoint(OutlineList).Properties.IsLeftButtonPressed) return;
            pressed = args.GetPosition(OutlineList);
            sourcePath = ItemAt<OutlineEntry>(args.Source)?.Path;
        }, RoutingStrategies.Tunnel);
        OutlineList.PointerReleased += (_, _) => { pressed = null; sourcePath = null; };
        OutlineList.PointerMoved += async (_, args) =>
        {
            if (pressed == null || sourcePath == null || sourcePath == "0" || sourceDirty
                || !args.GetCurrentPoint(OutlineList).Properties.IsLeftButtonPressed
                || Distance(args.GetPosition(OutlineList), pressed.Value) < 5) return;
            var path = sourcePath;
            pressed = null;
            sourcePath = null;
            using var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText("studio-node:" + path));
            try { await DragDrop.DoDragDropAsync(args, data, DragDropEffects.Move); }
            catch (Exception error) { ShowError(error); }
        };
        DragDrop.SetAllowDrop(OutlineList, true);
        OutlineList.AddHandler(DragDrop.DragOverEvent, (_, args) =>
        {
            var text = args.DataTransfer.TryGetText();
            var target = ItemAt<OutlineEntry>(args.Source);
            var valid = target != null && catalog.IsContainer(document.Find(target.Path)) && !sourceDirty;
            args.DragEffects = valid && text?.StartsWith("studio-node:") == true ? DragDropEffects.Move
                : valid && text?.StartsWith("studio-control:") == true ? DragDropEffects.Copy : DragDropEffects.None;
            args.Handled = true;
        });
        OutlineList.AddHandler(DragDrop.DropEvent, (_, args) =>
        {
            var text = args.DataTransfer.TryGetText();
            var target = ItemAt<OutlineEntry>(args.Source);
            if (target == null) return;
            if (text?.StartsWith("studio-node:") == true)
                RunEdit(() => selectedPath = document.Reparent(text[12..], target.Path), "Control moved into " + target.Title);
            else if (text?.StartsWith("studio-control:") == true)
                RunEdit(() => selectedPath = document.Insert(target.Path, text[15..]), "Control added");
            args.Handled = true;
        });
    }

    private static T? ItemAt<T>(object? source) where T : class => (source as Visual)?.GetSelfAndVisualAncestors()
        .OfType<ListBoxItem>().FirstOrDefault()?.DataContext as T;

    private string? HitControl(Point point)
    {
        return Hit(PreviewHost);

        string? Hit(Visual visual)
        {
            if (!visual.IsVisible || PreviewHost.TranslatePoint(point, visual) is not { } local) return null;
            var contains = new Rect(visual.Bounds.Size).Contains(local);
            if (visual.ClipToBounds && !contains) return null;
            foreach (var child in visual.GetVisualChildren().Reverse().OrderByDescending(child => child.ZIndex))
                if (Hit(child) is { } path) return path;
            return contains && visual is Control control ? Design.GetPath(control) : null;
        }
    }

    private void BeginGesture(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(InputLayer).Properties.IsLeftButtonPressed || sourceDirty) return;
        var point = args.GetPosition(Artboard);
        resizing = selectedBounds is { } bounds && new Rect(bounds.Right - 8, bounds.Bottom - 8, 18, 18).Contains(point);
        if (!resizing)
        {
            var path = HitControl(args.GetPosition(PreviewHost));
            if (path == null) return;
            SelectControl(path);
        }
        InputLayer.Focus();
        if (!renderedControls.TryGetValue(selectedPath, out gestureControl)) return;
        var element = document.Find(selectedPath);
        moving = element.Ancestors().FirstOrDefault(ancestor => catalog.Resolve(ancestor) != null)?.Name.LocalName == "Canvas";
        if (!resizing && !moving)
        {
            StatusLabel.Text = "Selected " + element.Name.LocalName + " · layout is controlled by its parent; edit properties or drag in the outline";
            return;
        }
        gestureStart = point;
        startLeft = Canvas.GetLeft(gestureControl);
        startTop = Canvas.GetTop(gestureControl);
        if (!double.IsFinite(startLeft)) startLeft = gestureControl.Bounds.X;
        if (!double.IsFinite(startTop)) startTop = gestureControl.Bounds.Y;
        startSize = gestureControl.Bounds.Size;
        gesturePointer = args.Pointer;
        args.Pointer.Capture(InputLayer);
        args.Handled = true;
    }

    private void UpdateGesture(object? sender, PointerEventArgs args)
    {
        if (gestureStart == null || gestureControl == null) return;
        var delta = args.GetPosition(Artboard) - gestureStart.Value;
        if (resizing)
        {
            gestureControl.Width = Math.Max(8, Snap(startSize.Width + delta.X));
            gestureControl.Height = Math.Max(8, Snap(startSize.Height + delta.Y));
        }
        else
        {
            Canvas.SetLeft(gestureControl, Snap(startLeft + delta.X));
            Canvas.SetTop(gestureControl, Snap(startTop + delta.Y));
        }
        UpdateSelectionBounds();
        args.Handled = true;
    }

    private void EndGesture(object? sender, PointerReleasedEventArgs args)
    {
        if (gestureStart == null || gestureControl == null) return;
        var delta = args.GetPosition(Artboard) - gestureStart.Value;
        gestureStart = null;
        gestureControl = null;
        gesturePointer = null;
        args.Pointer.Capture(null);
        if (Distance(delta, default) < 2) { Refresh(); return; }
        var success = RunEdit(() => document.Edit(xml =>
        {
            var element = DesignDocument.Find(xml, selectedPath);
            if (resizing)
            {
                element.SetAttributeValue("Width", Math.Max(8, Snap(startSize.Width + delta.X)).ToString(CultureInfo.InvariantCulture));
                element.SetAttributeValue("Height", Math.Max(8, Snap(startSize.Height + delta.Y)).ToString(CultureInfo.InvariantCulture));
            }
            else DesignDocument.SetPosition(element, Snap(startLeft + delta.X), Snap(startTop + delta.Y));
        }), resizing ? "Control resized" : "Control moved");
        if (!success)
        {
            var error = ErrorLabel.Text;
            Refresh();
            ShowError(error ?? "Gesture could not be applied.");
        }
        args.Handled = true;
    }

    private double Snap(double value) => SnapCheck.IsChecked == true ? Math.Round(value / 8) * 8 : Math.Round(value, 1);

    private static double Distance(Point first, Point second) => Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));

    private void UpdateSelectionBounds()
    {
        if (!renderedControls.TryGetValue(selectedPath, out var control) || !control.IsEffectivelyVisible
            || control.TranslatePoint(default, Artboard) is not { } origin)
        {
            SelectionBorder.IsVisible = ResizeHandle.IsVisible = false;
            selectedBounds = null;
            return;
        }
        selectedBounds = new Rect(origin, control.Bounds.Size);
        Canvas.SetLeft(SelectionBorder, origin.X);
        Canvas.SetTop(SelectionBorder, origin.Y);
        SelectionBorder.Width = Math.Max(0, control.Bounds.Width);
        SelectionBorder.Height = Math.Max(0, control.Bounds.Height);
        Canvas.SetLeft(ResizeHandle, origin.X + control.Bounds.Width - 5);
        Canvas.SetTop(ResizeHandle, origin.Y + control.Bounds.Height - 5);
        SelectionBorder.IsVisible = true;
        ResizeHandle.IsVisible = selectedPath != "0";
    }
}
