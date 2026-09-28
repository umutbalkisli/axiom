using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

namespace Axiom.Hosting;

/// <summary>
/// The web UI's files: normally the copy built into the executable (embedded resources named <c>ui/...</c>), or a
/// folder on disk while developing the UI.
/// </summary>
internal sealed class UiFiles
{
    private const string IndexFile = "index.html";
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();
    private readonly Func<string, Stream?> _open;

    private UiFiles(Func<string, Stream?> open, bool available)
    {
        _open = open;
        IsAvailable = available;
    }

    /// <summary>
    /// False when the executable was built without the UI (see the README: build the UI first).
    /// </summary>
    public bool IsAvailable { get; }

    public static UiFiles Embedded()
    {
        var assembly = typeof(UiFiles).Assembly;
        // Resource names use the build machine's separators; look them up with '/'.
        var names = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("ui/", StringComparison.Ordinal) || name.StartsWith("ui\\", StringComparison.Ordinal))
            .ToDictionary(name => name[3..].Replace('\\', '/'), name => name, StringComparer.Ordinal);
        return new UiFiles(path => names.TryGetValue(path, out var name) ? assembly.GetManifestResourceStream(name) : null, names.ContainsKey(IndexFile));
    }

    public static UiFiles FromDirectory(string directory)
    {
        var root = Path.GetFullPath(directory);
        return new UiFiles(path =>
        {
            var full = Path.GetFullPath(Path.Combine(root, path));
            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(full) ? File.OpenRead(full) : null;
        }, File.Exists(Path.Combine(root, IndexFile)));
    }

    /// <summary>
    /// Answers a GET for a UI path. A path that is not a file gets <c>index.html</c> (the UI decides what to show);
    /// hashed build assets are cached for good, everything else is revalidated.
    /// </summary>
    public async Task<bool> TryServeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.TrimStart('/') ?? string.Empty;
        if (path.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var stream = path.Length > 0 ? _open(path) : null;
        if (stream is null)
        {
            if (Path.HasExtension(path))
            {
                return false;
            }

            path = IndexFile;
            stream = _open(path);
            if (stream is null)
            {
                return false;
            }
        }

        await using (stream)
        {
            context.Response.ContentType = ContentTypes.TryGetContentType(path, out var type) ? type : "application/octet-stream";
            context.Response.Headers.CacheControl = path.StartsWith("assets/", StringComparison.Ordinal)
                ? "public, max-age=31536000, immutable"
                : "no-cache";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
        }

        return true;
    }
}
