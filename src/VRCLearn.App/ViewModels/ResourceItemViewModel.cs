using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCLearn.App.Services;
using VRCLearn.Core.Resources;

namespace VRCLearn.App.ViewModels;

public sealed partial class ResourceItemViewModel(
    ResourceDefinition definition,
    string coverPath,
    bool isExpired) : ObservableObject
{
    private bool _coverRequested;

    public ResourceDefinition Definition { get; } = definition;

    public string Name => Definition.Name;

    public IReadOnlyList<string> Tags => Definition.Tags;

    public string CoverPath { get; } = coverPath;

    public bool IsExpired { get; } = isExpired;

    public bool HasManual => Definition.Manual.Trim().Length > 0;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ImageSource? _cover;

    public async Task LoadCoverAsync()
    {
        if (_coverRequested || CoverPath.Length == 0)
        {
            return;
        }

        _coverRequested = true;

        try
        {
            var png = await File.ReadAllBytesAsync(CoverPath);

            Cover = await Task.Run(() => CoverImageScaler.Preview(png));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
