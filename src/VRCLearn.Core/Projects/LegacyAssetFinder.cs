using VRCLearn.Core.IO;
using System.Text;
using VRCLearn.Core.Packages;

namespace VRCLearn.Core.Projects;

internal static class LegacyAssetFinder
{
    /// <summary>只有这个路径核对 GUID。</summary>
    private const string UdonSharpPath = "Assets/UdonSharp";

    private const string AssetsDirectory = "Assets";
    private const string MetaExtension = ".meta";

    public sealed record Result(
        IReadOnlyList<LegacyAsset> Files,
        IReadOnlyList<LegacyAsset> Folders);

    public static async Task<Result> FindAsync(
        IFileIo io,
        IReadOnlyList<PackageInfo> packages,
        UnityProject project,
        CancellationToken cancellationToken)
    {
        var candidates = new List<Candidate>();

        Collect(packages, project, x => x.LegacyFolders, isFile: false, candidates);
        Collect(packages, project, x => x.LegacyFiles, isFile: true, candidates);

        var files = new Dictionary<string, string>();
        var folders = new Dictionary<string, string>();
        var byGuid = new Dictionary<Guid, (string Package, bool IsFile)>();

        foreach (var candidate in candidates)
        {
            if (Normalize(candidate.DeclaredPath) is not { } path)
            {
                continue;
            }

            if (Exists(io, path, candidate.IsFile) &&
                await GuidMatchesAsync(io, path, candidate.Guid, cancellationToken).ConfigureAwait(false))
            {
                (candidate.IsFile ? files : folders)[path] = candidate.Package;
            }
            else if (candidate.Guid is { } guid)
            {
                byGuid[guid] = (candidate.Package, candidate.IsFile);
            }
        }

        if (byGuid.Count > 0)
        {
            await FindByGuidAsync(io, byGuid, files, folders, cancellationToken).ConfigureAwait(false);
        }

        return new Result(
            [.. files.Select(x => new LegacyAsset(x.Key, x.Value))],
            [.. folders.Select(x => new LegacyAsset(x.Key, x.Value))]);
    }

    private static void Collect(
        IReadOnlyList<PackageInfo> packages,
        UnityProject project,
        Func<PackageManifest, IReadOnlyDictionary<string, string?>> legacyOf,
        bool isFile,
        List<Candidate> result)
    {
        foreach (var package in packages)
        {
            var installed = project.GetInstalledPackage(package.Name);

            foreach (var (path, guid) in legacyOf(package.Manifest))
            {
                if (installed is not null && legacyOf(installed).ContainsKey(path))
                {
                    continue;
                }

                result.Add(new Candidate(package.Name, path, ParseGuid(guid), isFile));
            }
        }
    }

    private static string? Normalize(string declaredPath)
    {
        var segments = declaredPath.Replace('\\', '/').Split('/');

        if (segments.Length < 2 || segments[0] is not (AssetsDirectory or UnityProject.PackagesDirectory))
        {
            return null;
        }

        return segments.Any(x => x is "" or "." or "..") ? null : string.Join('/', segments);
    }

    private static bool Exists(IFileIo io, string path, bool isFile) =>
        isFile ? io.FileExists(path) : io.DirectoryExists(path);

    private static async Task<bool> GuidMatchesAsync(
        IFileIo io,
        string path,
        Guid? guid,
        CancellationToken cancellationToken)
    {
        if (guid is null || !string.Equals(path, UdonSharpPath, StringComparison.Ordinal))
        {
            return true;
        }

        var actual = await TryParseMetaAsync(io, path + MetaExtension, cancellationToken).ConfigureAwait(false);
        return actual is null || actual == guid;
    }

    private static async Task FindByGuidAsync(
        IFileIo io,
        Dictionary<Guid, (string Package, bool IsFile)> byGuid,
        Dictionary<string, string> files,
        Dictionary<string, string> folders,
        CancellationToken cancellationToken)
    {
        foreach (var relative in io.ListFiles(AssetsDirectory, recursive: true))
        {
            if (!relative.EndsWith(MetaExtension, StringComparison.Ordinal))
            {
                continue;
            }

            if (await TryParseMetaAsync(io, $"{AssetsDirectory}/{relative}", cancellationToken).ConfigureAwait(false)
                is not { } guid)
            {
                continue;
            }

            var path = $"{AssetsDirectory}/{relative[..^MetaExtension.Length]}";
            var isFile = io.FileExists(path);

            if (!isFile && !io.DirectoryExists(path))
            {
                continue;
            }

            if (byGuid.TryGetValue(guid, out var entry) && entry.IsFile == isFile)
            {
                byGuid.Remove(guid);
                (isFile ? files : folders)[path] = entry.Package;
            }
        }
    }

    private static async Task<Guid?> TryParseMetaAsync(IFileIo io, string path, CancellationToken cancellationToken)
    {
        if (await io.ReadAllBytesOrNullAsync(path, cancellationToken).ConfigureAwait(false) is not { } bytes)
        {
            return null;
        }

        foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            if (line.StartsWith("guid: ", StringComparison.Ordinal))
            {
                return ParseGuid(line["guid: ".Length..].Trim());
            }
        }

        return null;
    }

    private static Guid? ParseGuid(string? text) =>
        text is { Length: 32 } && Guid.TryParseExact(text, "N", out var guid) ? guid : null;

    private sealed record Candidate(string Package, string DeclaredPath, Guid? Guid, bool IsFile);
}
