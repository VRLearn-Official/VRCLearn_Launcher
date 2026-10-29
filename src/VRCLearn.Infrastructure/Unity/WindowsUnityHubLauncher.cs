using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using Microsoft.Win32;
using VRCLearn.Core.Unity;
using VRCLearn.Core.Versioning;
using VRCLearn.Core.Vpm;

namespace VRCLearn.Infrastructure.Unity;

public sealed class WindowsUnityHubLauncher(ILauncherSettingsStore settings) : IUnityHubLauncher
{
    private const string ProcessName = "Unity Hub";

    private const string RegistryKey = @"Software\Unity Technologies\Hub";
    private const string RegistryValue = "InstallLocation";
    private const string ExecutableName = "Unity Hub.exe";
    private const string DefaultPath = @"C:\Program Files\Unity Hub\Unity Hub.exe";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public string? ExecutablePath
    {
        get
        {
            var configured = settings.Current.UnityHubPath;

            if (!string.IsNullOrWhiteSpace(configured))
            {
                var candidate = Directory.Exists(configured)
                    ? Path.Combine(configured, ExecutableName)
                    : configured;

                if (File.Exists(candidate))
                {
                    return UppercaseDriveLetter(candidate);
                }
            }

            if (FromRegistry() is { } fromRegistry && File.Exists(fromRegistry))
            {
                return UppercaseDriveLetter(fromRegistry);
            }

            return File.Exists(DefaultPath) ? DefaultPath : null;
        }
    }

    internal static string UppercaseDriveLetter(string path) =>
        path.Length >= 2 && path[1] == ':' ? char.ToUpperInvariant(path[0]) + path[1..] : path;

    public bool IsRunning => HasProcess();

    public async Task<UnityHubStartResult> EnsureRunningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (FindReadyWindow() is { } visible)
        {
            CloseWindow(visible);
            return UnityHubStartResult.AlreadyRunning;
        }

        if (HasProcess())
        {
            return UnityHubStartResult.AlreadyRunning;
        }

        if (ExecutablePath is not { } executable)
        {
            return UnityHubStartResult.NotFound;
        }

        if (!Start(executable))
        {
            return UnityHubStartResult.Failed;
        }

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (FindReadyWindow() is { } window)
            {
                CloseWindow(window);
                return UnityHubStartResult.Started;
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }

        return UnityHubStartResult.Timeout;
    }

    /// 解析 <c>Unity Hub -- --headless editors -i</c> 的输出。
    public async Task<IReadOnlyList<UnityEditor>> ListEditorsAsync(CancellationToken cancellationToken)
    {
        if (ExecutablePath is not { } executable)
        {
            return [];
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        foreach (var argument in new[] { "--", "--headless", "editors", "-i" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        string stdout;

        try
        {
            using var process = Process.Start(startInfo);

            if (process is null)
            {
                return [];
            }

            stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return [];
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            Debug.WriteLine($"让 Unity Hub 报编辑器列表失败：{exception.Message}");
            return [];
        }

        return ParseEditors(stdout);
    }

    internal static IReadOnlyList<UnityEditor> ParseEditors(string stdout)
    {
        var editors = new List<UnityEditor>();

        foreach (var line in stdout.Split('\n'))
        {
            if (line.Split("installed at", 2) is not [var before, var path])
            {
                continue;
            }

            if (UnityVersion.TryParse(before.Trim().Split(' ')[0]) is not { } version)
            {
                continue;
            }

            if (AsExecutable(path.Trim()) is { } found)
            {
                editors.Add(new UnityEditor(version, found, PeImage.ReadArchitecture(found)));
            }
        }

        return editors;
    }

    private static string? AsExecutable(string path)
    {
        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return File.Exists(path) ? path : null;
        }

        var candidate = Path.Combine(path, "Editor", "Unity.exe");

        return File.Exists(candidate) ? candidate : null;
    }

    private static void CloseWindow(nint window)
    {
        try
        {
            PostMessage(window, WmClose, 0, 0);
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
            Debug.WriteLine($"关闭 Unity Hub 窗口失败：{exception.Message}");
        }
    }

    private static bool Start(string executable)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executable)
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            });

            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Debug.WriteLine($"启动 Unity Hub 失败：{exception.Message}");
            return false;
        }
    }

    /// Hub 窗口句柄；界面未出则返回 null。
    private static nint? FindReadyWindow()
    {
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                if (process.MainWindowHandle is var handle and not 0)
                {
                    return handle;
                }
            }
        }

        return null;
    }

    private static bool HasProcess()
    {
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            process.Dispose();
            return true;
        }

        return false;
    }

    private const uint WmClose = 0x0010;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    private static string? FromRegistry()
    {
        try
        {
            var location = Registry.LocalMachine
                .OpenSubKey(RegistryKey)
                ?.GetValue(RegistryValue) as string;

            return string.IsNullOrWhiteSpace(location) ? null : Path.Combine(location, ExecutableName);
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
