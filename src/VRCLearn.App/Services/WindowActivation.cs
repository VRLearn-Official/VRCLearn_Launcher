using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VRCLearn.App.Services;

internal static class WindowActivation
{
    private const int AsfwAny = -1;

    public static void AllowForeground() => AllowSetForegroundWindow(AsfwAny);

    public static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        var handle = new WindowInteropHelper(window).Handle;

        if (handle == 0)
        {
            window.Activate();
            return;
        }

        if (SetForegroundWindow(handle))
        {
            window.Focus();
            return;
        }

        window.Topmost = true;
        window.Topmost = false;
        window.Activate();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
