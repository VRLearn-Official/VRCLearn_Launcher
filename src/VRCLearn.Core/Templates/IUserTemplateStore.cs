namespace VRCLearn.Core.Templates;

public interface IUserTemplateStore
{
    Task<UserTemplates> LoadAsync(CancellationToken cancellationToken);

    /// <summary>id 相同就覆盖，否则按名字新建。</summary>
    Task SaveAsync(AlcomTemplate template, CancellationToken cancellationToken);

    /// <summary>删掉了返回 true。</summary>
    bool Remove(string path);

    Task<TemplateImportResult> ImportAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken);

    Task<int> OverrideAsync(IReadOnlyList<TemplateImportDuplicate> duplicates, CancellationToken cancellationToken);
}

public sealed record UserTemplates(IReadOnlyList<AlcomTemplateFile> AlcomTemplates);

/// <param name="Path">定义在磁盘上的哪个文件。</param>
public sealed record AlcomTemplateFile(string Path, AlcomTemplate Template);

public sealed record TemplateImportResult(
    int Imported,
    IReadOnlyList<TemplateImportDuplicate> Duplicates);

public sealed record TemplateImportDuplicate(
    string Id,
    string ExistingPath,
    string ExistingName,
    string ImportingName,
    string Content);
