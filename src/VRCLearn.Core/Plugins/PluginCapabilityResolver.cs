using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Plugins;

public sealed record PluginResolutionNode(
    string Id,
    PackageVersion Version,
    bool Enabled,
    IReadOnlyList<PluginCapability> Provides,
    IReadOnlyList<PluginRequirement> Requires);

public sealed record PluginResolution(
    IReadOnlyList<string> LoadOrder,
    IReadOnlyDictionary<string, string> Providers,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Dependencies,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Missing,
    IReadOnlyList<string> Cycles);

public static class PluginCapabilityResolver
{
    public static PluginResolution Resolve(IEnumerable<PluginResolutionNode> nodes)
    {
        var enabled = nodes
            .Where(x => x.Enabled)
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

        var byId = enabled.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var providers = new Dictionary<string, string>(StringComparer.Ordinal);
        var missing = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var dependencies = enabled.ToDictionary(
            x => x.Id,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        foreach (var node in enabled)
        {
            var missingForNode = new List<string>();

            foreach (var requirement in node.Requires)
            {
                var provider = enabled
                    .Where(x => x.Id != node.Id)
                    .SelectMany(x => x.Provides
                        .Where(capability => capability.Capability == requirement.Capability
                            && capability.Version >= requirement.MinVersion)
                        .Select(capability => (Node: x, capability.Version)))
                    .OrderByDescending(x => x.Version)
                    .ThenBy(x => x.Node.Id, StringComparer.Ordinal)
                    .FirstOrDefault();

                if (provider.Node is null)
                {
                    missingForNode.Add($"{requirement.Capability} >= {requirement.MinVersion}");
                    continue;
                }

                providers.TryAdd(requirement.Capability, provider.Node.Id);
                dependencies[node.Id].Add(provider.Node.Id);
            }

            if (missingForNode.Count > 0)
            {
                missing[node.Id] = missingForNode;
            }
        }

        var dependents = enabled.ToDictionary(
            x => x.Id,
            _ => new List<string>(),
            StringComparer.Ordinal);

        foreach (var (consumer, providerIds) in dependencies)
        {
            foreach (var providerId in providerIds)
            {
                dependents[providerId].Add(consumer);
            }
        }

        var missingQueue = new Queue<string>(missing.Keys);

        while (missingQueue.Count > 0)
        {
            var id = missingQueue.Dequeue();

            foreach (var dependent in dependents[id])
            {
                if (missing.ContainsKey(dependent))
                {
                    continue;
                }

                missing[dependent] = missing[id];
                missingQueue.Enqueue(dependent);
            }
        }

        var indegree = dependencies.ToDictionary(
            x => x.Key,
            x => x.Value.Count,
            StringComparer.Ordinal);

        var ready = new SortedSet<string>(
            enabled.Where(x => indegree[x.Id] == 0 && !missing.ContainsKey(x.Id)).Select(x => x.Id),
            StringComparer.Ordinal);

        var ordered = new List<string>();

        while (ready.Count > 0)
        {
            var id = ready.Min!;
            ready.Remove(id);
            ordered.Add(id);

            foreach (var dependent in dependents[id])
            {
                if (missing.ContainsKey(dependent))
                {
                    continue;
                }

                indegree[dependent]--;

                if (indegree[dependent] == 0)
                {
                    ready.Add(dependent);
                }
            }
        }

        var cycles = enabled
            .Where(x => !missing.ContainsKey(x.Id) && !ordered.Contains(x.Id, StringComparer.Ordinal))
            .Select(x => x.Id)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var dependenciesById = dependencies.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<string>)[.. x.Value.OrderBy(value => value, StringComparer.Ordinal)],
            StringComparer.Ordinal);

        return new PluginResolution(ordered, providers, dependenciesById, missing, cycles);
    }
}
