using System.Text.Json.Serialization;

namespace VRCLearn.Core.Projects;

public sealed class BackupRecord : IJsonOnDeserialized
{
    public string FilePath { get; set; } = "";

    public string ProjectName { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public bool IsArchive { get; set; }

    public bool IsAuto { get; set; }

    public int Serial { get; set; }

    public string Note { get; set; } = "";

    [JsonIgnore]
    public string FileName => Path.GetFileName(FilePath);

    void IJsonOnDeserialized.OnDeserialized()
    {
        FilePath ??= "";
        ProjectName ??= "";
        Note ??= "";
    }
}

public sealed record BackupCreateRequest(
    string ProjectName,
    DateTime CreatedAt,
    bool IsArchive,
    string? Note,
    bool IsAuto = false);
