using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCLearn.App.Services;
using VRCLearn.Core.Resources;

namespace VRCLearn.App.ViewModels;

public sealed record ResourceEditResult(ResourceDefinition Definition, byte[]? CoverPng);

public sealed record ResourceEditOutcome(ResourceEditResult? Result, bool Remove);

public sealed record PackagePathItem(string Path, string Name, bool Exists)
{
    public bool IsMissing => !Exists;
}

public sealed partial class TagChoice(string name, bool selected) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isSelected = selected;
}

public sealed partial class ResourceEditViewModel : ObservableObject
{
    private readonly string _id;
    private readonly string _coverPath;
    private readonly DateTime _createdAt;
    private readonly LocalizationService _localization;

    private byte[]? _coverPng;

    public ResourceEditViewModel(
        ResourceDefinition? existing,
        IReadOnlyList<string> tags,
        LocalizationService localization)
    {
        _localization = localization;
        _id = existing?.Id ?? Guid.NewGuid().ToString("N");
        _coverPath = existing?.CoverPath ?? "";
        _createdAt = existing?.CreatedAt ?? DateTime.UtcNow;
        IsNew = existing is null;
        Name = existing?.Name ?? "";
        Manual = existing?.Manual ?? "";

        foreach (var path in existing?.UnityPackagePaths ?? [])
        {
            PackagePaths.Add(new PackagePathItem(path, Path.GetFileName(path), File.Exists(path)));
        }

        var selected = existing?.Tags.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        foreach (var tag in tags)
        {
            TagOptions.Add(new TagChoice(tag, selected.Contains(tag)));
        }
    }

    public bool IsNew { get; }

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _manual = "";

    public bool IsManualMissing => ResourceManual.IsMissingFile(Manual);

    public bool IsManualUnsupported
    {
        get
        {
            var value = Manual.Trim();

            return value.Length > 0
                && !ResourceManual.IsUrl(value)
                && File.Exists(value)
                && !ResourceManual.IsSupportedFile(value);
        }
    }

    partial void OnManualChanged(string value)
    {
        OnPropertyChanged(nameof(IsManualMissing));
        OnPropertyChanged(nameof(IsManualUnsupported));
    }

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private ImageSource? _coverPreview;

    public ObservableCollection<TagChoice> TagOptions { get; } = [];

    public ObservableCollection<PackagePathItem> PackagePaths { get; } = [];

    public bool HasTags => TagOptions.Count > 0;

    public bool HasPackages => PackagePaths.Count > 0;

    public void SetCover(byte[] png)
    {
        if (CoverImageScaler.Preview(png) is not { } preview)
        {
            Status = _localization["Resources.Edit.CoverUnreadable"];
            return;
        }

        _coverPng = png;
        CoverPreview = preview;
    }

    public void ShowCover(byte[] png)
    {
        if (CoverImageScaler.Preview(png) is { } preview)
        {
            CoverPreview = preview;
        }
    }

    public void AddPackages(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            if (!PackagePaths.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                PackagePaths.Add(new PackagePathItem(path, Path.GetFileName(path), File.Exists(path)));
            }
        }

        OnPropertyChanged(nameof(HasPackages));
    }

    [RelayCommand]
    private void RemovePackage(PackagePathItem item)
    {
        PackagePaths.Remove(item);
        OnPropertyChanged(nameof(HasPackages));
    }

    public ResourceEditResult? BuildResult()
    {
        var name = Name.Trim();

        if (name.Length == 0)
        {
            Status = _localization["Resources.Edit.NameEmpty"];
            return null;
        }

        if (PackagePaths.Count == 0)
        {
            Status = _localization["Resources.Edit.PackagesEmpty"];
            return null;
        }

        var now = DateTime.UtcNow;

        var definition = new ResourceDefinition
        {
            Id = _id,
            Name = name,
            CoverPath = _coverPath,
            Manual = Manual.Trim(),
            Tags = [.. TagOptions.Where(x => x.IsSelected).Select(x => x.Name)],
            UnityPackagePaths = [.. PackagePaths.Select(x => x.Path)],
            CreatedAt = _createdAt,
            UpdatedAt = now,
        };

        return new ResourceEditResult(definition, _coverPng);
    }
}
