namespace VRCLearn.Core.Resources;

public static class ResourceOperations
{
    public static bool AddTag(ResourceCollection collection, string tag)
    {
        var name = tag.Trim();

        if (name.Length == 0
            || collection.Tags.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        collection.Tags.Add(name);

        return true;
    }

    public static void RemoveTag(ResourceCollection collection, string tag)
    {
        var name = tag.Trim();

        collection.Tags.RemoveAll(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));

        foreach (var resource in collection.Resources)
        {
            resource.Tags.RemoveAll(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static bool IsExpired(ResourceDefinition resource, Func<string, bool> fileExists) =>
        resource.UnityPackagePaths.All(path => !fileExists(path));

    public static IReadOnlyList<ResourceDefinition> Expired(
        IEnumerable<ResourceDefinition> resources,
        Func<string, bool> fileExists) =>
        [.. resources.Where(x => IsExpired(x, fileExists))];

    public static IReadOnlyList<ResourceDefinition> RemoveExpired(
        ResourceCollection collection,
        Func<string, bool> fileExists)
    {
        var expired = Expired(collection.Resources, fileExists);
        var ids = expired.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        collection.Resources.RemoveAll(x => ids.Contains(x.Id));

        return expired;
    }
}
