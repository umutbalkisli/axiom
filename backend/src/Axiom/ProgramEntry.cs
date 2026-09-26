using System.Text.Json;
using Axiom.Hosting;
using Axiom.Runtime;
using Axiom.Services;
using Axiom.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Axiom;

internal static class ProgramEntry
{
    private static bool isJsonResponseMode = false;

    public static async Task<int> RunAsync(string[] args)
    {
        var parsed = ParseCliArguments(args);
        isJsonResponseMode = parsed.JsonMode;

        if (parsed.Args.Length == 0)
        {
            return PrintHelpAndReturn();
        }

        var command = parsed.Args[0].Trim().ToLowerInvariant();
        var rest = parsed.Args;

        try
        {
            return command switch
            {
                "run" => await HandleRunAsync(rest),
                "serve" => await HandleServeAsync(rest),
                _ => HandleUnknownCommand(),
            };
        }
        catch (Exception ex)
        {
            return await WriteErrorAndReturnAsync("UNEXPECTED", ex.Message, null, 3);
        }
    }

    private static (bool JsonMode, string[] Args) ParseCliArguments(string[] args)
    {
        var jsonMode = args.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));
        var filtered = args.Where(a => !string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase)).ToArray();
        return (jsonMode, filtered);
    }

    private static (string? Value, string[] Args) ExtractOption(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length)
        {
            return (null, args);
        }

        return (args[index + 1], args.Where((_, i) => i != index && i != index + 1).ToArray());
    }

    private static int PrintHelpAndReturn()
    {
        if (!isJsonResponseMode)
        {
            PrintHelp();
            return 1;
        }

        WriteJsonEnvelope(new JsonEnvelope<object?>(false, null, new JsonError("USAGE", "No command provided.", null)));
        return 1;
    }

    private static int HandleUnknownCommand()
    {
        if (!isJsonResponseMode)
        {
            PrintHelp();
            return 1;
        }

        WriteJsonEnvelope(new JsonEnvelope<object?>(false, null, new JsonError("UNKNOWN_COMMAND", "Unknown command.", null)));
        return 1;
    }

    private static async Task<int> HandleRunAsync(string[] args)
    {
        // --env <name> (or AXIOM_ENVIRONMENT) selects environment-specific secret sources.
        var (environment, remaining) = ExtractOption(args, "--env");
        args = remaining;
        environment ??= Environment.GetEnvironmentVariable("AXIOM_ENVIRONMENT");

        if (args.Length < 2)
        {
            return await WriteUsageAndReturnAsync("Usage: axiom run <collection-folder> [--env <name>]");
        }

        try
        {
            await using var services = new ServiceCollection().AddAxiomCore().BuildServiceProvider();
            var result = await services.GetRequiredService<CollectionRunner>().RunAsync(args[1], new RunOptions { Environment = environment });

            if (isJsonResponseMode)
            {
                WriteJsonEnvelope(new JsonEnvelope<object?>(true, result, null));
            }
            else
            {
                Console.WriteLine(ReportFormatterService.Format(result));
            }

            return result.UnsuccessfulCount == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            return await WriteErrorAndReturnAsync("RUN_FAILED", $"Execution failed: {ex.Message}", null, 3);
        }
    }

    private static async Task<int> HandleServeAsync(string[] args)
    {
        var port = 50743;
        var index = 1;
        while (index < args.Length)
        {
            if (!string.Equals(args[index], "--port", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                continue;
            }

            if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out port) || port <= 0)
            {
                return await WriteUsageAndReturnAsync("Usage: axiom serve [--port <number>]");
            }

            index += 2;
        }

        await HostServerService.RunAsync(port, CancellationToken.None);
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Axiom CLI");
        Console.WriteLine("  axiom run <collection-folder> [--env <name>] [--json]");
        Console.WriteLine("  axiom serve [--port <number>]");
    }

    private static async Task<int> WriteUsageAndReturnAsync(string message)
    {
        return await WriteErrorAndReturnAsync("USAGE", message, null, 1);
    }

    private static async Task<int> WriteErrorAndReturnAsync(string code, string message, List<FieldError>? fieldErrors, int exitCode)
    {
        if (isJsonResponseMode)
        {
            WriteJsonEnvelope(new JsonEnvelope<object?>(false, null, new JsonError(code, message, fieldErrors)));
        }
        else
        {
            await Console.Error.WriteLineAsync(message);
            if (fieldErrors is { Count: > 0 })
            {
                foreach (var error in fieldErrors)
                {
                    await Console.Error.WriteLineAsync($"  - {error.Field}: {error.Message}");
                }
            }
        }

        return exitCode;
    }

    private static void WriteJsonEnvelope<T>(JsonEnvelope<T> envelope)
    {
        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        Console.WriteLine(json);
    }
    
    private sealed record JsonEnvelope<T>(bool Ok, T? Data, JsonError? Error);

    private sealed record JsonError(string Code, string Message, List<FieldError>? FieldErrors);
}
