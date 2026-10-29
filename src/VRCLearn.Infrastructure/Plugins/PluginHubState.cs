namespace VRCLearn.Infrastructure.Plugins;

public sealed class PluginHubState
{
    public List<PluginHubStateEntry> Hubs { get; set; } = [];
}

public sealed class PluginHubStateEntry
{
    public string Hub { get; set; } = "";

    public DateTimeOffset CheckedAt { get; set; }

    public string ETag { get; set; } = "";

    public bool Failed { get; set; }

    public string Error { get; set; } = "";

    public List<PluginHubCachedRelease> Releases { get; set; } = [];
}

public sealed class PluginHubCachedRelease
{
    public string Id { get; set; } = "";

    public string Version { get; set; } = "";

    public string Url { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public long Size { get; set; }

    public int ApiVersion { get; set; }

    public string MinHostVersion { get; set; } = "";
}
