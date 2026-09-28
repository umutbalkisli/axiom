using System.Text.Json.Nodes;
using Axiom.LocalSecrets;
using Axiom.Parsing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Axiom.Hosting;

/// <summary>
/// What the browser UI cannot do by itself: browse local folders, keep secrets in the operating system's secure
/// storage, remember preferences, and tell the host it is still open.
/// </summary>
internal static class LocalEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/fs/list", ListFolder);
        app.MapGet("/api/fs/check", (string path) => Results.Ok(new
        {
            exists = Directory.Exists(path),
            hasCollection = File.Exists(CollectionPaths.CollectionFile(path)),
        }));
        app.MapPost("/api/fs/mkdir", MakeFolderAsync);

        app.MapGet("/api/local-secrets", (string folderPath, LocalSecretStore store) => Results.Ok(new { names = store.Names(folderPath) }));
        app.MapPost("/api/local-secrets", SetLocalSecretAsync);
        app.MapDelete("/api/local-secrets/{name}", (string folderPath, string name, LocalSecretStore store) =>
        {
            store.Delete(folderPath, name);
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/preferences", (Preferences preferences) => Results.Ok(preferences.Read()));
        app.MapPost("/api/preferences", async (HttpRequest request, Preferences preferences, CancellationToken cancellationToken) =>
        {
            var changes = await request.ReadFromJsonAsync<JsonObject>(cancellationToken);
            return changes is null ? Results.BadRequest(new { message = "Invalid payload." }) : Results.Ok(preferences.Merge(changes));
        });

        app.MapPost("/api/session/ping", (UiSession session) =>
        {
            session.Ping();
            return Results.Ok(new { ok = true });
        });
        app.MapPost("/api/session/goodbye", (UiSession session) =>
        {
            session.Goodbye();
            return Results.Ok(new { ok = true });
        });
    }

    /// <summary>
    /// A folder's subfolders (hidden ones left out), for the folder picker. Without a path: the home folder.
    /// </summary>
    private static IResult ListFolder(string? path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folder = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? home : path);
        if (!Directory.Exists(folder))
        {
            return Results.BadRequest(new { message = $"Folder '{folder}' does not exist." });
        }

        var directories = new List<object>();
        try
        {
            foreach (var directory in new DirectoryInfo(folder).EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (directory.Name.StartsWith('.') || directory.Attributes.HasFlag(FileAttributes.Hidden) || directory.Attributes.HasFlag(FileAttributes.System))
                {
                    continue;
                }

                directories.Add(new
                {
                    name = directory.Name,
                    path = directory.FullName,
                    hasCollection = File.Exists(Path.Combine(directory.FullName, CollectionPaths.CollectionFileName)),
                });
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // An unreadable folder shows as empty.
        }

        var roots = OperatingSystem.IsWindows()
            ? DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => new { name = d.Name, path = d.RootDirectory.FullName }).ToList()
            : [new { name = "/", path = "/" }];

        return Results.Ok(new
        {
            path = folder,
            parent = Directory.GetParent(folder)?.FullName,
            hasCollection = File.Exists(CollectionPaths.CollectionFile(folder)),
            separator = Path.DirectorySeparatorChar.ToString(),
            home,
            roots,
            directories,
        });
    }

    private static async Task<IResult> MakeFolderAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var payload = await request.ReadFromJsonAsync<MakeFolderPayload>(cancellationToken);
        var name = payload?.Name?.Trim() ?? string.Empty;
        if (payload?.Parent is null || !Directory.Exists(payload.Parent))
        {
            return Results.BadRequest(new { message = "The parent folder does not exist." });
        }

        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(['/', '\\', ':']) >= 0)
        {
            return Results.BadRequest(new { message = $"'{name}' cannot be used as a folder name." });
        }

        var path = Path.Combine(payload.Parent, name);
        if (Directory.Exists(path) || File.Exists(path))
        {
            return Results.BadRequest(new { message = $"'{name}' already exists." });
        }

        Directory.CreateDirectory(path);
        return Results.Ok(new { path });
    }

    private static async Task<IResult> SetLocalSecretAsync(HttpRequest request, string folderPath, LocalSecretStore store, CancellationToken cancellationToken)
    {
        var payload = await request.ReadFromJsonAsync<LocalSecretPayload>(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload?.Name) || payload.Value is null)
        {
            return Results.BadRequest(new { message = "A name and a value are required." });
        }

        try
        {
            store.Set(folderPath, payload.Name.Trim(), payload.Value);
            return Results.Ok(new { ok = true });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private sealed record MakeFolderPayload(string? Parent, string? Name);

    private sealed record LocalSecretPayload(string? Name, string? Value);
}
