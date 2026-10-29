using System.Security.Cryptography;
using System.Text;
using VRCLearn.Core.Archives;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Hashing;
using VRCLearn.Core.Http;
using VRCLearn.Core.IO;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Repositories;

namespace VRCLearn.Core.Packages;

/// <summary>远端包下载 zip 后解压，本地包整目录复制；下载的 zip 缓存在传入的根目录下，附一份 SHA-256。</summary>
/// <param name="http">为 null 表示离线：本地包照装，远端包直接报错。</param>
public sealed class PackageInstaller(IFileIo environment, IVpmHttpClient? http) : IPackageInstaller
{
    private const int Sha256HexLength = 64;

    private const int BufferSize = 81920;

    public async Task InstallPackageAsync(
        IFileIo project,
        PackageInfo package,
        string destinationRelativePath,
        CancellationToken cancellationToken)
    {
        if (!PathUtil.IsSafePathSegment(package.Name))
        {
            throw new InvalidDataException($"包名不能用作目录名：{package.Name}");
        }

        if (package.Repository is { } repository)
        {
            await InstallRemoteAsync(project, repository, package, destinationRelativePath, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await CopyLocalAsync(project, package, destinationRelativePath, cancellationToken).ConfigureAwait(false);
    }

    private async Task InstallRemoteAsync(
        IFileIo project,
        LocalCachedRepository repository,
        PackageInfo package,
        string destination,
        CancellationToken cancellationToken)
    {
        var zipPath = await EnsureZipAsync(repository, package, cancellationToken).ConfigureAwait(false);

        try
        {
            await using var zip = await environment
                .OpenReadOrNullAsync(zipPath, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException($"缓存里的 {zipPath} 读不出来了");

            await SafeZipExtractor.ExtractAsync(zip, project, destination, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            project.DeleteDirectory(destination);
            throw;
        }
    }

    /// <returns>缓存里那份 zip 的相对路径。</returns>
    private async Task<string> EnsureZipAsync(
        LocalCachedRepository repository,
        PackageInfo package,
        CancellationToken cancellationToken)
    {
        var fileName = $"vrc-get-{package.Name}-{package.Version}.zip";
        var zipPath = $"{package.Name}/{fileName}";
        var declared = ParseSha256(package.Manifest.ZipSha256);

        if (!await IsCachedAsync(zipPath, declared, cancellationToken).ConfigureAwait(false))
        {
            if (package.Manifest.Url is not { } url)
            {
                throw new InvalidDataException($"仓库里的 {package.Name}@{package.Version} 没有 url 字段，无法下载");
            }

            if (http is null)
            {
                throw new InvalidOperationException($"离线模式下无法下载 {package.Name}@{package.Version}，本地也没有可用的缓存");
            }

            var hash = await DownloadAsync(
                url,
                MergeHeaders(repository.Headers, package.Manifest.Headers),
                zipPath,
                cancellationToken).ConfigureAwait(false);

            if (declared is { } expected && !expected.AsSpan().SequenceEqual(hash))
            {
                environment.DeleteFile(zipPath);

                throw new InvalidDataException(
                    $"下载到的 {package.Name}@{package.Version} 的 SHA-256 与仓库声明的不一致。"
                    + "可能是仓库维护者写错了，也可能这个仓库或包被篡改过。");
            }

            await environment
                .WriteAllBytesAsync($"{zipPath}.sha256", Encoding.ASCII.GetBytes(Sidecar(hash, fileName)), cancellationToken)
                .ConfigureAwait(false);
        }

        return zipPath;
    }

    private async Task<byte[]> DownloadAsync(
        Uri url,
        IReadOnlyDictionary<string, string> headers,
        string zipPath,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        try
        {
            await using var response = await http!.GetAsync(url, headers, cancellationToken).ConfigureAwait(false);
            await using var destination = await environment.CreateAsync(zipPath, cancellationToken).ConfigureAwait(false);

            var buffer = new byte[BufferSize];

            while (true)
            {
                var read = await response.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            environment.DeleteFile(zipPath);
            throw;
        }

        return hash.GetHashAndReset();
    }

    private async Task<bool> IsCachedAsync(string zipPath, byte[]? declared, CancellationToken cancellationToken)
    {
        var stored = ParseSha256(await ReadSidecarAsync($"{zipPath}.sha256", cancellationToken).ConfigureAwait(false));

        if (stored is null || (declared is { } expected && !expected.AsSpan().SequenceEqual(stored)))
        {
            return false;
        }

        await using var zip = await environment
            .OpenReadOrNullAsync(zipPath, cancellationToken)
            .ConfigureAwait(false);

        return zip is not null
            && (await SHA256.HashDataAsync(zip, cancellationToken).ConfigureAwait(false)).AsSpan().SequenceEqual(stored);
    }

    private async Task<string?> ReadSidecarAsync(string shaPath, CancellationToken cancellationToken)
    {
        var bytes = await environment.ReadAllBytesOrNullAsync(shaPath, cancellationToken).ConfigureAwait(false);

        return bytes is { Length: >= Sha256HexLength }
            ? Encoding.ASCII.GetString(bytes, 0, Sha256HexLength)
            : null;
    }

    private static string Sidecar(byte[] hash, string fileName) =>
        $"{Sha256Hex.FromDigest(hash)} {fileName}\n";

    private static byte[]? ParseSha256(string? hex)
    {
        if (hex is not { Length: Sha256HexLength })
        {
            return null;
        }

        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> MergeHeaders(
        IReadOnlyDictionary<string, string> repositoryHeaders,
        IReadOnlyDictionary<string, string> packageHeaders)
    {
        var merged = new Dictionary<string, string>(repositoryHeaders);

        foreach (var (name, value) in packageHeaders)
        {
            merged[name] = value;
        }

        return merged;
    }

    private static async Task CopyLocalAsync(
        IFileIo project,
        PackageInfo package,
        string destination,
        CancellationToken cancellationToken)
    {
        var source = package.LocalPath!;
        project.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            project.CreateDirectory($"{destination}/{Relative(source, directory)}");
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            await using var input = File.OpenRead(file);
            await using var output = await project
                .CreateAsync($"{destination}/{Relative(source, file)}", cancellationToken)
                .ConfigureAwait(false);

            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string Relative(string source, string path) =>
        Path.GetRelativePath(source, path).Replace(Path.DirectorySeparatorChar, '/');
}
