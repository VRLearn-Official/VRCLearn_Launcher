using System.Text.Json.Nodes;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Templates;

namespace VRCLearn.Infrastructure.ProjectTemplates;

/// <summary>模板目录里 ALCOM 格式的旧文件：复制一份改成自家格式，原文件留给 ALCOM。</summary>
public static class TemplateCompat
{
    public static IReadOnlyList<string> Find(string templatesDirectory) =>
        Directory.Exists(templatesDirectory)
            ? Directory.EnumerateFiles(templatesDirectory, $"*{TemplateFormat.LegacyExtension}").ToList()
            : [];

    public static IReadOnlyList<string> CopyAndUpgrade(string templatesDirectory)
    {
        var upgraded = new List<string>();

        foreach (var file in Find(templatesDirectory))
        {
            var target = Path.ChangeExtension(file, TemplateFormat.Extension);

            try
            {
                if (File.Exists(target))
                {
                    continue;
                }

                File.WriteAllText(target, UpgradeContent(File.ReadAllText(file)));

                upgraded.Add(Path.GetFileName(target));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return upgraded;
    }

    private static string UpgradeContent(string content)
    {
        if (JsonNode.Parse(content) is not JsonObject root)
        {
            return content;
        }

        root["$type"] = TemplateFormat.Magic;

        if (root["base"] is JsonValue value
            && value.TryGetValue<string>(out var baseId)
            && baseId.StartsWith(TemplateFormat.LegacyNamespace, StringComparison.Ordinal))
        {
            root["base"] = TemplateFormat.BlankId;
        }

        return root.ToJsonString(JsonDefaults.Indented);
    }
}
