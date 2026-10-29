using System.Collections.Concurrent;
using System.Diagnostics;
using VRCLearn.Core.Unity;

namespace VRCLearn.Infrastructure.Unity;

public sealed class WindowsUnityLauncher : IUnityLauncher
{
    private const string LockFilePath = "Temp/UnityLockFile";

    private static readonly TimeSpan OpeningWindow = TimeSpan.FromSeconds(120);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _opening =
        new(StringComparer.OrdinalIgnoreCase);

    public void Launch(string executablePath, string projectDirectory, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"起不来：{executablePath}");

        _opening[projectDirectory] = DateTimeOffset.UtcNow;
    }

    public UnityProjectStatus Status(string projectDirectory)
    {
        if (IsLocked(projectDirectory))
        {
            _opening.TryRemove(projectDirectory, out _);
            return UnityProjectStatus.Open;
        }

        if (_opening.TryGetValue(projectDirectory, out var startedAt)
            && DateTimeOffset.UtcNow - startedAt < OpeningWindow)
        {
            return UnityProjectStatus.Opening;
        }

        _opening.TryRemove(projectDirectory, out _);

        return UnityProjectStatus.Closed;
    }

    public bool IsRunning(string projectDirectory) => Status(projectDirectory) != UnityProjectStatus.Closed;

    private static bool IsLocked(string projectDirectory) =>
        FileLock.IsLocked(Path.Combine(projectDirectory, LockFilePath));
}
