using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Axiom.Hosting;
using Axiom.Network;
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

        if (IsAppLaunch(Environment.ProcessPath, parsed.Args, WindowsConsole.StartedByDoubleClick()))
        {
            // Double-clicked (axiom.exe in Explorer, or Axiom.app in Finder): open the app, without a console window.
            WindowsConsole.Hide();
            return await HandleUiAsync(["ui"]);
        }

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
                "ui" => await HandleUiAsync(rest),
                "network" => await HandleNetworkAsync(rest),
                _ => HandleUnknownCommand(),
            };
        }
        catch (Exception ex)
        {
            return await WriteErrorAndReturnAsync("UNEXPECTED", ex.Message, null, 3);
        }
    }

    /// <summary>
    /// True when the program was started as an app rather than as a command: with no arguments, either double-clicked
    /// in Windows Explorer (the only process in its console) or started from a macOS app bundle (Finder, Dock, Launchpad).
    /// Older macOS versions pass a <c>-psn_...</c> argument to apps; it counts as none.
    /// </summary>
    internal static bool IsAppLaunch(string? processPath, string[] args, bool doubleClickedOnWindows)
    {
        if (args.Any(a => !a.StartsWith("-psn_", StringComparison.Ordinal)))
        {
            return false;
        }

        return doubleClickedOnWindows
            || (processPath?.Contains(".app/Contents/MacOS/", StringComparison.Ordinal) ?? false);
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
            // "local" secrets: the values stored on this machine by the app (secure storage), if any. The keychain may
            // ask for permission only when someone is at a terminal to answer; otherwise a read fails and says why.
            var localSecrets = new LocalSecretStore(new LazySecureStorage(allowPrompts: !Console.IsInputRedirected), AppData.Directory)
                .ValuesForRun(args[1], environment, out var secretProblems);
            foreach (var problem in secretProblems)
            {
                await Console.Error.WriteLineAsync(problem);
            }

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

        // AXIOM_NO_OPEN=1 does the same as --no-open, also for a double-click start (useful to check a build headless).
        var open = !args.Any(a => string.Equals(a, "--no-open", StringComparison.OrdinalIgnoreCase))
            && Environment.GetEnvironmentVariable("AXIOM_NO_OPEN") is not ("1" or "true");
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

    /// <summary>
    /// Shows how Axiom reaches <c>url</c> (proxy, sign-in, extra trusted certificates) and tries it: the first thing to
    /// run when requests fail behind a company proxy.
    /// </summary>
    private static async Task<int> HandleNetworkAsync(string[] args)
    {
        if (args.Length < 2 || !Uri.TryCreate(args[1], UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return await WriteUsageAndReturnAsync("Usage: axiom network <http(s)-url>");
        }

        NetworkSettings settings;
        try
        {
            settings = NetworkSettings.FromEnvironment();
        }
        catch (InvalidOperationException ex)
        {
            return await WriteErrorAndReturnAsync("NETWORK_SETTINGS", ex.Message, null, 3);
        }

        var proxy = settings.ProxyFor(uri);
        var environment = new[] { "AXIOM_PROXY", "HTTPS_PROXY", "HTTP_PROXY", "ALL_PROXY", "NO_PROXY", "AXIOM_CA_CERTS" }
            .Select(name => (Name: name, Value: Environment.GetEnvironmentVariable(name) ?? Environment.GetEnvironmentVariable(name.ToLowerInvariant())))
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Value))
            .ToDictionary(variable => variable.Name, variable => variable.Name.EndsWith("PROXY", StringComparison.Ordinal) && variable.Name != "NO_PROXY"
                ? ExplicitProxyAddress.Redact(variable.Value!)
                : variable.Value!);

        using var client = new HttpClient(AxiomHttp.CreateHandler(settings)) { Timeout = TimeSpan.FromSeconds(30) };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int? status = null;
        string? error = null;
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            status = (int)response.StatusCode;
            if (response.StatusCode == System.Net.HttpStatusCode.ProxyAuthenticationRequired && proxy is not null)
            {
                error = NetworkErrors.DescribeProxySignIn(uri, proxy);
            }
        }
        catch (HttpRequestException ex)
        {
            error = NetworkErrors.Describe(ex, uri, settings);
        }
        catch (TaskCanceledException)
        {
            error = $"No answer within 30 seconds (request went {(proxy is null ? "direct" : $"through proxy {NetworkSettings.Display(proxy)}")}).";
        }

        var report = new
        {
            url = uri.ToString(),
            proxySetting = settings.ProxySource,
            route = proxy is null ? "direct" : NetworkSettings.Display(proxy),
            extraRoots = settings.ExtraRoots.Count,
            environment,
            status,
            durationMs = (int)watch.Elapsed.TotalMilliseconds,
            error,
        };

        if (isJsonResponseMode)
        {
            WriteJsonEnvelope(new JsonEnvelope<object?>(error is null, report, error is null ? null : new JsonError("NETWORK", error, null)));
        }
        else
        {
            Console.WriteLine($"URL            : {report.url}");
            Console.WriteLine($"Proxy setting  : {report.proxySetting}{(settings.ProxySource == "system settings" ? " (the system's proxy settings, including a PAC script)" : string.Empty)}");
            Console.WriteLine($"Route          : {(proxy is null ? "direct" : $"through proxy {report.route}")}");
            Console.WriteLine($"Proxy sign-in  : {(proxy is null ? "-" : "the signed-in user (Windows NTLM / Kerberos), or the user name and password in the proxy address")}");
            Console.WriteLine($"Extra roots    : {(settings.ExtraRoots.Count == 0 ? "none (AXIOM_CA_CERTS not set)" : $"{settings.ExtraRoots.Count} from {settings.ExtraRootsFile}")}");
            foreach (var (name, value) in environment)
            {
                Console.WriteLine($"{name,-15}: {value}");
            }

            Console.WriteLine(error is null
                ? $"Result         : HTTP {status} in {report.durationMs} ms. The network path works."
                : $"Result         : failed after {report.durationMs} ms.{Environment.NewLine}                 {error}");
        }

        return error is null ? 0 : 3;
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
        Console.WriteLine("  axiom network <url>                               how Axiom reaches a URL (proxy, sign-in, certificates), and try it");
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
