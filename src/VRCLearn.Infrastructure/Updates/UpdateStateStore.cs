using Microsoft.Extensions.Logging;
using VRCLearn.Core.Configuration;
using VRCLearn.Infrastructure.Storage;

namespace VRCLearn.Infrastructure.Updates;

public sealed class UpdateState
{
    public string Tag { get; set; } = "";

    public string ETag { get; set; } = "";

    public DateTimeOffset CheckedAt { get; set; }
}

public sealed class UpdateStateStore(AppPaths paths, ILogger<UpdateStateStore> logger)
{
    private string FilePath => Path.Combine(paths.PrivateDirectory, "update-state.json");

    public UpdateState Load() => JsonFileStore.Peek<UpdateState>(FilePath) ?? new UpdateState();

    public void Save(UpdateState state) =>
        JsonFileStore.Save(FilePath, state, message => logger.LogWarning("{Message}", message));
}
