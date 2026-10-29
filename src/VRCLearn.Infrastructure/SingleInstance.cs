using System.IO.Pipes;
using System.Text.Json;

namespace VRCLearn.Infrastructure;

public sealed class SingleInstance : IDisposable
{
    public const string DefaultName = "VRCLearnLauncher";

    public const string RestartArgument = "--restart";

    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(5);

    private readonly Mutex _mutex;
    private readonly string _name;
    private readonly CancellationTokenSource _cancellation = new();

    private SingleInstance(Mutex mutex, string name)
    {
        _mutex = mutex;
        _name = name;
    }

    /// 已有实例在跑时返回 null；<paramref name="waitForPrevious"/> 用于等待即将退出的旧实例。
    public static SingleInstance? Acquire(string name = DefaultName, TimeSpan? waitForPrevious = null)
    {
        var deadline = Environment.TickCount64 + (long)(waitForPrevious ?? TimeSpan.Zero).TotalMilliseconds;

        while (true)
        {
            var mutex = new Mutex(initiallyOwned: true, $"{name}.SingleInstance", out var createdNew);

            if (createdNew)
            {
                return new SingleInstance(mutex, name);
            }

            mutex.Dispose();

            if (Environment.TickCount64 >= deadline)
            {
                return null;
            }

            Thread.Sleep(100);
        }
    }

    public static bool Handoff(
        IReadOnlyList<string> arguments,
        string name = DefaultName,
        TimeSpan? timeout = null)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", $"{name}.Handoff", PipeDirection.Out);
            client.Connect((int)(timeout ?? HandoffTimeout).TotalMilliseconds);

            var payload = JsonSerializer.SerializeToUtf8Bytes(arguments.ToArray());
            client.Write(payload, 0, payload.Length);
            client.Flush();

            return true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// 回调在后台线程上，界面操作须自行切回。
    public void StartListening(Action<IReadOnlyList<string>> onArguments) => _ = Task.Run(async () =>
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    $"{_name}.Handoff",
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1);

                await server.WaitForConnectionAsync(_cancellation.Token).ConfigureAwait(false);

                if (await ReadAsync(server, _cancellation.Token).ConfigureAwait(false) is { Count: > 0 } arguments)
                {
                    onArguments(arguments);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
            }
        }
    });

    private static async Task<IReadOnlyList<string>?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();

        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        return JsonSerializer.Deserialize<string[]>(buffer.ToArray());
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();

        _mutex.Dispose();
    }
}
