using System.Runtime.InteropServices;

namespace Axiom.Hosting;

/// <summary>
/// Axiom is a console program (so the CLI can print). Double-clicked in Explorer, Windows gives it a console window
/// of its own; that window is hidden when the app opens.
/// </summary>
internal static partial class WindowsConsole
{
    /// <summary>
    /// True when this process is the only one attached to its console, which is the case when Explorer started it
    /// (from a terminal, the shell is attached too).
    /// </summary>
    public static bool StartedByDoubleClick()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var processes = new uint[2];
        return GetConsoleProcessList(processes, (uint)processes.Length) == 1;
    }

    public static void Hide()
    {
        if (OperatingSystem.IsWindows())
        {
            ShowWindow(GetConsoleWindow(), 0);
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetConsoleProcessList([Out] uint[] processList, uint processCount);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetConsoleWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr window, int command);
}
