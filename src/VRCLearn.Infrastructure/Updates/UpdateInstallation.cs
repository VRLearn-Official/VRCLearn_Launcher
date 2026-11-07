using Microsoft.Win32;

namespace VRCLearn.Infrastructure.Updates;

internal static class UpdateInstallation
{
    internal const string HelperName = "VRCLearn.UpdateHelper.exe";
    internal const string LauncherName = "VRCLearnLauncher.exe";
    internal const string AssemblyName = "VRCLearnLauncher.dll";
    internal const string InstallerName = "pending-setup.exe";
    internal const string MutexName = "VRCLearnLauncher.Updating";
    internal const long MaxInstallerSize = 256L * 1024 * 1024;
    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{C3B4BABC-4F9B-4281-A58B-02AA08BC61B7}_is1";

    internal static string? FindDirectory(string? executable, string updateDirectory)
    {
        if (executable is null || !Path.IsPathFullyQualified(executable))
        {
            return null;
        }

        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);

        if (key?.GetValue("InstallLocation") is not string location || !Path.IsPathFullyQualified(location))
        {
            return null;
        }

        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(location));
        var root = Path.GetPathRoot(directory);

        return root is not null
            && !string.Equals(directory, Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(directory), "VRCLearnLauncher", StringComparison.OrdinalIgnoreCase)
            && SamePath(executable, Path.Combine(directory, LauncherName))
            && Directory.Exists(directory)
            && !IsInside(directory, updateDirectory)
            && !IsInside(updateDirectory, directory)
            && !HasReparsePoint(directory)
            && !HasReparsePoint(updateDirectory)
            ? directory
            : null;
    }

    internal static bool SamePath(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string parent, string child)
    {
        var relative = Path.GetRelativePath(parent, child);

        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    internal static bool HasReparsePoint(string path)
    {
        for (var item = new DirectoryInfo(path); item is not null; item = item.Parent)
        {
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsHash(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
