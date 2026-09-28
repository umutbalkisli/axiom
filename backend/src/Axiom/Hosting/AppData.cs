using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axiom.Hosting;

/// <summary>
/// Where Axiom keeps what belongs to the user rather than to a collection: preferences and the list of locally
/// stored secrets. <c>%APPDATA%\Axiom</c> on Windows, <c>~/.config/Axiom</c> elsewhere; <c>AXIOM_DATA_DIR</c> overrides it.
/// </summary>
internal static class AppData
{
    public static string Directory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable("AXIOM_DATA_DIR");
            var directory = string.IsNullOrWhiteSpace(overridden)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Axiom")
                : overridden;
            System.IO.Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public static string PathOf(string fileName) => Path.Combine(Directory, fileName);
}

/// <summary>
/// The UI's preferences (language, theme, recent collections, ...), kept in the app data folder. The UI is served on a
/// new port at every launch, so browser storage (which is per port) would forget them.
/// </summary>
internal sealed class Preferences(string directory)
{
    private readonly Lock _lock = new();
    private readonly string _path = Path.Combine(directory, "preferences.json");

    public JsonObject Read()
    {
        lock (_lock)
        {
            return Load();
        }
    }

    /// <summary>
    /// Sets the given keys (a null value removes one) and keeps the others.
    /// </summary>
    public JsonObject Merge(JsonObject changes)
    {
        lock (_lock)
        {
            var current = Load();
            foreach (var (key, value) in changes)
            {
                if (value is null)
                {
                    current.Remove(key);
                }
                else
                {
                    current[key] = value.DeepClone();
                }
            }

            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, current.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, overwrite: true);
            return current;
        }
    }

    private JsonObject Load()
    {
        try
        {
            return File.Exists(_path) ? JsonNode.Parse(File.ReadAllText(_path)) as JsonObject ?? [] : [];
        }
        catch (JsonException)
        {
            return [];     // a damaged file must not stop the app; it is rewritten on the next change
        }
    }
}
