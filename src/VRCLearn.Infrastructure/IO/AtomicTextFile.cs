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

        var temporary = $"{path}.tmp-{Guid.NewGuid():N}";

        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true))
                {
                    writer.Write(contents);
                    writer.Flush();
                }

                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            FileCleanup.TryDelete(temporary);
            throw;
        }
    }
}
