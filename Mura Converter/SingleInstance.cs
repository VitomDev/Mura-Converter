using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Mura_Converter;

/// <summary>
/// Keeps one Mura window: a second launch (e.g. from the Explorer context menu) hands its
/// arguments to the running instance over a named pipe and exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private static readonly string Name = "MuraConverter-" + Environment.UserName;

    private readonly Mutex mutex;
    private readonly CancellationTokenSource cts = new();

    private SingleInstance(Mutex mutex) => this.mutex = mutex;

    /// <summary>Returns null if another instance is running (after forwarding the args to it).</summary>
    public static SingleInstance? TryAcquire(string[] args)
    {
        var mutex = new Mutex(initiallyOwned: true, Name, out var createdNew);
        if (createdNew)
            return new SingleInstance(mutex);

        mutex.Dispose();
        try
        {
            using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.Write(string.Join('\n', args));
        }
        catch
        {
            // The other instance is closing; nothing useful to do.
        }

        return null;
    }

    public void Listen(Action<string[]> onArgs)
    {
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(Name, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(cts.Token);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var text = await reader.ReadToEndAsync(cts.Token);
                    onArgs(text.Length == 0 ? Array.Empty<string>() : text.Split('\n'));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    await Task.Delay(200);
                }
            }
        });
    }

    public void Dispose()
    {
        cts.Cancel();
        mutex.Dispose();
    }
}
