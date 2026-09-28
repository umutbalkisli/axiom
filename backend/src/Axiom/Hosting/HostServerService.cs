using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Axiom.Documents;
using Axiom.LocalSecrets;
using Axiom.Models;
using Axiom.Parsing;
using Axiom.Secrets;
using Axiom.Runtime;
using Axiom.Services;
using Axiom.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Axiom.Hosting;

internal static class HostServerService
{
    /// <summary>
    /// Name of the cookie that carries the token for the browser UI.
    /// </summary>
    internal const string SessionCookie = "axiom_session";

    /// <summary>
    /// Serves the API (and with <see cref="HostOptions.Ui"/> the web UI) on 127.0.0.1 until shut down. Every request
    /// must carry the token, as <c>Authorization: Bearer</c> or as the UI's session cookie: the API reads and writes files
    /// and runs tests, so neither another local process nor a web page in the user's browser may call it.
    /// <paramref name="onReady"/> gets the address once the host listens.
    /// </summary>
    public static async Task RunAsync(HostOptions options, Action<string>? onReady, CancellationToken cancellationToken)
    {
        await using var app = Build(options);
        await StartAndWaitAsync(app, options, onReady, cancellationToken);
    }

    internal static WebApplication Build(int port, string token, string? dataDirectory = null) =>
        Build(new HostOptions { Port = port, Token = token, DataDirectory = dataDirectory });

