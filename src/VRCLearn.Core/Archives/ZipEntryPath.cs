using VRCLearn.Core.Configuration;

namespace VRCLearn.Core.Archives;

public static class ZipEntryPath
{
    public static string? Normalize(string entryName)
    {
        if (entryName.StartsWith('/') || entryName.StartsWith('\\'))
        {
            return null;
        }

        var segments = new List<string>();

        foreach (var segment in entryName.Split('/', '\\'))
        {
            if (segment is "" or ".")
            {
                continue;
            }

            if (!PathUtil.IsSafePathSegment(segment))
            {
                return null;
            }

            segments.Add(segment);
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }
}
