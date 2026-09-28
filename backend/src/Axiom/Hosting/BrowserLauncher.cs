using System.Diagnostics;

namespace Axiom.Hosting;

/// <summary>
/// Opens the UI: in an app-style window (no tabs or address bar) of Edge or Chrome when one is installed, otherwise in
/// the default browser.
/// </summary>
internal static class BrowserLauncher
{
    public static void Open(string url)
    {
        var appWindow = new[] { $"--app={url}", "--window-size=1280,860" };
        foreach (var browser in AppModeBrowsers())
        {
            try
            {
                var start = new ProcessStartInfo(browser.FileName) { UseShellExecute = false };
                foreach (var argument in browser.Prefix.Concat(appWindow))
                {
                    start.ArgumentList.Add(argument);
                }

                Process.Start(start)?.Dispose();
                return;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Not usable: try the next one.
            }
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
    }

    private static IEnumerable<(string FileName, string[] Prefix)> AppModeBrowsers()
    {
        if (OperatingSystem.IsWindows())
        {
            var programFiles = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            };
            foreach (var relative in new[] { @"Microsoft\Edge\Application\msedge.exe", @"Google\Chrome\Application\chrome.exe" })
            {
                foreach (var root in programFiles.Where(p => p.Length > 0))
                {
                    var path = Path.Combine(root, relative);
                    if (File.Exists(path))
                    {
                        yield return (path, []);
                    }
                }
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            foreach (var app in new[] { "Google Chrome", "Microsoft Edge", "Chromium" })
            {
                if (Directory.Exists($"/Applications/{app}.app"))
                {
                    // -n: a new window even when the browser is already open.
                    yield return ("open", ["-na", app, "--args"]);
                }
            }
        }
        else
        {
            foreach (var name in new[] { "google-chrome", "chromium", "chromium-browser", "microsoft-edge" })
            {
                var path = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Select(directory => Path.Combine(directory, name))
                    .FirstOrDefault(File.Exists);
                if (path is not null)
                {
                    yield return (path, []);
                }
            }
        }
    }
}
