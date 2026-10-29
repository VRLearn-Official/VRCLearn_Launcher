using System.Text;
using VRCLearn.Core.IO;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Core.Templates;

public static class ProjectCreator
{
    private static readonly string[] BlankDirectories = ["Assets", "Packages", "ProjectSettings"];

    public static async Task<UnityProject> CreateAsync(
        IFileIo destination,
        IFileIoFactory fileIo,
        TemplatePlan plan,
        UnityVersion unityVersion,
        string projectName,
        CancellationToken cancellationToken)
    {
        if (plan.Source is { } source)
        {
            await CopyAsync(source, destination, "", cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await CreateBlankAsync(destination, unityVersion, cancellationToken).ConfigureAwait(false);
        }

        foreach (var path in plan.UnityPackages)
        {
            await ImportUnityPackageAsync(destination, fileIo, path, cancellationToken).ConfigureAwait(false);
        }

        await UpdateNameAndGuidAsync(destination, projectName, cancellationToken).ConfigureAwait(false);

        var project = await UnityProject.LoadAsync(destination, cancellationToken).ConfigureAwait(false);

        foreach (var (name, range) in plan.Dependencies)
        {
            project.AddDependencyRaw(name, range);
        }

        if (plan.Dependencies.Count > 0)
        {
            await project.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        return project;
    }

    private static async Task ImportUnityPackageAsync(
        IFileIo destination,
        IFileIoFactory fileIo,
        string path,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);

        var bytes = directory is null or ""
            ? null
            : await fileIo
                .For(directory)
                .ReadAllBytesOrNullAsync(Path.GetFileName(path), cancellationToken)
                .ConfigureAwait(false);

        if (bytes is null)
        {
            throw new FileNotFoundException($"模板要导入的 {path} 找不到了", path);
        }

        await UnityPackageImporter
            .ImportAsync(destination, new MemoryStream(bytes), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task CreateBlankAsync(
        IFileIo destination,
        UnityVersion unityVersion,
        CancellationToken cancellationToken)
    {
        foreach (var directory in BlankDirectories)
        {
            destination.CreateDirectory(directory);
        }

        await destination
            .WriteAllBytesAsync(
                UnityProject.ProjectVersionPath,
                Encoding.UTF8.GetBytes($"m_EditorVersion: {unityVersion}\n"),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task CopyAsync(
        IFileIo source,
        IFileIo destination,
        string relative,
        CancellationToken cancellationToken)
    {
        foreach (var entry in source.ListEntries(relative))
        {
            var path = relative.Length == 0 ? entry.Name : $"{relative}/{entry.Name}";

            if (entry.IsDirectory)
            {
                destination.CreateDirectory(path);
                await CopyAsync(source, destination, path, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (await source.ReadAllBytesOrNullAsync(path, cancellationToken).ConfigureAwait(false) is { } bytes)
            {
                await destination.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task UpdateNameAndGuidAsync(
        IFileIo io,
        string projectName,
        CancellationToken cancellationToken)
    {
        if (await io.ReadAllBytesOrNullAsync(UnityProject.ProjectSettingsAsset, cancellationToken).ConfigureAwait(false)
            is not { } bytes)
        {
            return;
        }

        var content = Encoding.UTF8.GetString(bytes);

        var changed = SetValue(ref content, "productGUID: ", Guid.NewGuid().ToString("N"));
        changed |= SetValue(ref content, "productName: ", YamlQuote(projectName));

        if (changed)
        {
            await io.WriteAllBytesAsync(UnityProject.ProjectSettingsAsset, Encoding.UTF8.GetBytes(content), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>只在前面是空白的位置匹配键名。</summary>
    private static bool SetValue(ref string content, string key, string value)
    {
        var position = content.IndexOf(key, StringComparison.Ordinal);

        if (position < 0 || (position > 0 && !char.IsWhiteSpace(content[position - 1])))
        {
            return false;
        }

        var start = position + key.Length;
        var lineEnd = content.IndexOf('\n', start);

        if (lineEnd < start)
        {
            return false;
        }

        var valueEnd = content[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;

        content = string.Concat(content.AsSpan(0, start), value, content.AsSpan(valueEnd));
        return true;
    }

    private static string YamlQuote(string value) =>
        $"\"{value.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r")}\"";
}
