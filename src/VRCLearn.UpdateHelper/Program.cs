using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using VRCLearn.Core.Configuration;
using VRCLearn.Infrastructure.Updates;

namespace VRCLearn.UpdateHelper;

internal static partial class Program
{
    private static int Main(string[] args)
    {
        var paths = AppPaths.Resolve([]);
        if (!paths.IsDefaultRoot)
        {
            return 1;
        }

        var root = paths.LauncherUpdateDirectory;
        var log = Path.Combine(root, "update.log");
        string? launcherToOpen = null;

        try
        {
            if (args is not [var pid, var launcher, var tag, var hash, var length, var signal]
                || !int.TryParse(pid, NumberStyles.None, CultureInfo.InvariantCulture, out var processId)
                || !Version.TryParse(tag, out var version) || version.Build < 0 || version.Revision >= 0
                || !UpdateInstallation.IsHash(hash)
                || !long.TryParse(length, NumberStyles.None, CultureInfo.InvariantCulture, out var size)
                || size is <= 0 or > UpdateInstallation.MaxInstallerSize
                || !signal.StartsWith("VRCLearnLauncher.UpdateReady.", StringComparison.Ordinal)
                || !UpdateInstallation.SamePath(root, AppContext.BaseDirectory)
                || UpdateInstallation.FindDirectory(launcher, root) is not { } directory)
            {
                return 1;
            }

            using var updating = new Mutex(false, UpdateInstallation.MutexName, out var created);
            if (!created)
            {
                return 1;
            }

            using var parent = Process.GetProcessById(processId);
            if (parent.MainModule?.FileName is not { } parentPath || !UpdateInstallation.SamePath(parentPath, launcher))
            {
                return 1;
            }

            using var ready = EventWaitHandle.OpenExisting(signal);
            using var proceed = EventWaitHandle.OpenExisting(signal + ".Proceed");
            var installer = Path.Combine(root, UpdateInstallation.InstallerName);

            if ((File.GetAttributes(installer) & FileAttributes.ReparsePoint) != 0)
            {
                return 1;
            }

            // Keep the verified package open without write/delete sharing through the installer run.
            using (var file = File.Open(installer, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (file.Length != size || !Convert.ToHexString(SHA256.HashData(file)).Equals(hash, StringComparison.OrdinalIgnoreCase)
                    || ReadVersion(directory) is not { } current || current >= version)
                {
                    return 1;
                }

                File.WriteAllText(log, $"[{DateTimeOffset.Now:O}] Update {current} to {version}.\n");
                ready.Set();

                if (!proceed.WaitOne(TimeSpan.FromSeconds(10)) || !parent.WaitForExit(30_000))
                {
                    WriteLog(log, "The launcher did not complete the handoff. Installation was skipped.");
                    return 1;
                }

                launcherToOpen = ApplyUpdate(directory, version, () => RunInstaller(installer, directory, root), log);
            }

            if (launcherToOpen is not null && ReadVersion(directory) == version)
            {
                File.Delete(installer);
                File.Delete(Path.Combine(root, "attempt.json"));
            }
        }
        catch (Exception exception) when (IsUpdateError(exception))
        {
            WriteLog(log, exception.ToString());
        }

        if (launcherToOpen is null)
        {
            return 1;
        }

        try
        {
            using var launcher = Process.Start(new ProcessStartInfo(launcherToOpen) { UseShellExecute = true });
            return launcher is null ? 1 : 0;
        }
        catch (Exception exception) when (IsUpdateError(exception))
        {
            WriteLog(log, exception.ToString());
            return 1;
        }
    }

    internal static string? ApplyUpdate(string directory, Version version, Func<int> install, string log)
    {
        var launcher = Path.Combine(directory, UpdateInstallation.LauncherName);
        var backup = $"{directory}.previous-{Guid.NewGuid():N}";
        var moved = false;

        try
        {
            // Record the recovery path before moving the current installation.
            File.AppendAllText(log, $"Previous installation: {backup}{Environment.NewLine}");
            Directory.Move(directory, backup);
            moved = true;

            var exit = install();
            if (exit != 0 || ReadVersion(directory) != version)
            {
                throw new InvalidDataException($"Installer returned {exit} or did not install version {version}.");
            }

            return launcher;
        }
        catch (Exception exception) when (IsUpdateError(exception))
        {
            WriteLog(log, exception.ToString());

            if (moved)
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Move(directory, $"{directory}.incomplete-{Guid.NewGuid():N}");
                    }

                    Directory.Move(backup, directory);
                }
                catch (Exception rollbackError) when (IsUpdateError(rollbackError))
                {
                    WriteLog(log, $"Could not restore {backup}: {rollbackError}");
                    MessageBox(IntPtr.Zero, $"Update failed. The previous installation is at:\n{backup}\n\nLog: {log}",
                        "VRCLearn Launcher", 0x10);
                    return null;
                }
            }

            return File.Exists(launcher) ? launcher : null;
        }
    }

    private static int RunInstaller(string installer, string directory, string root)
    {
        var start = new ProcessStartInfo(installer) { UseShellExecute = true, WorkingDirectory = root };

        foreach (var argument in new[]
        {
            "/SILENT", "/SP-", "/SUPPRESSMSGBOXES", "/NORESTART", "/NOCLOSEAPPLICATIONS", "/NORESTARTAPPLICATIONS",
            $"/DIR={directory}", $"/LOG={Path.Combine(root, "installer.log")}",
        })
        {
            start.ArgumentList.Add(argument);
        }

        using var setup = Process.Start(start) ?? throw new IOException("The installer could not be started.");
        setup.WaitForExit();
        return setup.ExitCode;
    }

    private static Version? ReadVersion(string directory)
    {
        var executable = Path.Combine(directory, UpdateInstallation.LauncherName);
        var assembly = Path.Combine(directory, UpdateInstallation.AssemblyName);

        if (!File.Exists(executable) || !File.Exists(assembly))
        {
            return null;
        }

        return AssemblyName.GetAssemblyName(assembly).Version is { Build: >= 0 } version
            ? new Version(version.Major, version.Minor, version.Build)
            : null;
    }

    private static bool IsUpdateError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException
            or ArgumentException or InvalidDataException or BadImageFormatException or SecurityException
            or WaitHandleCannotBeOpenedException;

    private static void WriteLog(string path, string text)
    {
        try
        {
            File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {text}{Environment.NewLine}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr window, string text, string caption, uint type);
}
