using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCLearn.App.Services;
using VRCLearn.Core.Resources;

namespace VRCLearn.App.ViewModels;

public sealed partial class TagManagerViewModel(
    IResourceStore store,
    DialogService dialogs,
    LocalizationService localization) : ObservableObject
{
    public ObservableCollection<string> Tags { get; } = [];

    [ObservableProperty]
    private string _newTag = "";

    [ObservableProperty]
    private string? _status;

    public bool HasTags => Tags.Count > 0;

    public void Load()
    {
        Tags.Clear();

        foreach (var tag in store.Current.Tags)
        {
            Tags.Add(tag);
        }

        OnPropertyChanged(nameof(HasTags));
    }

    [RelayCommand]
    private void AddTag()
    {
        var name = NewTag.Trim();

        if (name.Length == 0)
        {
            return;
        }

        if (!ResourceOperations.AddTag(store.Current, name))
        {
            Status = localization["Resources.Manage.Exists"];
            return;
        }

        store.Save();
        NewTag = "";
        Status = null;
        Load();
    }

    [RelayCommand]
    private void RemoveTag(string tag)
    {
        if (!dialogs.ShowConfirm(
                localization["Resources.Manage.RemoveTitle"],
                localization.Format("Resources.Manage.RemoveMessage", tag),
                localization["Resources.Manage.RemoveConfirm"],
                danger: true))
        {
            return;
        }

        ResourceOperations.RemoveTag(store.Current, tag);
        store.Save();
        Status = null;
        Load();
    }

    [RelayCommand]
    private void Cleanup()
    {
        var expired = ResourceOperations.Expired(store.Current.Resources, File.Exists);

        if (expired.Count == 0)
        {
            Status = localization["Resources.Manage.CleanupNone"];
            return;
        }

        var names = string.Join(
            Environment.NewLine,
            expired.Select(x => x.Name));

        if (!dialogs.ShowConfirm(
                localization["Resources.Manage.CleanupTitle"],
                localization.Format("Resources.Manage.CleanupMessage", names),
                localization["Resources.Manage.CleanupConfirm"],
                danger: true))
        {
            return;
        }

        foreach (var resource in ResourceOperations.RemoveExpired(store.Current, File.Exists))
        {
            store.DeleteCover(resource);
        }

        store.Save();
        Status = localization.Format("Resources.Manage.CleanupDone", expired.Count);
        Load();
    }
}
