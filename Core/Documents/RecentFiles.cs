using System.IO;
using System.Text.Json;

namespace Mwrites.Core.Documents;

public sealed record RecentDocument(string Path, string Title, DateTimeOffset OpenedAt);

public static class RecentFiles
{
    private static readonly string Folder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSoft", "Mwrites");

    public static string StorePath => System.IO.Path.Combine(Folder, "recent.json");

    public static List<RecentDocument> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return new List<RecentDocument>();
            var json = File.ReadAllText(StorePath);
            var items = JsonSerializer.Deserialize<List<RecentDocument>>(json) ?? new List<RecentDocument>();
            return items.Where(item => !string.IsNullOrWhiteSpace(item.Path)).Take(10).ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<RecentDocument>();
        }
    }

    public static void Add(string path)
    {
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            var items = Load();
            items.RemoveAll(item => string.Equals(item.Path, full, StringComparison.OrdinalIgnoreCase));
            items.Insert(0, new RecentDocument(full, System.IO.Path.GetFileNameWithoutExtension(full), DateTimeOffset.Now));
            if (items.Count > 10) items = items.Take(10).ToList();
            Directory.CreateDirectory(Folder);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // La lista de recientes es una comodidad: si no se puede escribir, se ignora.
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(StorePath)) File.Delete(StorePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
