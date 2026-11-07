using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VRCLearn.Core.Configuration;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.Infrastructure.Updates;

public sealed record PendingLauncherUpdate(string Version, string Sha256, long Size, bool Automatic = false)
{
    public PendingLauncherUpdate() : this("", "", 0) { }
}

public sealed class LauncherUpdater(
    AppPaths paths,
    UpdateService releases,
    HttpClient http,
    ILogger<LauncherUpdater> logger)
{
    private static readonly bool OfficialBuild = typeof(LauncherUpdater).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(attribute => attribute.Key == "VRCLearnOfficialRelease" && attribute.Value == "true");

    private string Root => paths.LauncherUpdateDirectory;
    private string PendingPath => Path.Combine(Root, "pending.json");
    private string AttemptPath => Path.Combine(Root, "attempt.json");
    private string InstallerPath => Path.Combine(Root, UpdateInstallation.InstallerName);

    public bool CanInstall => FindInstalledDirectory() is not null;

    public static bool IsInstalling()
    {
        if (!Mutex.TryOpenExisting(UpdateInstallation.MutexName, out var mutex))
        {
            return false;
        }

        mutex.Dispose();
        return true;
    }

    public PendingLauncherUpdate? Pending => CanInstall ? ReadPending(PendingPath) : null;

    public bool InstallFailed => CanInstall && ReadPending(AttemptPath) is not null;

    public async Task StageAsync(
        string version, IProgress<int>? progress, CancellationToken cancellationToken, bool automatic = false)
    {
        if (!CanInstall)
        {
            throw new InvalidOperationException("The current installation does not support automatic updates.");
        }

        var package = await releases.GetInstallerAsync(version, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Root);
        if (UpdateInstallation.HasReparsePoint(Root))
        {
            throw new IOException("The launcher update directory is redirected.");
        }

        var temporary = Path.Combine(Root, $"download-{Guid.NewGuid():N}.part");

        try
        {
            await DownloadAsync(package, temporary, progress, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, InstallerPath, overwrite: true);

            if (!JsonFileStore.Save(PendingPath, new PendingLauncherUpdate(version, package.Sha256, package.Size, automatic)))
            {
                throw new IOException("Could not save the pending update.");
            }
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    public void CancelPending()
    {
        File.Delete(PendingPath);
        TryDelete(InstallerPath);
    }

    public bool TryStartInstall(IReadOnlyList<string> arguments, bool automaticUpdatesEnabled)
    {
        // Deep links, restart commands and isolated test sessions keep their usual startup behavior.
        if (arguments.Count != 0 || FindInstalledDirectory() is not { } installed
            || ReadPending(PendingPath) is not { } pending
            || (pending.Automatic && !automaticUpdatesEnabled))
        {
            return false;
        }

        try
        {
            // Consume consent once, even if the installer or handoff fails.
            File.Move(PendingPath, AttemptPath, overwrite: true);
            var helper = Path.Combine(Root, UpdateInstallation.HelperName);
            File.Delete(helper);
            File.Copy(Path.Combine(installed, UpdateInstallation.HelperName), helper);
            var signal = $"VRCLearnLauncher.UpdateReady.{Guid.NewGuid():N}";
            using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, signal);
            using var proceed = new EventWaitHandle(false, EventResetMode.ManualReset, signal + ".Proceed");

            var start = new ProcessStartInfo(helper)
            {
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in new[]
            {
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Environment.ProcessPath!,
                pending.Version,
                pending.Sha256,
                pending.Size.ToString(System.Globalization.CultureInfo.InvariantCulture),
                signal,
            })
            {
                start.ArgumentList.Add(argument);
            }

            using var worker = Process.Start(start);

            if (worker is not null && ready.WaitOne(TimeSpan.FromSeconds(10)))
            {
                proceed.Set();
                return true;
            }

            logger.LogWarning("更新助手未能启动");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception
            or InvalidOperationException)
        {
            logger.LogWarning(exception, "启动安装更新失败");
        }

        return false;
    }

    private string? FindInstalledDirectory()
    {
        if (!OfficialBuild || releases.HasCustomSource || !paths.IsDefaultRoot)
        {
            return null;
        }

        try
        {
            var installed = UpdateInstallation.FindDirectory(Environment.ProcessPath, Root);
            return installed is not null && File.Exists(Path.Combine(installed, UpdateInstallation.HelperName))
                ? installed
                : null;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException
            or SecurityException)
        {
            logger.LogWarning(exception, "无法确认启动器的安装位置");
            return null;
        }
    }

    private PendingLauncherUpdate? ReadPending(string path) =>
        JsonFileStore.Peek<PendingLauncherUpdate>(path) is { } pending
        && Version.TryParse(pending.Version, out var version)
        && version.Build >= 0 && version.Revision < 0
        && version > releases.CurrentVersion
        && pending.Size is > 0 and <= UpdateInstallation.MaxInstallerSize
        && UpdateInstallation.IsHash(pending.Sha256)
            ? pending
            : null;

    private async Task DownloadAsync(
        LauncherReleaseAsset package, string path, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        var token = timeout.Token;
        using var response = await http.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var location = response.RequestMessage?.RequestUri;

        if (location?.Scheme != Uri.UriSchemeHttps
            || location.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
        {
            throw new InvalidDataException("The installer download redirected outside GitHub.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long received = 0;
        var lastPercent = -1;
        int read;

        while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            received += read;

            if (received > package.Size)
            {
                throw new InvalidDataException("The installer exceeds the expected size.");
            }

            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            var percent = (int)(received * 100 / package.Size);

            if (percent != lastPercent)
            {
                lastPercent = percent;
                progress?.Report(percent);
            }
        }

        if (received != package.Size
            || !Convert.ToHexString(hash.GetHashAndReset()).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The installer size or SHA256 does not match the release.");
        }

        destination.Flush(flushToDisk: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
