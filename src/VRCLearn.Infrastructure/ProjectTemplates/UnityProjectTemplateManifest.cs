using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using VRCLearn.Core.Templates;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Infrastructure.ProjectTemplates;

public sealed class UnityProjectTemplateManifest(ILogger<UnityProjectTemplateManifest> logger)
    : IUnityProjectTemplateManifest
{
    private const string TemplatePattern = "com.unity.template.3d-*.tgz";

    private const string ManifestEntry = "package/ProjectData~/Packages/manifest.json";

    private const long MaxManifestBytes = 1024 * 1024;

    public async Task<byte[]?> FindAsync(
        UnityVersion unityVersion,
        string? editorExecutablePath,
        CancellationToken cancellationToken)
    {
        if (unityVersion.Major == 2022 && unityVersion.Minor == 3)
        {
            return Encoding.UTF8.GetBytes(DefaultUpmManifest.Unity2022);
        }

        if (unityVersion.Major != 2019
            || unityVersion.Minor != 4
            || string.IsNullOrWhiteSpace(editorExecutablePath)
            || Path.GetDirectoryName(editorExecutablePath) is not { Length: > 0 } editorDirectory)
        {
            return null;
        }

        var templates = Path.Combine(
            editorDirectory,
            "Data",
            "Resources",
            "PackageManager",
            "ProjectTemplates");

        if (!Directory.Exists(templates))
        {
            return null;
        }

        List<string> candidates;

        try
        {
            candidates = Directory
                .EnumerateFiles(templates, TemplatePattern)
                .OrderByDescending(TemplateVersion)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "枚举 Unity 项目模板失败：{Directory}", templates);

            return null;
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = File.OpenRead(candidate);

                if (await TarGzEntryReader
                        .ReadEntryAsync(stream, ManifestEntry, MaxManifestBytes, cancellationToken)
                        .ConfigureAwait(false) is not { } manifest
                    || !IsUsable(manifest))
                {
                    logger.LogWarning("Unity 项目模板里没有可用的 manifest：{Template}", candidate);

                    continue;
                }

                return manifest;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException)
            {
                logger.LogWarning(exception, "读取 Unity 项目模板失败：{Template}", candidate);
            }
        }

        return null;
    }

    private static Version TemplateVersion(string path) =>
        System.Version.TryParse(
            Path.GetFileNameWithoutExtension(path)["com.unity.template.3d-".Length..],
            out var version)
            ? version
            : new Version(0, 0);

    private static bool IsUsable(byte[] manifest)
    {
        try
        {
            return JsonNode.Parse(Encoding.UTF8.GetString(manifest)) is JsonObject root
                && root["dependencies"] is JsonObject dependencies
                && dependencies.Count > 0
                && (dependencies.ContainsKey("com.unity.test-framework")
                    || dependencies.ContainsKey("com.unity.feature.development"));
        }
        catch (Exception exception) when (exception is ArgumentException or System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
