using Microsoft.Extensions.Logging;
using VRCLearn.Core.Vpm;
using Sdk = VRCLearn.Plugin.Sdk;

namespace VRCLearn.App.Plugins;

public sealed class RepositoryHostService(
    RepositoryService repositories,
    ILogger<RepositoryHostService> logger) : Sdk.IRepositoryHost
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new Dictionary<string, string>();

    public bool IsAdded(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return repositories.List().Any(x => x.Url is { } existing
            && string.Equals(existing.AbsoluteUri, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Sdk.RepositoryAddResult> AddAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            logger.LogWarning("插件请求添加无效仓库地址：{Url}", url);

            return new Sdk.RepositoryAddResult(Sdk.RepositoryAddStatus.InvalidUrl);
        }

        Sdk.RepositoryAddResult result;

        try
        {
            var outcome = await repositories.AddAsync(uri, null, NoHeaders, cancellationToken);

            result = new Sdk.RepositoryAddResult(Map(outcome.Status), outcome.Detail);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "插件添加仓库失败：{Url}", uri.AbsoluteUri);
            throw;
        }

        switch (result.Status)
        {
            case Sdk.RepositoryAddStatus.Added:
                logger.LogInformation("插件添加仓库：{Url}", uri.AbsoluteUri);
                break;
            case Sdk.RepositoryAddStatus.AlreadyAdded:
                logger.LogInformation("仓库已存在：{Url}", uri.AbsoluteUri);
                break;
            default:
                logger.LogWarning(
                    "插件添加仓库失败：{Url} {Status} {Detail}",
                    uri.AbsoluteUri,
                    result.Status,
                    result.Detail);
                break;
        }

        return result;
    }

    private static Sdk.RepositoryAddStatus Map(AddRepositoryStatus status) => status switch
    {
        AddRepositoryStatus.Success => Sdk.RepositoryAddStatus.Added,
        AddRepositoryStatus.AlreadyAdded => Sdk.RepositoryAddStatus.AlreadyAdded,
        AddRepositoryStatus.Unreachable => Sdk.RepositoryAddStatus.Unreachable,
        AddRepositoryStatus.InvalidContent => Sdk.RepositoryAddStatus.InvalidContent,
        _ => Sdk.RepositoryAddStatus.InvalidUrl,
    };
}
