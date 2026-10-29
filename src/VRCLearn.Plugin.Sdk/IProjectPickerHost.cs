namespace VRCLearn.Plugin.Sdk;

public interface IProjectPickerHost
{
    ProjectPickResult PickProject(ProjectPickerOptions? options = null);

    ProjectPickResult ValidateProject(string projectPath);
}

public sealed record ProjectPickerOptions(
    string? Title = null,
    string? Hint = null,
    string? Summary = null,
    string? Note = null,
    string? ConfirmText = null,
    string? EmptyText = null);

public enum ProjectPickStatus
{
    Selected,
    Cancelled,
    NoProjects,
    NotAProject,
    Running,
}

public sealed record ProjectPickResult(
    ProjectPickStatus Status,
    string? ProjectPath = null,
    string? ProjectName = null);
