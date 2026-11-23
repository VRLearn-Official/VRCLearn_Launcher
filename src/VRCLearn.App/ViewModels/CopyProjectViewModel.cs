using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCLearn.App.Services;

namespace VRCLearn.App.ViewModels;

public sealed partial class CopyProjectViewModel : ObservableObject
{
    private readonly LocalizationService _localization;
    private readonly DialogService _dialogs;
    private readonly string _folderName;

    public CopyProjectViewModel(string destination, LocalizationService localization, DialogService dialogs)
    {
        _destination = destination;
        _folderName = Path.GetFileName(destination);
        _localization = localization;
        _dialogs = dialogs;
    }

    [ObservableProperty]
    private string _destination;

    [RelayCommand]
    private void Browse()
    {
        var selected = _dialogs.PickFolder(
            _localization["ProjectDetail.Copy.Title"],
            Path.GetDirectoryName(Destination) ?? "");

        if (selected is null)
        {
            return;
        }

        Destination = Path.Combine(
            selected,
            Path.GetFileName(Destination) is { Length: > 0 } name ? name : _folderName);
    }
}
