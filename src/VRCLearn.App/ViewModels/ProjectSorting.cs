using VRCLearn.Core.Projects;

namespace VRCLearn.App.ViewModels;

public static class ProjectSorting
{
    public static readonly string[] Keys = ["name", "type", "unity", "createdAt", "lastModified"];

    public static string Normalize(string? value) =>
        value is not null && IsValid(value) ? value : "lastModified";

    public static bool IsValid(string value) =>
        Keys.Contains(Strip(value), StringComparer.Ordinal) && value == value.Trim();

    public static bool IsReversed(string value) => Strip(value) != value;

    public static string KeyOf(string value) => Strip(value);

    public static string WithDirection(string key, bool reversed) => reversed ? $"{key}Reversed" : key;

    public static IEnumerable<ProjectItemViewModel> Apply(IEnumerable<ProjectItemViewModel> projects, string sorting)
    {
        var key = KeyOf(sorting);
        var ordered = key switch
        {
            "name" => projects.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            "type" => projects.OrderBy(x => ProjectTypeRank.Of(x.ProjectType)).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            "unity" => projects.OrderByDescending(x => x.UnityVersion, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),

            "createdAt" => projects.OrderBy(x => x.CreatedAt).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            _ => projects.OrderByDescending(x => x.LastModified).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
        };

        return IsReversed(sorting) ? ordered.Reverse() : ordered;
    }

    private static string Strip(string value) => value.EndsWith("Reversed", StringComparison.Ordinal)
        ? value[..^"Reversed".Length]
        : value;
}
