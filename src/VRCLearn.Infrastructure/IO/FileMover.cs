namespace VRCLearn.Infrastructure.IO;

internal static class FileMover
{
    public static void Move(string source, string destination)
    {
        if (string.Equals(
                Path.GetPathRoot(source),
                Path.GetPathRoot(destination),
                StringComparison.OrdinalIgnoreCase))
        {
            File.Move(source, destination, overwrite: true);
            return;
        }

        var staging = $"{destination}.part";

        try
        {
            File.Copy(source, staging, overwrite: true);
            File.Move(staging, destination, overwrite: true);
            File.Delete(source);
        }
        catch
        {
            FileCleanup.TryDelete(staging);
            throw;
        }
    }
}
