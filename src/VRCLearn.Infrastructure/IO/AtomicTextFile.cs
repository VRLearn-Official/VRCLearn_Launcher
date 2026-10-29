using System.Text;

namespace VRCLearn.Infrastructure.IO;

internal static class AtomicTextFile
{
    public static void Write(string path, string contents)
    {
        if (Path.GetDirectoryName(path) is { } directory)
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";

        try
        {
            File.WriteAllText(temporary, contents, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            FileCleanup.TryDelete(temporary);
            throw;
        }
    }
}
