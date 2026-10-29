using System.Net;
using System.Net.Sockets;

namespace VRCLearn.Core.Resources;

public static class ResourceManual
{
    private static readonly string[] DocumentExtensions =
    [
        ".pdf",
        ".md",
        ".txt",
        ".doc",
        ".docx",
        ".rtf",
        ".odt",
        ".html",
        ".htm",
    ];

    public static bool IsSupportedFile(string path) =>
        DocumentExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsUrl(string value)
    {
        var text = value.Trim();

        if (text.Length == 0)
        {
            return false;
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }

        return IsIpLink(text);
    }

    public static string ToOpenableUrl(string value)
    {
        var text = value.Trim();

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return text;
        }

        return IPAddress.TryParse(text, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"http://[{text}]"
            : $"http://{text}";
    }

    public static bool IsMissingFile(string value)
    {
        var text = value.Trim();

        return text.Length > 0 && !IsUrl(text) && !File.Exists(text);
    }

    private static bool IsIpLink(string text)
    {
        if (IPAddress.TryParse(text.Trim('[', ']'), out _))
        {
            return true;
        }

        return Uri.TryCreate($"http://{text}", UriKind.Absolute, out var uri)
            && IPAddress.TryParse(uri.Host.Trim('[', ']'), out _);
    }
}
