using VRCLearn.App.Services;

namespace VRCLearn.App.ViewModels;

/// <summary>下拉框选项的唯一一份；换语言时调用 <see cref="Refresh"/> 更新显示。</summary>
internal static class LocalizedOptions
{
    /// <summary><c>""</c> 表示跟随系统，其余是语言包。</summary>
    public static IReadOnlyList<Option> Languages(LocalizationService localization) =>
    [
        new("", () => localization["Settings.Language.System"]),
        .. localization.Packs.Select(x => new Option(x.Code, x.Display)),
    ];

    public static IReadOnlyList<Option> Themes(LocalizationService localization) =>
    [
        new("System", () => localization["Settings.Theme.System"]),
        new("Light", () => localization["Settings.Theme.Light"]),
        new("Dark", () => localization["Settings.Theme.Dark"]),
    ];

    public static IReadOnlyList<Option> ButtonColors(LocalizationService localization) =>
    [
        new("diamond", () => localization["Settings.ButtonColor.Diamond"]),
        new("bilibili", () => localization["Settings.ButtonColor.Bilibili"]),
        new("grass", () => localization["Settings.ButtonColor.Grass"]),
        new("system", () => localization["Settings.ButtonColor.System"]),
    ];

    public static void Refresh(IEnumerable<Option> options)
    {
        foreach (var option in options)
        {
            option.Refresh();
        }
    }
}
