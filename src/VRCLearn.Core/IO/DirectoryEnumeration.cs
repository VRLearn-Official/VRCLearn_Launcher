namespace VRCLearn.Core.IO;

public static class DirectoryEnumeration
{
    public static EnumerationOptions TopLevel { get; } = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };

    public static EnumerationOptions Recursive { get; } = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };
}
