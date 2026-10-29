using System.Text;

namespace VRCLearn.Core.Repositories;

/// <summary>带回车头的仓库写成 <c>vcc://vpm/addRepo</c> 深链。</summary>
public static class RepositoriesFileWriter
{
    public static string Write(IEnumerable<UserRepoSetting> repositories)
    {
        var builder = new StringBuilder();

        foreach (var repository in repositories)
        {
            if (repository.Url is not { } url)
            {
                continue;
            }

            if (repository.Headers.Count == 0)
            {
                builder.Append(url.AbsoluteUri).Append('\n');
                continue;
            }

            builder.Append("vcc://vpm/addRepo?url=").Append(Uri.EscapeDataString(url.AbsoluteUri));

            foreach (var (name, value) in repository.Headers)
            {
                builder.Append("&headers[]=").Append(Uri.EscapeDataString($"{name}:{value}"));
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }
}
