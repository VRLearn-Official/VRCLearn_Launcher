using System.Text.RegularExpressions;

namespace VRCLearn.Core.Plugins;

public static class PluginSource
{
    private static readonly Regex GitHubLink = new(
        @"^https?://(?:www\.)?github\.com/([A-Za-z0-9][A-Za-z0-9-]{0,38})(?:[/?#]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>取 GitHub 链接里的账号名；不是 GitHub 链接时返回空串。</summary>
    public static string OwnerOf(string? author) =>
        TryMatch(author) is { } match ? match.Owner : "";

    /// <summary>返回可打开的 GitHub 链接；不是 GitHub 链接时返回空串。</summary>
    public static string LinkOf(string? author) =>
        TryMatch(author) is { } match ? match.Link : "";

    private static (string Owner, string Link)? TryMatch(string? author)
    {
        if (author is not { Length: > 0 })
        {
            return null;
        }

        var link = author.Trim();
        var match = GitHubLink.Match(link);

        return match.Success ? (match.Groups[1].Value, link) : null;
    }
}
