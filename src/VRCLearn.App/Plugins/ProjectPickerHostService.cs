using System.IO;
using VRCLearn.App.Services;
using VRCLearn.App.ViewModels;
using VRCLearn.Core.Configuration;
using VRCLearn.Core.Projects;
using VRCLearn.Core.Unity;
using VRCLearn.Plugin.Sdk;

namespace VRCLearn.App.Plugins;

public sealed class ProjectPickerHostService(
    ProjectService projects,
    UnityProjectOpener opener,
    DialogService dialogs,
    LocalizationService localization) : IProjectPickerHost
{
    public ProjectPickResult PickProject(ProjectPickerOptions? options = null)
    {
        var choices = projects.List()
            .Select(x => new ProjectChoice(
                x,
                NameOf(x),
                ProjectService.IsProject(x),
                opener.Status(x) != UnityProjectStatus.Closed))
            .OrderByDescending(x => x.IsRunning)
            .ToList();

        if (choices.Count == 0)
        {
            return new ProjectPickResult(ProjectPickStatus.NoProjects);
        }

        var chrome = new ProjectPickerChrome(
            options?.Title ?? localization["PluginPicker.Title"],
            options?.Hint ?? localization["PluginPicker.Hint"],
            options?.Summary ?? localization["PluginPicker.Summary"],
            options?.Note ?? localization["PluginPicker.Note"],
            options?.ConfirmText ?? localization["PluginPicker.Confirm"],
            options?.EmptyText ?? localization["PluginPicker.Empty"]);

        var path = dialogs.ShowProjectPicker(choices, chrome, PluginWindowOwner.Active());

        return path is null
            ? new ProjectPickResult(ProjectPickStatus.Cancelled)
            : ValidateProject(path);
    }

    public ProjectPickResult ValidateProject(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return new ProjectPickResult(ProjectPickStatus.NotAProject);
        }

        string normalized;

        try
        {
            normalized = PathUtil.Normalize(projectPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new ProjectPickResult(ProjectPickStatus.NotAProject);
        }

        var name = NameOf(normalized);

        if (!ProjectService.IsProject(normalized))
        {
            return new ProjectPickResult(ProjectPickStatus.NotAProject, normalized, name);
        }

        if (opener.Status(normalized) != UnityProjectStatus.Closed)
        {
            return new ProjectPickResult(ProjectPickStatus.Running, normalized, name);
        }

        return new ProjectPickResult(ProjectPickStatus.Selected, normalized, name);
    }

    private static string NameOf(string path)
    {
        try
        {
            return PathUtil.NameOf(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
