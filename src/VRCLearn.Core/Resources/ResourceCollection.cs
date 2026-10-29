using System.Text.Json.Serialization;

namespace VRCLearn.Core.Resources;

public sealed class ResourceCollection : IJsonOnDeserialized
{
    public List<string> Tags { get; set; } = [];

    public List<ResourceDefinition> Resources { get; set; } = [];

    public void OnDeserialized()
    {
        Tags ??= [];
        Resources ??= [];

        Resources.RemoveAll(x => x is null || string.IsNullOrWhiteSpace(x.Id));
    }
}
