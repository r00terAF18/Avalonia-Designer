namespace AvaloniaDesigner;

public static class RecentFiles
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AvaloniaStudio");
    private static readonly string FilePath = Path.Combine(ConfigDir, "recent.txt");
    private const int MaxEntries = 10;

    public static IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            return File.ReadAllLines(FilePath)
                .Where(line => !string.IsNullOrWhiteSpace(line) && File.Exists(line))
                .Distinct()
                .Take(MaxEntries)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static void Add(string path)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var existing = Load().ToList();
            existing.RemoveAll(entry => string.Equals(entry, path, StringComparison.OrdinalIgnoreCase));
            existing.Insert(0, path);
            File.WriteAllLines(FilePath, existing.Take(MaxEntries));
        }
        catch
        {
            // Non-fatal: ignore errors when persisting recent files.
        }
    }
}
