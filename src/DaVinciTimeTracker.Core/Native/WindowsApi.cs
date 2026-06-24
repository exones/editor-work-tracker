using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DaVinciTimeTracker.Core.Native;

public static class WindowsApi
{
    // ── Global hotkey registration ────────────────────────────────────────────
    // Used by WindowsHotkeyManager.
    // If TFM ever changes from net9.0-windows* to net9.0, add #if WINDOWS guards here.

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public const uint MOD_ALT     = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT   = 0x0004;
    public const uint MOD_WIN     = 0x0008;
    /// <summary>MOD_NOREPEAT: suppress auto-repeat WM_HOTKEY messages while key is held.</summary>
    public const uint MOD_NOREPEAT = 0x4000;

    // ── Idle / foreground window detection ────────────────────────────────────

    [DllImport("user32.dll")]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    public static TimeSpan GetIdleTime()
    {
        var lastInputInfo = new LASTINPUTINFO();
        lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);

        if (GetLastInputInfo(ref lastInputInfo))
        {
            var currentTickCount = Environment.TickCount64;
            var lastInputTickCount = (long)lastInputInfo.dwTime;
            var idleTimeMilliseconds = currentTickCount - lastInputTickCount;
            return TimeSpan.FromMilliseconds(idleTimeMilliseconds);
        }

        return TimeSpan.Zero;
    }

    public static string GetForegroundWindowTitle()
    {
        var handle = GetForegroundWindow();
        var text = new StringBuilder(256);
        GetWindowText(handle, text, text.Capacity);
        return text.ToString();
    }

    public static string? GetForegroundProcessName()
    {
        try
        {
            var handle = GetForegroundWindow();
            GetWindowThreadProcessId(handle, out uint processId);
            using (var process = System.Diagnostics.Process.GetProcessById((int)processId))
            {
                return process.ProcessName;
            }
        }
        catch
        {
            return null;
        }
    }

    public static bool IsDaVinciResolveInFocus()
    {
        var processName = GetForegroundProcessName();
        if (processName == null)
        {
            return false;
        }

        // Check for exact DaVinci Resolve process names
        // Don't use Contains() to avoid matching our own "DaVinciTimeTracker" process!
        var daVinciProcessNames = new[] { "Resolve", "DaVinciResolve", "resolve" };
        return daVinciProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsDaVinciResolveRunning()
    {
        try
        {
            // Check for common DaVinci Resolve process names
            var processNames = new[] { "Resolve", "DaVinciResolve", "resolve" };

            foreach (var name in processNames)
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(name);
                if (processes.Length > 0)
                {
                    // Dispose all process objects
                    foreach (var p in processes)
                        p.Dispose();
                    return true;
                }
            }

            return false;
        }
        catch
        {
            // If we can't check, assume not running to avoid unnecessary API calls
            return false;
        }
    }
}
