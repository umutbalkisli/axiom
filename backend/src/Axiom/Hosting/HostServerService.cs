using Axiom.Documents;
using Axiom.Models;
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

namespace Axiom.Hosting;

internal static class HostServerService
{
    public static async Task RunAsync(int port, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder([]);
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        builder.Services.AddAxiomCore();

        var app = builder.Build();
        MapRoutes(app);

        await StartAndWaitAsync(app, port, cancellationToken);
    }

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

        app.MapGet("/api/tests/{fileName}", GetTest);
        app.MapPost("/api/tests", SaveTestAsync);
        app.MapDelete("/api/tests/{fileName}", DeleteTest);
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
        if (errors.Count > 0)
        {
            return ValidationFailed(errors);
        }

        var result = manager.SaveTest(folderPath, payload);
        return Results.Ok(new { result.FilePath, result.FileName });
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

    private static async Task<IResult> RunCollectionAsync(HttpRequest request, string folderPath, CollectionRunner runner, CancellationToken cancellationToken)
    {
        try
        {
            var payload = request.ContentLength > 0
                ? await request.ReadFromJsonAsync<RunPayload>(cancellationToken)
                : null;
            var result = await runner.RunAsync(folderPath, new RunOptions { LocalSecrets = payload?.LocalSecrets, Environment = payload?.Environment }, cancellationToken);
            return Results.Ok(new
            {
                exitCode = result.FailedCount == 0 ? 0 : 2,
                report = ReportFormatterService.Format(result),
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Execution failed: {ex.Message}", statusCode: 500);
        }
    }

    private static IResult ValidationFailed(List<FieldError> errors) =>
        Results.BadRequest(new { message = "Validation failed.", fieldErrors = errors });

    private static async Task StartAndWaitAsync(WebApplication app, int port, CancellationToken cancellationToken)
    {
        await app.StartAsync(cancellationToken);

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.FirstOrDefault() ?? $"http://127.0.0.1:{port}";
        Console.WriteLine($"AXIOM_HOST_READY {address}");

        await app.WaitForShutdownAsync(cancellationToken);
    }

    private sealed record CollectionSettingsPayload(
        Dictionary<string, object?> Variables,
        Dictionary<string, object?> Connections,
        Dictionary<string, SecretReference>? Secrets);

    private sealed record RunPayload(Dictionary<string, string>? LocalSecrets, string? Environment);

    private sealed record InitCollectionPayload(string CollectionName);

    private sealed record OpenApiImportPayload(string CollectionName, string SpecificationUrl);
}
