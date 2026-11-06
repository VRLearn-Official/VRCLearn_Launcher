namespace VRCLearn.Core.Repositories;

public static class RepositoryHeaders
{
    public static Dictionary<string, string> Clean(IEnumerable<KeyValuePair<string, string>> headers) =>
        headers
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
}
