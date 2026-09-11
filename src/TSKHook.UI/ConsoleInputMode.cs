using System.Runtime.InteropServices;

namespace TSKHook.UI;

internal static class ConsoleInputMode
{
    private const int StandardInputHandle = -10;
    private const uint QuickEdit = 0x0040;
    private const uint ExtendedFlags = 0x0080;

    /// <summary>Disable mouse selection on this game's console; return false when no console is available.</summary>
    internal static bool DisableQuickEdit()
    {
        var input = GetStdHandle(StandardInputHandle);
        if (!GetConsoleMode(input, out var mode)) return false;
        // Windows requires EXTENDED_FLAGS when clearing QUICK_EDIT_MODE.
        // Preserve every other input mode; this does not change console defaults.
        return SetConsoleMode(input, (mode | ExtendedFlags) & ~QuickEdit);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr console, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr console, uint mode);
}
