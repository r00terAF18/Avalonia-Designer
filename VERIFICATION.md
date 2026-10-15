# Verification — October 15, 2026

## Passed

- `dotnet build AvaloniaDesigner.slnx --no-restore -m:1`: clean build, zero warnings and errors.
- `dotnet test AvaloniaDesigner.slnx --no-restore --logger 'trx;LogFileName=verification.trx' --results-directory artifacts/test-results`: **22 passed, 0 failed, 0 skipped**.
- The catalog rendering test loads and lays out **129 of 129 insertable controls**. Two additional controls remain explicitly unavailable for standalone insertion, with reasons in the toolbox.
- Skia screenshot inspected at 1500×940; retained at `artifacts/studio.png`.
- Headless native-input tests exercise toolbox drop routing, snapping, transparent-container selection, dragging, resizing, Escape cancellation and undo. Other tests cover style rendering, theme switching, source-draft preservation, XML transactions, hierarchy edits, atomic saves and both shipped samples.
- `dotnet publish src/AvaloniaDesigner/AvaloniaDesigner.csproj -c Release --no-restore -o artifacts/app`: published successfully.
- Native X11 launch under Xvfb: both Debug and the published Release app remained running without startup errors. `timeout` intentionally ended each smoke run (exit 124).

## Not verified / not implemented

- Windows and macOS runtime behavior, native file-picker dialogs, accessibility tools, and real desktop OS drag negotiation across applications have not been exercised.
- Large-document performance and long-running memory behavior have not been benchmarked. Runtime XAML compilation happens on accepted edits, not on every pointer-move event.
- Arbitrary project/custom-assembly loading, application data contexts, multi-selection, clipboard editing, visual binding/template editors and crash recovery are not implemented.
- `DataGridRowGroupHeader` requires a DataGrid owner. `ExperimentalAcrylicBorder` requires platform composition the embedded preview cannot reliably provide.

The app is a usable first version of a standalone AXAML designer. These checks do not establish full WinForms-designer parity or support for every arbitrary Avalonia application.
