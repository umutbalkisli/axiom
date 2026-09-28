using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Axiom.Hosting;
using Axiom.LocalSecrets;
using Axiom.Runtime;
using Axiom.Services;
using Axiom.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Axiom;

internal static class ProgramEntry
{
    private const string HostTokenVariable = "AXIOM_HOST_TOKEN";

    private static bool isJsonResponseMode = false;

    public static async Task<int> RunAsync(string[] args)
    {
        // Reports, JSON and HTTP bodies must not depend on the machine's regional settings.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        var parsed = ParseCliArguments(args);
        isJsonResponseMode = parsed.JsonMode;

        if (parsed.Args.Length == 0)
        {
            // Double-clicked in Windows Explorer: open the app, without the empty console window.
            if (WindowsConsole.StartedByDoubleClick())
            {
                WindowsConsole.Hide();
                return await HandleUiAsync(["ui"]);
            }

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
                "ui" => await HandleUiAsync(rest),
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
            // "local" secrets: the values stored on this machine by the app (secure storage), if any.
            var localSecrets = new LocalSecretStore(new LazySecureStorage(), AppData.Directory).Values(args[1]);
            var result = await services.GetRequiredService<CollectionRunner>().RunAsync(args[1], new RunOptions { Environment = environment, LocalSecrets = localSecrets });

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
        const string usage = "Usage: axiom serve [--port <number>]";
        var (port, portError) = ParsePort(args, 50743);
        if (portError)
        {
            return await WriteUsageAndReturnAsync(usage);
        }

        // A caller that starts the host passes its own token; started by hand, the host makes one up and prints it.
        var token = Environment.GetEnvironmentVariable(HostTokenVariable);
        if (string.IsNullOrWhiteSpace(token))
        {
            token = NewToken();
            Console.WriteLine($"AXIOM_HOST_TOKEN {token}");
        }

        await HostServerService.RunAsync(new HostOptions { Port = port, Token = token }, onReady: null, CancellationToken.None);
        return 0;
    }

    /// <summary>
    /// Starts the host with the web UI on a free port and opens it. The program ends when the UI window is closed.
    /// </summary>
    private static async Task<int> HandleUiAsync(string[] args)
    {
        const string usage = "Usage: axiom ui [--port <number>] [--no-open] [--ui-dir <folder>]";
        var (port, portError) = ParsePort(args, 0);
        var (uiDirectory, _) = ExtractOption(args, "--ui-dir");
        if (portError || (uiDirectory is not null && !Directory.Exists(uiDirectory)))
        {
            return await WriteUsageAndReturnAsync(usage);
        }

        var open = !args.Any(a => string.Equals(a, "--no-open", StringComparison.OrdinalIgnoreCase));
        var options = new HostOptions
        {
            Port = port,
            Token = NewToken(),
            Ui = true,
            UiDirectory = uiDirectory ?? Environment.GetEnvironmentVariable("AXIOM_UI_DIR"),
        };

        await HostServerService.RunAsync(options, address =>
        {
            var url = $"{address}/?token={options.Token}";
            Console.WriteLine($"Axiom is running. Open {url}");
            Console.WriteLine("It stops when its window is closed (or press Ctrl+C).");
            if (open)
            {
                BrowserLauncher.Open(url);
            }
        }, CancellationToken.None);
        return 0;
    }

    private static string NewToken() => RandomNumberGenerator.GetHexString(64, lowercase: true);

    private static (int Port, bool Error) ParsePort(string[] args, int fallback)
    {
        var (value, _) = ExtractOption(args, "--port");
        if (value is null)
        {
            return args.Any(a => string.Equals(a, "--port", StringComparison.OrdinalIgnoreCase)) ? (fallback, true) : (fallback, false);
        }

        return int.TryParse(value, out var port) && port is >= 0 and <= 65535 ? (port, false) : (fallback, true);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Axiom");
        Console.WriteLine("  axiom ui [--port <number>] [--no-open]           open the app (also: double-click the program on Windows)");
        Console.WriteLine("  axiom run <collection-folder> [--env <name>] [--json]");
        Console.WriteLine("  axiom serve [--port <number>]   API only (0 picks a free port; requests need 'Authorization: Bearer $AXIOM_HOST_TOKEN')");
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
