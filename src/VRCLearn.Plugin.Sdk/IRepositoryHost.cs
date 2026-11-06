namespace VRCLearn.Plugin.Sdk;

public interface IRepositoryHost
{
    bool IsAdded(string url);

    Task<RepositoryAddResult> AddAsync(
        string url,
        CancellationToken cancellationToken = default);
}

public enum RepositoryAddStatus
{
    Added,

    AlreadyAdded,

    InvalidUrl,

    Unreachable,

    InvalidContent,

    SaveFailed,
}

public sealed record RepositoryAddResult(RepositoryAddStatus Status, string? Detail = null);
