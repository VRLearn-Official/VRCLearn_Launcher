using System.Net;

namespace VRCLearn.Infrastructure.Http;

public sealed class MutableProxy : IWebProxy
{
    private Uri? _proxy;

    public void Configure(bool enabled, string? address) =>
        _proxy = enabled && Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri) ? uri : null;

    public Uri GetProxy(Uri destination) => _proxy ?? destination;

    public bool IsBypassed(Uri host) => _proxy is null;

    public ICredentials? Credentials { get; set; }
}
