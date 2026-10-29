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

            if (segment.Length >= 2 && segment[1] == ':')
            {
                return null;
            }

            if (!PathUtil.IsSafePathSegment(segment, allowColon: true))
            {
                return null;
            }

            segments.Add(segment);
        }

        return segments.Count == 0 ? null : string.Join('/', segments);
    }
}
