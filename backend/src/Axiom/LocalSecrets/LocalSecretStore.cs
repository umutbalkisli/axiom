using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Axiom.LocalSecrets;

/// <summary>
/// Values of secrets whose provider is <c>local</c>, per collection folder. The names are listed in the app data folder;
/// the values live only in the operating system's secure storage and are read back only to run tests, never by the UI.
/// </summary>
internal sealed class LocalSecretStore(ISecureStorage storage, string directory)
{
    private readonly Lock _lock = new();
    private readonly string _indexPath = Path.Combine(directory, "local-secrets.json");

    public IReadOnlyList<string> Names(string folderPath)
    {
        lock (_lock)
        {
            return LoadIndex().GetValueOrDefault(Normalize(folderPath)) ?? [];
        }
    }

    public void Set(string folderPath, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A secret name is required.", nameof(name));
        }

        lock (_lock)
        {
            storage.Write(KeyOf(folderPath, name), value);
            var index = LoadIndex();
            var names = index.GetValueOrDefault(Normalize(folderPath)) ?? [];
            if (!names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }

            index[Normalize(folderPath)] = names;
            SaveIndex(index);
        }
    }

    public void Delete(string folderPath, string name)
    {
        lock (_lock)
        {
            storage.Delete(KeyOf(folderPath, name));
            var index = LoadIndex();
            if (index.TryGetValue(Normalize(folderPath), out var names) && names.Remove(name))
            {
                if (names.Count == 0)
                {
                    index.Remove(Normalize(folderPath));
                }

                SaveIndex(index);
            }
        }
    }

    /// <summary>
    /// Every stored value of a collection, for a run. A value that cannot be read is left out; the run then reports
    /// that secret as missing.
    /// </summary>
    public Dictionary<string, string> Values(string folderPath)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Names(folderPath))
        {
            try
            {
                if (storage.Read(KeyOf(folderPath, name)) is { } value)
                {
                    values[name] = value;
                }
            }
            catch (InvalidOperationException)
            {
                // The store refused (for example a locked keychain on a build machine): reported as missing by the run.
            }
        }

        return values;
    }

    private static string Normalize(string folderPath) => Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);

    /// <summary>
    /// A fixed-length key per folder and name, so no path or name has to fit a storage's naming rules.
    /// </summary>
    private static string KeyOf(string folderPath, string name) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{Normalize(folderPath)}\0{name}")));

    private Dictionary<string, List<string>> LoadIndex()
    {
        try
        {
            return File.Exists(_indexPath)
                ? JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(_indexPath)) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void SaveIndex(Dictionary<string, List<string>> index)
    {
        var temporary = _indexPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _indexPath, overwrite: true);
    }
}
