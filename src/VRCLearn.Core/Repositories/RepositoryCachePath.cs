using System.Text;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Hashing;

namespace VRCLearn.Core.Repositories;

/// <summary>相对数据目录的路径。</summary>
public static class RepositoryCachePath
{
    public static string For(string? repositoryId, Uri url) =>
        $"{AppPaths.RepositoriesFolderName}/{(PathUtil.IsSafeFileName(repositoryId) ? repositoryId : Hash(url))}.json";

    private static string Hash(Uri url) =>
        Sha256Hex.Of(Encoding.UTF8.GetBytes(url.AbsoluteUri))[..16];
}
