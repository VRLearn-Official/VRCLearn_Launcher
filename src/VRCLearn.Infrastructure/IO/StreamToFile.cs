namespace VRCLearn.Infrastructure.IO;

/// <summary>把流写进文件，失败时清掉半截文件。</summary>
internal static class StreamToFile
{
    private const int BufferSize = 81920;

    public static async Task<long> CopyAsync(
        Stream source,
        string destinationPath,
        Action<long>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var destination = File.Create(destinationPath);

            var buffer = new byte[BufferSize];
            long received = 0;

            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

                received += read;
                progress?.Invoke(received);
            }

            return received;
        }
        catch
        {
            FileCleanup.TryDelete(destinationPath);
            throw;
        }
    }
}
