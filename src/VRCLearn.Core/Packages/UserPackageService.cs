using System.Text.Json;
using System.Text.Json.Nodes;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.IO;
using VRCLearn.Core.Vpm;

namespace VRCLearn.Core.Packages;

public enum AddUserPackageStatus
{
    Success,

    AlreadyAdded,

    /// <summary>不是个绝对路径。</summary>
    NotAFolder,

    /// <summary>读不出 <c>package.json</c>。</summary>
    BadPackage,
}

/// <param name="Manifest">读不出来时为 null，该条仍会列出但不参与解析。</param>
public sealed record UserPackageFolder(string Path, PackageManifest? Manifest)
{
    public bool IsReadable => Manifest is not null;

    public string DisplayName => Manifest?.DisplayName ?? Manifest?.Name ?? Path;
}

/// <summary>每个目录里一个 <c>package.json</c>，与仓库包一起参与解析。</summary>
public sealed class UserPackageService(ILauncherSettingsStore settingsStore, IFileIoFactory fileIo)
{
    private const string ManifestFileName = "package.json";

    public IReadOnlyList<string> Paths => settingsStore.Current.UserPackageFolders;

    /// <summary>读不出来的那几条也会返回，只是 <c>Manifest</c> 是 null。</summary>
    public async Task<IReadOnlyList<UserPackageFolder>> LoadAsync(CancellationToken cancellationToken)
    {
        var folders = new List<UserPackageFolder>();

        foreach (var path in Paths)
        {
            folders.Add(new UserPackageFolder(path, await ReadManifestAsync(path, cancellationToken).ConfigureAwait(false)));
        }

        return folders;
    }

    public async Task<IReadOnlyList<PackageInfo>> LoadPackagesAsync(CancellationToken cancellationToken) =>
    [
        .. (await LoadAsync(cancellationToken).ConfigureAwait(false))
            .Where(x => x.Manifest is not null)
            .Select(x => PackageInfo.Local(x.Manifest!, x.Path)),
    ];

    public async Task<AddUserPackageStatus> AddAsync(string folder, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
        {
            return AddUserPackageStatus.NotAFolder;
        }

        var normalized = PathUtil.Normalize(folder);

        if (Paths.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return AddUserPackageStatus.AlreadyAdded;
        }

        if (await ReadManifestAsync(normalized, cancellationToken).ConfigureAwait(false) is null)
        {
            return AddUserPackageStatus.BadPackage;
        }

        settingsStore.Current.UserPackageFolders.Add(normalized);
        settingsStore.Save();

        return AddUserPackageStatus.Success;
    }

    public void Remove(string folder)
    {
        if (settingsStore.Current.UserPackageFolders.RemoveAll(
            x => string.Equals(x, folder, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            settingsStore.Save();
        }
    }

    private async Task<PackageManifest?> ReadManifestAsync(string folder, CancellationToken cancellationToken)
    {
        try
        {
            var io = fileIo.For(folder);

            if (await io.ReadAllBytesOrNullAsync(ManifestFileName, cancellationToken).ConfigureAwait(false)
                is not { } bytes)
            {
                return null;
            }

            return JsonNode.Parse(bytes) is JsonObject json ? LoosePackageManifest.TryRead(json) : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
