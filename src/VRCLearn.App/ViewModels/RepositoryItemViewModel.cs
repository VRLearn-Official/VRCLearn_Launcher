using CommunityToolkit.Mvvm.ComponentModel;

namespace VRCLearn.App.ViewModels;

public sealed partial class RepositoryItemViewModel : ObservableObject
{
    public RepositoryItemViewModel(
        int index,
        string? id,
        string? url,
        string? name,
        string summary,
        bool isHidden,
        string hideAction)
    {
        Index = index;
        Id = id;
        Url = url;
        Summary = summary;
        IsHidden = isHidden;
        HideAction = hideAction;
        Title = name ?? url ?? id ?? "(unknown)";
    }

    public int Index { get; }

    public string? Id { get; }

    public string? Url { get; }

    public string Title { get; }

    public string Summary { get; }

    public bool IsHidden { get; }

    public string HideAction { get; }

    public string Details => Id is null ? Url ?? "" : $"{Id}  ·  {Url}";

    [ObservableProperty]
    private bool _isDragging;
}
