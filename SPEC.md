# Avalonia Studio

A standalone, cross-platform visual editor for Avalonia AXAML, implemented in readable C# with Avalonia 11.3.22 and .NET 10. The shell and preview use real Avalonia controls. No web imitation, database, or separate server.

## Capabilities and build order

1. Document: XML-preserving edits, undo/redo, duplication, deletion, hierarchy and safe file saving.
2. Preview: runtime AXAML rendering, selection mapping, theme/viewport preview and actionable errors.
3. Editing: searchable reflected control catalog, drag/drop insertion and movement, resize, property inspector and AXAML/style editing.
4. Verification: document tests, headless rendering/interaction checks and native Linux launch.

## Acceptance

- Drag controls from the toolbox into containers; select from the canvas or outline.
- Move and resize children on a Canvas; nest/reorder through the outline; preserve native Grid/StackPanel layout semantics.
- Discover public, concrete built-in controls with default constructors, including DataGrid. Expose inherited scalar properties, enums, booleans, brushes, dimensions and attached layout properties.
- Keep complex resources, templates, bindings and styles editable in AXAML, preserving them during visual edits.
- Preview Fluent/Simple and light/dark themes, viewport size and zoom; enable interaction mode.
- Open/save regular AXAML, export directly usable markup, recover failed edits without losing the last valid document, and confirm unsaved changes before replacement/close.
- Keyboard shortcuts and undo/redo cover document operations. Invalid operations report errors without mutating the document.

## Boundaries

This first version targets standalone views and built-in controls. It does not claim automatic loading of arbitrary application assemblies, view models, code-behind events, third-party packages, or project resource graphs. Window chrome is not embedded. Native dialogs/popups and platform-only controls can require a live desktop. Runtime XAML is trusted executable input, not a sandbox.

## Structure and style

`src/AvaloniaDesigner`: application, document model, catalog, preview and focused partial window files.
`tests/AvaloniaDesigner.Tests`: document and Avalonia headless tests.
`samples`: real editable AXAML examples.

Use explicit names, small methods, standard .NET XML and file APIs, and direct event handlers for editor orchestration. No general-purpose framework or speculative extension system.

## Verification commands

```sh
dotnet build AvaloniaDesigner.slnx
dotnet test AvaloniaDesigner.slnx
dotnet run --project src/AvaloniaDesigner
```

Always preserve unknown XML and report unsupported rendering. Ask before destructive changes. Never silently overwrite a dirty document or discard invalid source text. Windows/macOS runtime validation must be reported separately from Linux validation.
