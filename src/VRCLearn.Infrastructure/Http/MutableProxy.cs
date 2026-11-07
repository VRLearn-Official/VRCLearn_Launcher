using System.Net;

namespace VRCLearn.Infrastructure.Http;

public sealed class MutableProxy : IWebProxy
{
    private Uri? _proxy;

    public bool IsConfigured => _proxy is not null;

    public event EventHandler? AvailabilityChanged;

    public void Configure(bool enabled, string? address)
    {
        var wasConfigured = IsConfigured;
        _proxy = enabled && Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri) ? uri : null;

        if (wasConfigured != IsConfigured)
        {
            AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Uri GetProxy(Uri destination) => _proxy ?? destination;

    public bool IsBypassed(Uri host) => _proxy is null;

    public ICredentials? Credentials { get; set; }
}
