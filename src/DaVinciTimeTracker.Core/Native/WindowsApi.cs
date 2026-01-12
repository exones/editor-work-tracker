using System.Runtime.InteropServices;
using System.Text;

namespace DaVinciTimeTracker.Core.Native;

public static class WindowsApi
{
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
        return processName != null &&
               (processName.Contains("Resolve", StringComparison.OrdinalIgnoreCase) ||
                processName.Contains("DaVinci", StringComparison.OrdinalIgnoreCase));
    }
}
