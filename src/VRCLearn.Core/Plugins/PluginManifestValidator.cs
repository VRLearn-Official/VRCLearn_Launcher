using System.Text.RegularExpressions;
using VRCLearn.Core.Archives;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Plugins;

public static partial class PluginManifestValidator
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[a-z][a-z0-9.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex CapabilityPattern();

    public static void Validate(PluginManifest manifest)
    {
        if (manifest.SchemaVersion != 1)
        {
            throw new PluginManifestException($"unsupported schemaVersion: {manifest.SchemaVersion}");
        }

        if (!IdPattern().IsMatch(manifest.Id)
            || !PathUtil.IsSafePathSegment(manifest.Id)
            || manifest.Id.Length > 64)
        {
            throw new PluginManifestException($"invalid plugin id: {manifest.Id}");
        }

        ValidateText(manifest.Name, "name", required: true);
        ValidateText(manifest.Description, "description", required: false);

        ParseVersion(manifest.Version.ToString(), "version");

        if (manifest.Author.Length > 128)
        {
            throw new PluginManifestException("author must be at most 128 characters");
        }

        if (manifest.ApiVersion <= 0)
        {
            throw new PluginManifestException("apiVersion must be positive");
        }

        ValidateRelativePath(manifest.Entry.Assembly, "entry.assembly", ".dll");
        ValidateTypeName(manifest.Entry.Type);

        if (manifest.Icon is { Length: > 0 })
        {
            ValidateRelativePath(
                manifest.Icon,
                "icon",
                ".png",
                ".jpg",
                ".jpeg",
                ".bmp",
                ".ico");
        }

        if (manifest.Window.Width is < 320 or > 2000 || manifest.Window.Height is < 240 or > 1600)
        {
            throw new PluginManifestException("window size is outside 320-2000 x 240-1600");
        }

        var provides = new HashSet<string>(StringComparer.Ordinal);

        foreach (var capability in manifest.Provides)
        {
            ValidateCapability(capability.Capability, capability.Version);

            if (!provides.Add(capability.Capability))
            {
                throw new PluginManifestException($"duplicate provided capability: {capability.Capability}");
            }
        }

        var requires = new HashSet<string>(StringComparer.Ordinal);

        foreach (var requirement in manifest.Requires)
        {
            ValidateCapability(requirement.Capability, requirement.MinVersion);

            if (!requires.Add(requirement.Capability))
            {
                throw new PluginManifestException($"duplicate required capability: {requirement.Capability}");
            }
        }
    }

    public static PackageVersion ParseVersion(string text, string fieldName)
    {
        if (!VersionPattern().IsMatch(text))
        {
            throw new PluginManifestException($"{fieldName} must be major.minor.patch: {text}");
        }

        try
        {
            return PackageVersion.Parse(text);
        }
        catch (VersionParseException exception)
        {
            throw new PluginManifestException($"{fieldName} is invalid: {text}", exception);
        }
    }

    private static void ValidateText(PluginLocalizedText text, string fieldName, bool required)
    {
        if (text.IsEmpty)
        {
            if (required)
            {
                throw new PluginManifestException($"{fieldName} is required");
            }

            return;
        }

        foreach (var pair in text.Values)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new PluginManifestException($"{fieldName} contains an empty language code");
            }

            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                throw new PluginManifestException($"{fieldName}.{pair.Key} is empty");
            }
        }
    }

    private static void ValidateRelativePath(string path, string fieldName, params string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains(':'))
        {
            throw new PluginManifestException($"{fieldName} must be a relative path");
        }

        var normalized = ZipEntryPath.Normalize(path);

        if (normalized is null || normalized != path.Replace('\\', '/'))
        {
            throw new PluginManifestException($"{fieldName} is not a safe relative path: {path}");
        }

        if (extensions.Length > 0
            && !extensions.Contains(Path.GetExtension(normalized), StringComparer.OrdinalIgnoreCase))
        {
            throw new PluginManifestException($"{fieldName} has an unsupported extension: {path}");
        }
    }

    private static void ValidateTypeName(string type)
    {
        if (string.IsNullOrWhiteSpace(type)
            || type.Length > 256
            || type.Contains(' ')
            || !type.Contains('.'))
        {
            throw new PluginManifestException($"invalid entry.type: {type}");
        }
    }

    private static void ValidateCapability(string capability, int version)
    {
        if (!CapabilityPattern().IsMatch(capability))
        {
            throw new PluginManifestException($"invalid capability name: {capability}");
        }

        if (version <= 0)
        {
            throw new PluginManifestException($"capability {capability} must have a positive version");
        }
    }
}
