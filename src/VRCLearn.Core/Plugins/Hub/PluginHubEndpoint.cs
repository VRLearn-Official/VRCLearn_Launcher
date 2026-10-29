namespace VRCLearn.Core.Plugins.Hub;

public static class PluginHubEndpoint
{
    public const int MaxLength = 2048;

    private const string IndexFileName = "index.json";

    public static bool TryNormalize(string? hub, out string normalized)
    {
        normalized = "";

        if (string.IsNullOrWhiteSpace(hub)
            || hub.Length > MaxLength
            || !Uri.TryCreate(hub.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.Host.Length == 0
            || uri.UserInfo.Length > 0
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0)
        {
            return false;
        }

        var builder = new UriBuilder(uri)
        {
            Query = "",
            Fragment = "",
        };

        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        normalized = builder.Uri.AbsoluteUri;

        return normalized.Length <= MaxLength;
    }

    public static Uri IndexUri(string normalizedHub) => new(new Uri(normalizedHub), IndexFileName);

    public static bool TryResolvePackage(string normalizedHub, string? url, out Uri package)
    {
        package = null!;

        if (string.IsNullOrWhiteSpace(url)
            || url.Length > MaxLength
            || !Uri.TryCreate(new Uri(normalizedHub), url.Trim(), out var candidate)
            || candidate.Scheme != Uri.UriSchemeHttps
            || candidate.UserInfo.Length > 0
            || candidate.Fragment.Length > 0)
        {
            return false;
        }

        var hub = new Uri(normalizedHub);

        if (!string.Equals(candidate.Host, hub.Host, StringComparison.OrdinalIgnoreCase)
            || candidate.Port != hub.Port
            || !candidate.AbsolutePath.StartsWith(hub.AbsolutePath, StringComparison.Ordinal))
        {
            return false;
        }

        package = candidate;

        return true;
    }
}
