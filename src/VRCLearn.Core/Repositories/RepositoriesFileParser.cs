namespace VRCLearn.Core.Repositories;

public sealed record RepositoryInfo(Uri Url, IReadOnlyDictionary<string, string> Headers);

public sealed record RepositoriesFileParseResult(
    IReadOnlyList<RepositoryInfo> Repositories,
    IReadOnlyList<string> UnparseableLines);

/// <summary>一行一个 URL，<c>#</c> 后是注释，支持 <c>http</c> / <c>https</c> / <c>vcc</c> 三种 scheme。</summary>
public static class RepositoriesFileParser
{
    public static RepositoriesFileParseResult Parse(string content)
    {
        var repositories = new List<RepositoryInfo>();
        var unparseable = new List<string>();

        foreach (var rawLine in content.Split('\n'))
        {
            var comment = rawLine.IndexOf('#');
            var line = (comment < 0 ? rawLine : rawLine[..comment]).Trim();

            if (line.Length == 0)
            {
                continue;
            }

            var repository = Uri.TryCreate(line, UriKind.Absolute, out var uri)
                ? FromUri(uri)
                : null;

            if (repository is null)
            {
                unparseable.Add(line);
            }
            else
            {
                repositories.Add(repository);
            }
        }

        return new RepositoriesFileParseResult(repositories, unparseable);
    }

    /// <summary>解析 <c>vcc://vpm/addRepo?url=...&amp;headers[]=k:v</c> 深链。</summary>
    public static RepositoryInfo? ParseDeepLink(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Scheme
            ? FromVccLink(uri)
            : null;

    private const string Scheme = "vcc";

    private static RepositoryInfo? FromUri(Uri uri) => uri.Scheme switch
    {
        "http" or "https" => new RepositoryInfo(uri, new Dictionary<string, string>()),
        Scheme => FromVccLink(uri),
        _ => null,
    };

    private static RepositoryInfo? FromVccLink(Uri uri)
    {
        if (!string.Equals(uri.Host, "vpm", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath != "/addRepo")
        {
            return null;
        }

        Uri? target = null;
        var headers = new Dictionary<string, string>();

        foreach (var (key, value) in ParseQuery(uri.Query))
        {
            switch (key)
            {
                case "url":
                    if (target is not null
                        || !Uri.TryCreate(value, UriKind.Absolute, out var parsed)
                        || parsed.Scheme is not ("http" or "https"))
                    {
                        return null;
                    }

                    target = parsed;
                    break;

                case "headers[]":
                    var separator = value.IndexOf(':');
                    if (separator < 0)
                    {
                        return null;
                    }

                    headers[value[..separator]] = value[(separator + 1)..];
                    break;
            }
        }

        return target is null ? null : new RepositoryInfo(target, headers);
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string query)
    {
        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            if (pair.Length == 0)
            {
                continue;
            }

            var separator = pair.IndexOf('=');
            yield return separator < 0
                ? new KeyValuePair<string, string>(Decode(pair), "")
                : new KeyValuePair<string, string>(Decode(pair[..separator]), Decode(pair[(separator + 1)..]));
        }
    }

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
}
