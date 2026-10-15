# Avalonia Studio

A native, cross-platform visual layout editor for Avalonia. Built with C# and Avalonia, with actual Avalonia controls in the preview and ordinary `.axaml` files on disk.

This is a working first version, not a complete replacement for Visual Studio's mature WinForms designer. The supported workflow is editing standalone Avalonia views. Arbitrary application projects, custom assemblies and view models are not automatically loaded.

## Run

Install the .NET 10 SDK, then from this directory:

```sh
dotnet restore AvaloniaDesigner.slnx
dotnet run --project src/AvaloniaDesigner
```

Windows, macOS and Linux use the same project. Linux needs a graphical session and Avalonia's native desktop prerequisites. Linux startup and headless Skia rendering are verified here; Windows/macOS runtime behavior has not been tested.

## Design a view

1. Start with the welcome view, choose **New** for an empty canvas, or **Open** an AXAML file. Try `samples/ResponsiveForm.axaml` for native Grid/StackPanel layout.
2. Search the toolbox. Drag a control onto the preview or a container in the outline. Double-click a toolbox control or choose **Add to selection** for a keyboard-friendly alternative.
3. Select on the preview or in the outline. Drag direct Canvas children to position them; use the lower-right selection handle to resize. **Snap 8px** controls snapping. Arrow keys nudge Canvas children by one pixel; Shift+arrow moves eight.
4. Reparent controls by dragging outline rows onto containers. The arrow buttons change sibling order. Single-child containers reject a second child instead of discarding existing content. Use a Grid, StackPanel or other panel inside a Border/ScrollViewer for multiple children.
5. Edit properties in the inspector. Enter or leaving a field applies its value; **↺** removes the local value. Enums and booleans have pickers; brush and color fields have a color chooser. Filter by property name to quickly reach less-common properties.
6. Choose **Create style…** to assign a reusable class and setter. This removes that property's local value so the style takes effect. Add that class to other controls through **Classes**. Edit selectors, resources, templates and additional setters directly in AXAML.
7. Switch Fluent/Simple, Dark/Light, viewport and zoom without changing the document. **Interact** lets you use the preview's buttons, inputs and menus. Switch it off to resume selection.
8. **Save** writes standard AXAML. There is no proprietary project format or generated code-behind.

## Source and safety

The XML document owns the design. Visual edits preserve unrelated styles, resources, bindings and comments; formatting is normalized. Designer selection metadata is injected only into a preview clone and is never exported. Native Grid and StackPanel layout rules are respected rather than converted to absolute positions.

Source changes are an explicit draft: use **Apply AXAML** or Ctrl/Cmd+Enter to validate and render them. A failed edit reports its error and retains the previous working document. Visual edits are blocked while a source draft is unapplied, so the draft cannot be silently replaced. **Revert draft** explicitly discards it.

Undo/redo retains up to 100 document edits. Save uses a temporary sibling file followed by replacement. Saving checks for external changes to the current file; conflicts require Save as. New/Open/Close prompt for unsaved changes. History and draft buffers are in memory, not crash recovery.

Only open AXAML you trust. Avalonia's runtime XAML loader creates .NET objects; the preview is not a security sandbox. XML DTDs are disabled and document size is limited to 2 MB of characters.

## Control coverage and limitations

- The toolbox discovers public, concrete, default-constructible controls from Avalonia, DataGrid and ColorPicker. Inherited scalar properties and common attached layout properties are reflected into the inspector. New properties do not require hand-written editors.
- Specialized primitives appear in search too. `DataGridRowGroupHeader` needs its owning DataGrid; `ExperimentalAcrylicBorder` needs platform composition unavailable in the embedded preview. Both remain listed with explanations and reject standalone insertion. Use their owning control or edit templates in AXAML.
- Window roots are previewed as UserControls without window chrome. The original root, title and `x:Class` remain in the saved file. Unhandled Window-only properties may need removal from the preview input manually.
- Custom/third-party controls, project resource graphs, application-specific markup extensions, compiled bindings to external types and code-behind event handlers are not resolved automatically. Unsupported input stays available in the source editor and reports a rendering error. No project build or plugin system is implied.
- Complex objects, collection contents, templates, animations, data bindings and custom selectors are edited in AXAML. The property inspector is for scalar values and resource expressions; it is not a visual binding or template editor.
- Containers move/reorder in the outline. Free positioning applies to direct Canvas children; Grid cells, Dock position and StackPanel orientation are edited as layout properties. There is no multi-selection, alignment toolbar, copy/paste, or constraint designer yet.
- The preview resets control interaction state after document edits. Popup/native-platform behavior needs a live desktop. DataGrid starts empty until you supply items in AXAML or integrate application data yourself.
- Duplicate generates unique names, but does not rewrite bindings referring to those names; inspect such references after duplicating a named subtree.
- Theme and viewport controls affect preview only. Save the corresponding application theme configuration in your own app separately.
- Avalonia 11.3.22 is pinned deliberately; DataGrid uses its compatible 11.3.13 release. This is not an Avalonia 12 project.

## Shortcuts

| Action | Shortcut |
|---|---|
| New / open / save | Ctrl/Cmd+N / O / S |
| Save as | Ctrl/Cmd+Shift+S |
| Undo / redo | Ctrl/Cmd+Z / Shift+Z, or Ctrl/Cmd+Y |
| Duplicate selection | Ctrl/Cmd+D |
| Delete selection | Delete |
| Apply AXAML draft | Ctrl/Cmd+Enter |
| Move Canvas selection | Arrows / Shift+arrows |
| Cancel an active canvas gesture | Escape |

Text fields keep their own editing shortcuts. Undo document edits using the toolbar while the source editor has focus.

## Code map

| File | Responsibility |
|---|---|
| `DesignDocument.cs` | XML transactions, history, hierarchy, properties and class styles |
| `ControlCatalog.cs` | Control discovery, initial control values and editable-property metadata |
| `PreviewRenderer.cs` | Preview-only AXAML transformations and runtime loading |
| `MainWindow.axaml` | Editor layout and named controls |
| `MainWindow.axaml.cs` | Editor state, commands, themes and refresh |
| `MainWindow.Surface.cs` | Drag/drop, canvas gestures, selection and snapping |
| `MainWindow.Inspector.cs` | Property editors, color chooser and style dialog |
| `MainWindow.Files.cs` | File dialogs, atomic saves and unsaved-change confirmation |

The UI uses direct event handlers around a small document model. No MVVM toolkit, service container, database or web layer is required.

## Verify and publish

```sh
dotnet build AvaloniaDesigner.slnx
dotnet test AvaloniaDesigner.slnx
dotnet publish src/AvaloniaDesigner -c Release -o artifacts/app
```

`artifacts/app/AvaloniaDesigner` (or `.exe` on Windows) launches the published application on the publishing platform with the matching .NET runtime installed. For self-contained distribution, publish separately for the target runtime, for example `-r linux-x64 --self-contained true`. Do not enable trimming or Native AOT: runtime XAML compilation requires reflection and dynamic code.

Tests cover document preservation, transactional failures, single-child protection, hierarchy changes, history, atomic saves, runtime XAML loading, control discovery, theme switching, source draft protection and actual pointer movement/resizing through Avalonia's headless input system. Set `STUDIO_SCREENSHOT` to an absolute PNG path when running tests to capture the editor with Skia.