    internal static WebApplication Build(HostOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Token))
        {
            throw new ArgumentException("A host token is required.", nameof(options));
        }

        var builder = WebApplication.CreateSlimBuilder([]);
        builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");

        builder.Services.AddAxiomCore();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(TimeProvider.System);
        var dataDirectory = options.DataDirectory ?? AppData.Directory;
        Directory.CreateDirectory(dataDirectory);
        builder.Services.AddSingleton(new Preferences(dataDirectory));
        builder.Services.AddSingleton(new LocalSecretStore(options.SecureStorage ?? new LazySecureStorage(), dataDirectory));
        builder.Services.AddSingleton<UiSession>();
        var uiFiles = options.UiDirectory is { } directory ? UiFiles.FromDirectory(directory) : UiFiles.Embedded();

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            // Only addresses of this machine: a web page that re-points its own domain name at 127.0.0.1 (DNS
            // rebinding) sends its domain here, and is turned away before anything else.
            if (!IsLoopbackHost(context.Request.Host.Host))
            {
                context.Response.StatusCode = StatusCodes.Status421MisdirectedRequest;
                return;
            }

            // The browser UI is opened once as /?token=...: that becomes an HttpOnly, SameSite=Strict cookie (other
            // sites' pages never send it), and the token leaves the address bar.
            if (options.Ui && context.Request.Query.TryGetValue("token", out var queryToken) && Matches(queryToken.ToString(), options.Token))
            {
                context.Response.Cookies.Append(SessionCookie, options.Token, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/",
                    IsEssential = true,
                });
                context.Response.Redirect(context.Request.PathBase + context.Request.Path);
                return;
            }

            if (!HasBearer(context.Request, options.Token) && !(options.Ui && HasCookie(context.Request, options.Token)))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                if (options.Ui && !context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.WriteAsync("<!doctype html><title>Axiom</title><p style=\"font-family:system-ui;margin:2rem\">This page needs the link Axiom opened for you. Start Axiom again (<code>axiom ui</code>) to open it.</p>");
                }

                return;
            }

            await next(context);

            // Anything not answered by the API is part of the UI.
            if (options.Ui && context.Response.StatusCode == StatusCodes.Status404NotFound && !context.Response.HasStarted
                && !context.Request.Path.StartsWithSegments("/api")
                && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                if (!await uiFiles.TryServeAsync(context))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    if (!uiFiles.IsAvailable)
                    {
                        await context.Response.WriteAsync("This Axiom was built without its UI. Build the UI first (see the README).");
                    }
                }
            }
        });
        MapRoutes(app);
        LocalEndpoints.Map(app);
        if (options.Ui)
        {
            app.Lifetime.ApplicationStarted.Register(() => app.Services.GetRequiredService<UiSession>().Start());
        }

        return app;
    }

    private static bool IsLoopbackHost(string host) =>
        host is "127.0.0.1" or "localhost" or "[::1]" or "::1";

    private static bool Matches(string candidate, string token) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(token));

    private static bool HasBearer(HttpRequest request, string token)
    {
        const string scheme = "Bearer ";
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith(scheme, StringComparison.Ordinal) && Matches(header[scheme.Length..], token);
    }

    private static bool HasCookie(HttpRequest request, string token) =>
        request.Cookies.TryGetValue(SessionCookie, out var cookie) && Matches(cookie, token);

    private static void MapRoutes(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { ok = true }));

        MapCollectionEndpoints(app);
        MapTestEndpoints(app);
        app.MapPost("/api/run", RunCollectionAsync);
    }

    private static void MapCollectionEndpoints(WebApplication app)
    {
        app.MapGet("/api/collection", (string folderPath, CollectionManagementService manager) =>
            Results.Ok(manager.GetCollection(folderPath)));

        app.MapPost("/api/collection/init", InitCollectionAsync);
        app.MapPost("/api/collection/variables", SaveCollectionVariablesAsync);
        app.MapPost("/api/collection/settings", SaveCollectionSettingsAsync);
        app.MapPost("/api/collection/secrets", SaveCollectionSecretsAsync);
        app.MapPost("/api/collection/import-openapi", ImportOpenApiAsync);
        app.MapGet("/api/secrets/providers", (IEnumerable<ISecretProvider> providers) =>
            Results.Ok(new { providers = providers.Select(p => new { name = p.Name, keyFormat = p.KeyFormat }) }));
    }

    private static void MapTestEndpoints(WebApplication app)
    {
        app.MapGet("/api/tests", (string folderPath, CollectionManagementService manager) =>
            Results.Ok(new { tests = manager.ListTests(folderPath) }));

        // A test is addressed by its path relative to the tests folder, which may contain '/' (orders/create.test.yaml).
        app.MapGet("/api/tests/{**fileName}", GetTest);
        app.MapGet("/api/assertions/aggregations", (IEnumerable<IAssertionAggregation> aggregations) =>
            Results.Ok(new { aggregations = aggregations.Select(a => a.Name) }));
        app.MapPost("/api/tests", SaveTestAsync);
        app.MapPost("/api/tests/preview", PreviewStepAsync);
        app.MapPost("/api/tests/clone", CloneTestAsync);
        app.MapPost("/api/tests/move", MoveTestAsync);
        app.MapPost("/api/folders/rename", RenameFolderAsync);
        app.MapPost("/api/folders/delete", DeleteFolderAsync);

        app.MapGet("/api/shared", (string folderPath, CollectionManagementService manager) =>
            Results.Ok(new { shared = manager.ListShared(folderPath) }));
        app.MapGet("/api/shared/{fileName}", GetShared);
        app.MapPost("/api/shared", SaveSharedAsync);
        app.MapDelete("/api/shared/{fileName}", DeleteShared);
        app.MapDelete("/api/tests/{**fileName}", DeleteTest);
    }

    private static async Task<IResult> InitCollectionAsync(HttpRequest request, string folderPath, CollectionInitializer initializer, CancellationToken cancellationToken)
    {
        var payload = await request.ReadFromJsonAsync<InitCollectionPayload>(cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.CollectionName))
        {
            return Results.BadRequest(new { message = "A collection name is required." });
        }

        try
        {
            await initializer.InitializeAsync(folderPath, payload.CollectionName, cancellationToken);
            return Results.Ok(new
            {
                folderPath = Path.GetFullPath(folderPath),
                collectionName = payload.CollectionName,
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> SaveCollectionVariablesAsync(HttpRequest request, string folderPath, CollectionManagementService manager, CancellationToken cancellationToken)
    {
        var variables = await request.ReadFromJsonAsync<Dictionary<string, object?>>(cancellationToken)
            ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var errors = CollectionSettingsValidator.ValidateVariables(variables);
        if (errors.Count > 0)
        {
            return ValidationFailed(errors);
        }

        manager.SaveCollectionVariables(folderPath, variables);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> SaveCollectionSettingsAsync(HttpRequest request, string folderPath, CollectionManagementService manager, CancellationToken cancellationToken)
    {
        var payload = await request.ReadFromJsonAsync<CollectionSettingsPayload>(cancellationToken);
        if (payload is null)
        {
            return Results.BadRequest(new { message = "Invalid payload." });
        }

        var errors = CollectionSettingsValidator.ValidateVariables(payload.Variables)
            .Concat(CollectionSettingsValidator.ValidateConnections(payload.Connections))
            .Concat(payload.Secrets is null ? [] : CollectionSettingsValidator.ValidateSecrets(payload.Secrets))
            .ToList();
        if (errors.Count > 0)
        {
            return ValidationFailed(errors);
        }

        manager.SaveCollectionSettings(folderPath, payload.Variables, payload.Connections, payload.Secrets);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> SaveCollectionSecretsAsync(HttpRequest request, string folderPath, CollectionManagementService manager, CancellationToken cancellationToken)
    {
        var secrets = await request.ReadFromJsonAsync<Dictionary<string, SecretReference>>(cancellationToken);
        if (secrets is null)
        {
            return Results.BadRequest(new { message = "Invalid payload." });
        }

        var errors = CollectionSettingsValidator.ValidateSecrets(secrets);
        if (errors.Count > 0)
        {
            return ValidationFailed(errors);
        }

        manager.SaveCollectionSecrets(folderPath, secrets);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> ImportOpenApiAsync(HttpRequest request, string folderPath, OpenApiImporter importer, CancellationToken cancellationToken)
    {
        var payload = await request.ReadFromJsonAsync<OpenApiImportPayload>(cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.SpecificationUrl))
        {
            return Results.BadRequest(new { message = "An OpenAPI specification URL is required." });
        }

        try
        {
            var imported = await importer.ImportFromUrlAsync(folderPath, payload.CollectionName, payload.SpecificationUrl, cancellationToken);
            return Results.Ok(new { imported });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static IResult GetTest(string folderPath, string fileName, CollectionManagementService manager)
    {
        try
        {
            var test = manager.GetTest(folderPath, fileName);
            if (test is null)
            {
                return Results.Ok(null);
            }

            return Results.Ok(new
            {
                fileName,
                test.Name,
                test.Description,
                test.Endpoint,
                test.Method,
                test.Variables,
                test.Steps,
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> SaveTestAsync(HttpRequest request, string folderPath, CollectionManagementService manager, TestCaseValidator validator, CancellationToken cancellationToken)
    {
        var payload = await request.ReadFromJsonAsync<SaveTestCaseRequest>(cancellationToken);
        if (payload is null)
        {
            return Results.BadRequest(new { message = "Invalid payload." });
        }

        var errors = validator.Validate(payload);
        errors.AddRange(TestCaseValidator.ValidateIncludes(payload.Steps, SharedIds(manager, folderPath)));
        if (errors.Count > 0)
        {
            return ValidationFailed(errors);
        }

        try
        {
            var result = manager.SaveTest(folderPath, payload);
            return Results.Ok(new { result.FilePath, result.FileName });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> CloneTestAsync(HttpRequest request, string folderPath, CollectionManagementService manager, CancellationToken cancellationToken)
    {
        var payload = await request.ReadFromJsonAsync<ClonePayload>(cancellationToken);
        return FileOperation(() =>
        {
            var result = manager.CloneTest(folderPath, payload?.FileName ?? string.Empty, payload?.Name ?? string.Empty);
            return new { result.FilePath, result.FileName };
        });
    }

    private static async Task<IResult> MoveTestAsync(HttpRequest request, string folderPath, CollectionManagementService manager, CancellationToken cancellationToken)
    {
        var payload = await request.ReadFromJsonAsync<MovePayload>(cancellationToken);
        return FileOperation(() => new { fileName = manager.MoveTest(folderPath, payload?.FileName ?? string.Empty, payload?.Folder) });
    }

    private static async Task<IResult> RenameFolderAsync(HttpRequest request, string folderPath, CollectionManagementService manager, CancellationToken cancellationToken)
    {
        var payload = await request.ReadFromJsonAsync<RenameFolderPayload>(cancellationToken);
        return FileOperation(() => new { folder = manager.RenameFolder(folderPath, payload?.Folder ?? string.Empty, payload?.NewFolder ?? string.Empty) });
    }

    private static async Task<IResult> DeleteFolderAsync(HttpRequest request, string folderPath, CollectionManagementService manager, CancellationToken cancellationToken)
    {
        var payload = await request.ReadFromJsonAsync<FolderPayload>(cancellationToken);
        return FileOperation(() => new { deleted = manager.DeleteFolder(folderPath, payload?.Folder ?? string.Empty) });
    }

    /// <summary>
    /// Runs a file operation; a refused one (bad name, missing file, a folder that already exists, the disk saying no)
    /// is a 400 with the reason.
    /// </summary>
    private static IResult FileOperation(Func<object> operation)
    {
        try
        {
            return Results.Ok(operation());
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static IReadOnlySet<string> SharedIds(CollectionManagementService manager, string folderPath) =>
        manager.ListShared(folderPath).Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IResult GetShared(string folderPath, string fileName, CollectionManagementService manager)
    {
        try
        {
            var shared = manager.GetShared(folderPath, fileName);
            return shared is null ? Results.Ok(null) : Results.Ok(new { fileName, shared.Name, shared.Description, shared.Run, shared.Steps });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> SaveSharedAsync(HttpRequest request, string folderPath, CollectionManagementService manager, TestCaseValidator validator, CancellationToken cancellationToken)
    {
        var payload = await request.ReadFromJsonAsync<SaveSharedStepsRequest>(cancellationToken);
        if (payload is null)
        {
            return Results.BadRequest(new { message = "Invalid payload." });
        }

        var errors = validator.Validate(payload);
        var ownId = string.IsNullOrWhiteSpace(payload.FileName) ? null : CollectionPaths.ToId(CollectionPaths.Shared, payload.FileName.Trim());
        var otherIds = SharedIds(manager, folderPath).Where(id => !string.Equals(id, ownId, StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        errors.AddRange(TestCaseValidator.ValidateIncludes(payload.Steps, otherIds));
        if (errors.Count > 0)
        {
            return ValidationFailed(errors);
        }

        try
        {
            var result = manager.SaveShared(folderPath, payload);
            return Results.Ok(new { result.FilePath, result.FileName });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static IResult DeleteShared(string folderPath, string fileName, CollectionManagementService manager)
    {
        try
        {
            manager.DeleteShared(folderPath, fileName);
            return Results.Ok(new { ok = true });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static IResult DeleteTest(string folderPath, string fileName, CollectionManagementService manager)
    {
        try
        {
            manager.DeleteTest(folderPath, fileName);
            return Results.Ok(new { ok = true });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Runs the collection and streams its progress as newline-delimited JSON: <c>started</c> (with the number of
    /// tests), one <c>test</c> event per finished test, then <c>completed</c> (the full result) or <c>failed</c>.
    /// Closing the request cancels the run.
    /// </summary>
    private static async Task RunCollectionAsync(HttpContext http, string folderPath, CollectionRunner runner, LocalSecretStore localSecrets, IOptions<JsonOptions> jsonOptions)
    {
        var cancellationToken = http.RequestAborted;
        // The body is optional; a chunked request has no Content-Length, so go by its content type.
        var payload = http.Request.HasJsonContentType() && http.Request.ContentLength != 0
            ? await http.Request.ReadFromJsonAsync<RunPayload>(cancellationToken)
            : null;

        http.Response.ContentType = "application/x-ndjson";
        var events = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });
        var writing = WriteEventsAsync(http.Response, events.Reader, jsonOptions.Value.SerializerOptions, cancellationToken);
        try
        {
            var options = new RunOptions
            {
                // Values of "local" secrets come from this machine's secure storage unless the caller brings its own.
                LocalSecrets = payload?.LocalSecrets ?? localSecrets.Values(folderPath),
                Environment = payload?.Environment,
                Tests = payload?.Tests,
                OnStarted = total => events.Writer.TryWrite(new { type = "started", total }),
                OnTestCompleted = test => events.Writer.TryWrite(new { type = "test", test }),
            };
            var result = await runner.RunAsync(folderPath, options, cancellationToken);
            events.Writer.TryWrite(new
            {
                type = "completed",
                exitCode = result.UnsuccessfulCount == 0 ? 0 : 2,
                report = ReportFormatterService.Format(result),
                result,
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away (the user pressed Cancel); there is no one left to tell.
        }
        catch (Exception ex)
        {
            events.Writer.TryWrite(new { type = "failed", message = $"Execution failed: {ex.Message}" });
        }
        finally
        {
            events.Writer.Complete();
        }

        await writing;
    }

    private static async Task WriteEventsAsync(HttpResponse response, ChannelReader<object> events, JsonSerializerOptions json, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in events.ReadAllAsync(CancellationToken.None))
            {
                await JsonSerializer.SerializeAsync(response.Body, item, item.GetType(), json, cancellationToken);
                await response.Body.WriteAsync("\n"u8.ToArray(), cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller disconnected.
        }
    }

    /// <summary>
    /// Runs an unsaved test's steps up to one of them and returns what that step received.
    /// </summary>
    private static async Task<IResult> PreviewStepAsync(HttpRequest request, string folderPath, CollectionRunner runner, YamlCollectionLoader loader, LocalSecretStore localSecrets, CancellationToken cancellationToken)
    {
        var payload = await request.ReadFromJsonAsync<PreviewPayload>(cancellationToken);
        if (payload?.Test is null)
        {
            return Results.BadRequest(new { message = "Invalid payload." });
        }

        try
        {
            // Through the same YAML a save would write, so the preview runs exactly what will be saved.
            var sourceName = string.IsNullOrWhiteSpace(payload.Test.FileName) ? "unsaved test" : payload.Test.FileName;
            var test = loader.ParseTest(CollectionManagementService.ToYaml(payload.Test), sourceName);
            var preview = await runner.PreviewAsync(folderPath, test, payload.StepIndex, new RunOptions
            {
                LocalSecrets = payload.LocalSecrets ?? localSecrets.Values(folderPath),
                Environment = payload.Environment,
            }, cancellationToken);
            return Results.Ok(preview);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static IResult ValidationFailed(List<FieldError> errors) =>
        Results.BadRequest(new { message = "Validation failed.", fieldErrors = errors });

    private static async Task StartAndWaitAsync(WebApplication app, HostOptions options, Action<string>? onReady, CancellationToken cancellationToken)
    {
        await app.StartAsync(cancellationToken);

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = (addresses?.FirstOrDefault() ?? $"http://127.0.0.1:{options.Port}").TrimEnd('/');
        Console.WriteLine($"AXIOM_HOST_READY {address}");

        onReady?.Invoke(address);
        await app.WaitForShutdownAsync(cancellationToken);
    }

    private sealed record CollectionSettingsPayload(
        Dictionary<string, object?> Variables,
        Dictionary<string, object?> Connections,
        Dictionary<string, SecretReference>? Secrets);

    private sealed record RunPayload(Dictionary<string, string>? LocalSecrets, string? Environment, List<string>? Tests);

    private sealed record PreviewPayload(SaveTestCaseRequest? Test, int StepIndex, Dictionary<string, string>? LocalSecrets, string? Environment);

    private sealed record InitCollectionPayload(string CollectionName);

    private sealed record ClonePayload(string? FileName, string? Name);

    private sealed record MovePayload(string? FileName, string? Folder);

    private sealed record RenameFolderPayload(string? Folder, string? NewFolder);

    private sealed record FolderPayload(string? Folder);

    private sealed record OpenApiImportPayload(string CollectionName, string SpecificationUrl);
}
