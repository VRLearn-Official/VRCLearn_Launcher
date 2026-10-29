using System.Text.Json.Serialization;

namespace VRCLearn.Infrastructure.Plugins;

public sealed class PluginState : IJsonOnDeserialized
{
    public List<string> Enabled { get; set; } = [];

    public List<string> PendingRemovals { get; set; } = [];

    public Dictionary<string, string> PendingInstalls { get; set; } = new(StringComparer.Ordinal);

    public void OnDeserialized()
    {
        Enabled = Normalize(Enabled);
        PendingRemovals = Normalize(PendingRemovals);
        PendingInstalls = new Dictionary<string, string>(
            PendingInstalls ?? [],
            StringComparer.Ordinal);
    }

    private static List<string> Normalize(List<string>? values) =>
        [.. (values ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal)];
}
