using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Unity;

namespace VRCLearn.Infrastructure.Unity;

public sealed class WindowsUnityWindowActivator : IUnityWindowActivator
{
    private const string ProcessName = "Unity";
    private const string EditorWindowClass = "UnityContainerWndClass";

    private const int SwRestore = 9;

    private const int FlashCount = 3;

    public BringUnityToFrontResult BringToFront(string projectDirectory)
    {
        if (FindWindow(projectDirectory) is not { } window)
        {
            return BringUnityToFrontResult.WindowNotFound;
        }

        if (IsIconic(window))
        {
            ShowWindow(window, SwRestore);
        }

        if (SetForegroundWindow(window))
        {
            return BringUnityToFrontResult.BroughtToFront;
        }

        Flash(window);
        return BringUnityToFrontResult.FailedToBringToFront;
    }

    private static nint? FindWindow(string projectDirectory)
    {
        var processes = UnityProcessIds();

        if (processes.Count == 0)
        {
            return null;
        }

        var name = PathUtil.NameOf(projectDirectory);
        var found = new List<(nint Window, string Title)>();

        EnumWindows(
            (window, _) =>
            {
                GetWindowThreadProcessId(window, out var processId);

                if (processes.Contains(processId) && IsEditorWindow(window))
                {
                    found.Add((window, Title(window)));
                }

                return true;
            },
            0);

        return found.Count == 1 ? found[0].Window : PickByTitle(found, name);
    }

    internal static nint? PickByTitle(IReadOnlyList<(nint Window, string Title)> windows, string projectName)
    {
        if (windows.Count == 0)
        {
            return null;
        }

        var matches = windows
            .Where(x => x.Title.Contains(projectName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count == 1 ? matches[0].Window : null;
    }

    internal static bool IsEditorWindow(string className, bool visible, bool hasMenu) =>
        visible && hasMenu && className == EditorWindowClass;

    private static bool IsEditorWindow(nint window)
    {
        var visible = IsWindowVisible(window);

        if (!visible || GetMenu(window) == 0)
        {
            return false;
        }

        var buffer = new StringBuilder(64);
        var length = GetClassNameW(window, buffer, buffer.Capacity);

        return length > 0 && IsEditorWindow(buffer.ToString(), visible, hasMenu: true);
    }

    private static string Title(nint window)
    {
        var buffer = new StringBuilder(256);
        var length = GetWindowTextW(window, buffer, buffer.Capacity);

        return length > 0 ? buffer.ToString() : "";
    }

    private static HashSet<uint> UnityProcessIds()
    {
        var ids = new HashSet<uint>();

        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                ids.Add((uint)process.Id);
            }
        }

        return ids;
    }

    private static void Flash(nint window)
    {
        var info = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            Window = window,

            Flags = FlashWindowTray | FlashWindowTimerNoForeground,
            Count = FlashCount,
            Timeout = 0,
        };

        FlashWindowEx(ref info);
    }

    private const uint FlashWindowTray = 0x00000002;
    private const uint FlashWindowTimerNoForeground = 0x0000000C;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public nint Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);

    [DllImport("user32.dll")]
    private static extern nint GetMenu(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder className, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, StringBuilder title, int capacity);
}
