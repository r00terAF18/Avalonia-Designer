using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace AvaloniaDesigner;

public sealed partial class MainWindow
{
    private static readonly FilePickerFileType AxamlType = new("Avalonia views") { Patterns = ["*.axaml", "*.xaml"] };

    private async Task OpenFile()
    {
        if (!await ConfirmDiscard()) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open a trusted Avalonia view", AllowMultiple = false, FileTypeFilter = [AxamlType, FilePickerFileTypes.All]
            });
            if (files.Count == 0) return;
            using var file = files[0];
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var source = await reader.ReadToEndAsync();
            var next = CreateDocument(source);
            filePath = file.TryGetLocalPath();
            savedDiskText = source;
            document = next;
            selectedPath = "0";
            sourceDirty = false;
            if (filePath != null) RecentFiles.Add(filePath);
            Refresh();
        }
        catch (Exception error) { ShowError(error); }
    }

    /// <summary>Opens a specific file path directly, bypassing the file picker.</summary>
    private async Task OpenFileDirect(string path)
    {
        if (!await ConfirmDiscard()) return;
        try
        {
            var source = await File.ReadAllTextAsync(path);
            var next = CreateDocument(source);
            filePath = path;
            savedDiskText = source;
            document = next;
            selectedPath = "0";
            sourceDirty = false;
            RecentFiles.Add(filePath);
            Refresh();
        }
        catch (Exception error) { ShowError(error); }
    }

    private async Task<bool> SaveFile(bool saveAs)
    {
        if (sourceDirty && !ApplySource()) return false;
        try
        {
            var target = saveAs ? null : filePath;
            if (target != null && savedDiskText != null && File.Exists(target) && await File.ReadAllTextAsync(target) != savedDiskText)
            {
                ShowError("This file changed on disk. Use Save as to keep your edits without overwriting the external changes.");
                return false;
            }
            if (target == null)
            {
                using var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save Avalonia view", SuggestedFileName = filePath == null ? "View.axaml" : Path.GetFileName(filePath),
                    DefaultExtension = "axaml", FileTypeChoices = [AxamlType], ShowOverwritePrompt = true
                });
                if (file == null) return false;
                target = file.TryGetLocalPath();
                if (target == null) throw new InvalidOperationException("Choose a local file so the editor can save it atomically.");
            }
            var source = document.Source;
            await DocumentFiles.SaveAtomic(target, source);
            filePath = target;
            savedDiskText = source;
            document.MarkSaved();
            RecentFiles.Add(filePath);
            UpdateTitle();
            StatusLabel.Text = "Saved " + target;
            return true;
        }
        catch (Exception error) { ShowError(error); return false; }
    }

    private async Task ShowRecentFiles()
    {
        var recents = RecentFiles.Load();
        if (recents.Count == 0)
        {
            StatusLabel.Text = "No recent files yet.";
            return;
        }

        var dialog = new Window
        {
            Title = "Recent Files",
            Width = 300,
            Height = 360,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#181B23")
        };

        string? chosen = null;

        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = "RECENT FILES",
            FontSize = 10,
            Foreground = Avalonia.Media.Brush.Parse("#A797FF"),
            LetterSpacing = 1.5,
            Margin = new Thickness(0, 0, 0, 10)
        });

        foreach (var path in recents)
        {
            var btn = new Button
            {
                Content = Path.GetFileName(path),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 7),
                Background = Avalonia.Media.Brush.Parse("#1E2130"),
                Foreground = Avalonia.Media.Brush.Parse("#D0D5E8"),
                BorderThickness = new Thickness(0)
            };
            ToolTip.SetTip(btn, path);
            var capturedPath = path;
            btn.Click += (_, _) => { chosen = capturedPath; dialog.Close(); };
            panel.Children.Add(btn);
        }

        var cancelBtn = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        cancelBtn.Click += (_, _) => dialog.Close();
        panel.Children.Add(cancelBtn);

        var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        dialog.Content = scroll;

        await dialog.ShowDialog(this);

        if (chosen != null)
            await OpenFileDirect(chosen);
    }

    private async Task<bool> ConfirmDiscard()
    {
        if (!document.IsDirty && !sourceDirty) return true;
        var dialog = new Window { Title = "Unsaved changes", Width = 430, Height = 200, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 20 };
        panel.Children.Add(new TextBlock { Text = "Save your changes before continuing?", FontSize = 18 });
        panel.Children.Add(new TextBlock { Text = "Your view includes unsaved edits or an AXAML draft.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel" };
        var discard = new Button { Content = "Discard" };
        var save = new Button { Content = "Save" };
        save.Classes.Add("primary");
        cancel.Click += (_, _) => dialog.Close("cancel");
        discard.Click += (_, _) => dialog.Close("discard");
        save.Click += (_, _) => dialog.Close("save");
        buttons.Children.Add(cancel);
        buttons.Children.Add(discard);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        var choice = await dialog.ShowDialog<string?>(this);
        return choice == "discard" || choice == "save" && await SaveFile(false);
    }
}

public static class DocumentFiles
{
    public static async Task SaveAtomic(string path, string source)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, source, new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
