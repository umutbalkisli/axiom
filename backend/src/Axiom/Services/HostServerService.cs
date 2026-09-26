using Axiom.Documents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Axiom.Services;

internal static class HostServerService
{
    public static async Task RunAsync(int port, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder([]);
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        builder.Services.AddSingleton<AxiomService>();
        builder.Services.AddSingleton<CollectionManagementService>();

        var app = builder.Build();
        MapRoutes(app);

        await StartAndWaitAsync(app, port, cancellationToken);
    }

    private static void MapRoutes(WebApplication app)
    {
        MapHealthEndpoints(app);
        MapCollectionEndpoints(app);
        MapTestEndpoints(app);
        MapRunEndpoint(app);
    }

    private static void MapHealthEndpoints(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { ok = true }));
    }

    private static void MapCollectionEndpoints(WebApplication app)
    {
        app.MapGet("/api/collection", (string folderPath, CollectionManagementService manager) =>
        {
            var doc = manager.GetCollection(folderPath);
            return Results.Ok(doc);
        });

        app.MapPost("/api/collection/init", InitCollectionAsync);
        app.MapPost("/api/collection/variables", SaveCollectionVariablesAsync);
        app.MapPost("/api/collection/settings", SaveCollectionSettingsAsync);
        app.MapPost("/api/collection/import-openapi", ImportOpenApiAsync);
    }

    private static void MapTestEndpoints(WebApplication app)
    {
        app.MapGet("/api/tests", (string folderPath, CollectionManagementService manager) =>
        {
            var tests = manager.ListTests(folderPath);
            return Results.Ok(new { tests });
        });

        app.MapGet("/api/tests/{fileName}", (string folderPath, string fileName, CollectionManagementService manager) =>
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
        });

        app.MapPost("/api/tests", SaveTestAsync);
        app.MapDelete("/api/tests/{fileName}", DeleteTest);
    }

    private static void MapRunEndpoint(WebApplication app)
    {
        app.MapPost("/api/run", RunCollectionAsync);
    }

    private static async Task<IResult> InitCollectionAsync(HttpRequest request, string folderPath)
    {
        var payload = await request.ReadFromJsonAsync<InitCollectionPayload>(CancellationToken.None);
        if (payload is null || string.IsNullOrWhiteSpace(payload.CollectionName))
        {
            return Results.BadRequest(new { message = "A collection name is required." });
        }

        try
        {
            await AxiomService.InitializeCollectionAsync(folderPath, payload.CollectionName);
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

    private static async Task<IResult> SaveCollectionVariablesAsync(HttpRequest request, string folderPath, CollectionManagementService manager)
    {
        var variables = await request.ReadFromJsonAsync<Dictionary<string, object?>>(CancellationToken.None)
            ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var errors = ValidateCollectionVariables(variables);
        if (errors.Count > 0)
        {
            return Results.BadRequest(new { message = "Validation failed.", fieldErrors = errors });
        }

        manager.SaveCollectionVariables(folderPath, variables);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> SaveCollectionSettingsAsync(HttpRequest request, string folderPath, CollectionManagementService manager)
    {
        var payload = await request.ReadFromJsonAsync<CollectionSettingsPayload>(CancellationToken.None);
        if (payload is null)
        {
            return Results.BadRequest(new { message = "Invalid payload." });
        }

        var variableErrors = ValidateCollectionVariables(payload.Variables);
        var connectionErrors = ValidateCollectionConnections(payload.Connections);
        var errors = variableErrors.Concat(connectionErrors).ToList();
        if (errors.Count > 0)
        {
            return Results.BadRequest(new { message = "Validation failed.", fieldErrors = errors });
        }

        manager.SaveCollectionSettings(folderPath, payload.Variables, payload.Connections);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> ImportOpenApiAsync(HttpRequest request, string folderPath)
    {
        var payload = await request.ReadFromJsonAsync<OpenApiImportPayload>(CancellationToken.None);
        if (payload is null || string.IsNullOrWhiteSpace(payload.SpecificationUrl))
        {
            return Results.BadRequest(new { message = "An OpenAPI specification URL is required." });
        }

        try
        {
            var imported = await AxiomService.ImportOpenApiFromUrlAsync(folderPath, payload.CollectionName, payload.SpecificationUrl);
            return Results.Ok(new { imported });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> SaveTestAsync(HttpRequest request, string folderPath, CollectionManagementService manager)
    {
        var payload = await request.ReadFromJsonAsync<SaveTestCaseRequest>(CancellationToken.None);
        if (payload is null)
        {
            return Results.BadRequest(new { message = "Invalid payload." });
        }

        var errors = ValidateSaveTestRequest(payload);
        if (errors.Count > 0)
        {
            return Results.BadRequest(new { message = "Validation failed.", fieldErrors = errors });
        }

        var result = manager.SaveTest(folderPath, payload);
        return Results.Ok(new { result.FilePath, result.FileName });
    }

    private static IResult DeleteTest(string folderPath, string fileName)
    {
        CollectionManagementService.DeleteTest(folderPath, fileName);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> RunCollectionAsync(string folderPath, AxiomService service)
    {
        try
        {
            var result = await service.RunCollectionAsync(folderPath, CancellationToken.None);
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

    private static async Task StartAndWaitAsync(WebApplication app, int port, CancellationToken cancellationToken)
    {
        await app.StartAsync(cancellationToken);

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.FirstOrDefault() ?? $"http://127.0.0.1:{port}";
        Console.WriteLine($"AXIOM_HOST_READY {address}");

        await app.WaitForShutdownAsync(cancellationToken);
    }

    private static List<FieldError> ValidateCollectionVariables(Dictionary<string, object?> variables)
    {
        var errors = new List<FieldError>();
        foreach (var key in variables.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                errors.Add(new FieldError("variables", "Variable key cannot be empty."));
            }
        }

        return errors;
    }

    private static List<FieldError> ValidateSaveTestRequest(SaveTestCaseRequest request)
    {
        var errors = new List<FieldError>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add(new FieldError("name", "Test case name is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Endpoint))
        {
            errors.Add(new FieldError("endpoint", "Endpoint is required."));
        }

        if (request.Steps.Count == 0)
        {
            errors.Add(new FieldError("steps", "At least one step is required."));
            return errors;
        }

        for (var index = 0; index < request.Steps.Count; index++)
        {
            ValidateStep(request.Steps[index], index, errors);
        }

        return errors;
    }

    private static List<FieldError> ValidateCollectionConnections(Dictionary<string, object?> connections)
    {
        var errors = new List<FieldError>();
        foreach (var key in connections.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                errors.Add(new FieldError("connections", "Connection name cannot be empty."));
            }
        }

        return errors;
    }

    private static void ValidateStep(StepDocument step, int index, List<FieldError> errors)
    {
        var prefix = $"steps[{index}]";

        if (string.IsNullOrWhiteSpace(step.Id))
        {
            errors.Add(new FieldError($"{prefix}.id", "Step id is required."));
        }

        if (!IsSupportedStepType(step.Type))
        {
            errors.Add(new FieldError($"{prefix}.type", "Step type must be request or db_query."));
            return;
        }

        if (string.Equals(step.Type, "request", StringComparison.OrdinalIgnoreCase))
        {
            ValidateRequestStep(step, prefix, errors);
        }
        else
        {
            ValidateDbStep(step, prefix, errors);
        }

        for (var assertionIndex = 0; assertionIndex < step.Assert.Count; assertionIndex++)
        {
            ValidateAssertion(step.Assert[assertionIndex], prefix, assertionIndex, errors);
        }
    }

    private static void ValidateRequestStep(StepDocument step, string prefix, List<FieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(step.Method))
        {
            errors.Add(new FieldError($"{prefix}.method", "HTTP method is required for request step."));
        }

        if (string.IsNullOrWhiteSpace(step.Url))
        {
            errors.Add(new FieldError($"{prefix}.url", "URL is required for request step."));
        }
    }

    private static void ValidateDbStep(StepDocument step, string prefix, List<FieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(step.Connection))
        {
            errors.Add(new FieldError($"{prefix}.connection", "Connection is required for db_query step."));
        }

        if (string.IsNullOrWhiteSpace(step.Sql))
        {
            errors.Add(new FieldError($"{prefix}.sql", "SQL is required for db_query step."));
        }
    }

    private static void ValidateAssertion(AssertionDocument assertion, string stepPrefix, int assertionIndex, List<FieldError> errors)
    {
        var assertionPrefix = $"{stepPrefix}.assert[{assertionIndex}]";

        if (string.IsNullOrWhiteSpace(assertion.Source))
        {
            errors.Add(new FieldError($"{assertionPrefix}.source", "Assertion source is required."));
        }

        if (string.IsNullOrWhiteSpace(assertion.Operator))
        {
            errors.Add(new FieldError($"{assertionPrefix}.operator", "Assertion operator is required."));
        }
    }

    private static bool IsSupportedStepType(string? type)
    {
        return string.Equals(type, "request", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "db_query", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CollectionSettingsPayload(
        Dictionary<string, object?> Variables,
        Dictionary<string, object?> Connections);

    private sealed record InitCollectionPayload(string CollectionName);

    private sealed record OpenApiImportPayload(string CollectionName, string SpecificationUrl);

    private sealed record FieldError(string Field, string Message);
}
