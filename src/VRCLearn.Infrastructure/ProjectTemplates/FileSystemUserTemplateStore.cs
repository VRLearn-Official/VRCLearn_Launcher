using Microsoft.Extensions.Logging;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Templates;

namespace VRCLearn.Infrastructure.ProjectTemplates;

/// <summary>用户模板路径：<c>&lt;共用目录&gt;/vrclearn/templates/</c>。</summary>
public sealed class FileSystemUserTemplateStore(
    AppPaths paths,
    ILogger<FileSystemUserTemplateStore> logger)
    : IUserTemplateStore
{
    private string Root => Path.Combine(paths.PrivateDirectory, "templates");

    public Task<UserTemplates> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new UserTemplates(ReadAll()));

    public async Task SaveAsync(AlcomTemplate template, CancellationToken cancellationToken)
    {
        var path = FindPathById(template.Id) ?? PathForNew(template.DisplayName);

        await File.WriteAllTextAsync(path, template.ToJson(), cancellationToken).ConfigureAwait(false);
    }

    public bool Remove(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    public async Task<TemplateImportResult> ImportAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        var existing = ReadAll();
        var imported = 0;
        var duplicates = new List<TemplateImportDuplicate>();

        foreach (var path in paths)
        {
            string content;

            try
            {
                content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not read {Path}", path);
                continue;
            }

            if (AlcomTemplate.TryParse(content) is not { } template)
            {
                logger.LogWarning("Skipping {Path}: not a template file", path);
                continue;
            }

            var clash = template.Id is { } id
                ? existing.FirstOrDefault(x => x.Template.Id == id)
                : null;

            if (clash is null)
            {
                await File
                    .WriteAllTextAsync(PathForNew(template.DisplayName), content, cancellationToken)
                    .ConfigureAwait(false);

                imported++;
                continue;
            }

            duplicates.Add(new TemplateImportDuplicate(
                clash.Template.Id!,
                clash.Path,
                clash.Template.DisplayName,
                template.DisplayName,
                content));
        }

        return new TemplateImportResult(imported, duplicates);
    }

    public async Task<int> OverrideAsync(
        IReadOnlyList<TemplateImportDuplicate> duplicates,
        CancellationToken cancellationToken)
    {
        var written = 0;

        foreach (var duplicate in duplicates)
        {
            try
            {
                await File
                    .WriteAllTextAsync(duplicate.ExistingPath, duplicate.Content, cancellationToken)
                    .ConfigureAwait(false);

                written++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not override template at {Path}", duplicate.ExistingPath);
            }
        }

        return written;
    }

    private IReadOnlyList<AlcomTemplateFile> ReadAll()
    {
        var templates = new List<AlcomTemplateFile>();

        if (!Directory.Exists(Root))
        {
            return templates;
        }

        foreach (var path in Directory.EnumerateFiles(Root, $"*{TemplateFormat.Extension}"))
        {
            AlcomTemplate? template;

            try
            {
                template = AlcomTemplate.TryParse(File.ReadAllText(path));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                template = null;
            }

            if (template is null)
            {
                logger.LogWarning("Skipping unreadable template {Path}", path);
                continue;
            }

            templates.Add(new AlcomTemplateFile(path, template));
        }

        return templates;
    }

    private string? FindPathById(string? id) =>
        id is null ? null : ReadAll().FirstOrDefault(x => x.Template.Id == id)?.Path;

    private string PathForNew(string displayName)
    {
        Directory.CreateDirectory(Root);

        var name = new string([.. displayName.Where(x => !Path.GetInvalidFileNameChars().Contains(x))]).Trim().TrimEnd('.');

        if (name.Length == 0)
        {
            name = "template";
        }

        if (name.Length > 50)
        {
            name = name[..50];
        }

        var path = Path.Combine(Root, name + TemplateFormat.Extension);

        for (var sequence = 1; File.Exists(path) && sequence <= 10; sequence++)
        {
            path = Path.Combine(Root, $"{name}_{sequence}{TemplateFormat.Extension}");
        }

        return File.Exists(path)
            ? Path.Combine(Root, $"{Guid.NewGuid():N}{TemplateFormat.Extension}")
            : path;
    }
}
