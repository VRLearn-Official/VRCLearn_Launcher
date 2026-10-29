namespace VRCLearn.Core.Packages;

/// <summary>JSON 里可以写布尔值，也可以写字符串（撤回原因）。</summary>
public sealed record YankState(bool IsYanked, string? Reason)
{
    public static YankState NotYanked { get; } = new(false, null);

    public static YankState FromBoolean(bool yanked) => yanked ? new YankState(true, null) : NotYanked;

    public static YankState FromReason(string reason) => new(true, reason);
}
