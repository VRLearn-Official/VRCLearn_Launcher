using System.Diagnostics;

namespace VRCLearn.Infrastructure.Shell;

public static class ShellLauncher
{
    public static void OpenUrl(string url) => Start(url);

    public static void OpenFolder(string directory) => Start(directory);

    public static void OpenFile(string path) => Start(path);

    private static void Start(string target)
    {
        using var process = Process.Start(new ProcessStartInfo(target)
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        });
    }
}
