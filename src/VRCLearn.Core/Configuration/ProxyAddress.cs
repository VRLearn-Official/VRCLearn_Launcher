namespace VRCLearn.Core.Configuration;

public static class ProxyAddress
{
    public const string DefaultScheme = "http";

    public static IReadOnlyList<string> Schemes { get; } = ["http", "https", "socks4", "socks5"];

    public static (string Scheme, string Host, string Port) Split(string url)
    {
        var text = url.Trim();

        if (text.Length == 0)
        {
            return (DefaultScheme, "", "");
        }

        var separator = text.IndexOf("://", StringComparison.Ordinal);
        var scheme = DefaultScheme;
        var rest = text;

        if (separator >= 0)
        {
            scheme = Schemes.FirstOrDefault(x => x.Equals(text[..separator], StringComparison.OrdinalIgnoreCase))
                ?? DefaultScheme;
            rest = text[(separator + 3)..];
        }

        var colon = rest.LastIndexOf(':');

        if (colon < 0)
        {
            return (scheme, rest, "");
        }

        var port = rest[(colon + 1)..];

        if (port.Length > 0 && port.All(char.IsAsciiDigit))
        {
            return IsPort(port) ? (scheme, rest[..colon], port) : (scheme, rest[..colon], "");
        }

        return (scheme, rest.TrimEnd(':'), "");
    }

    public static string Compose(string scheme, string host, string port)
    {
        var name = host.Trim();

        if (name.Length == 0)
        {
            return "";
        }

        var address = $"{scheme}://{name}";
        var number = port.Trim();

        return IsPort(number) ? $"{address}:{number}" : address;
    }

    private static bool IsPort(string text) =>
        text.All(char.IsAsciiDigit)
        && int.TryParse(text, out var number)
        && number is > 0 and <= 65535;
}
