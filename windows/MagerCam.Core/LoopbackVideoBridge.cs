using System.Net;
using System.Net.Sockets;

namespace MagerCam.Core;

/// A local-only H264 transport for the native decoder; no managed memory callbacks.
public sealed class LoopbackVideoBridge : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly object gate = new();
    private TcpClient? consumer;
    private bool disposed;
    public string Location { get; }
    public Task Completion { get; }

    public LoopbackVideoBridge(McVideoStream source)
    {
        listener.Start(1);
        Location = $"tcp://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        Completion = Task.Run(() =>
        {
            using var peer = listener.AcceptTcpClient();
            lock (gate)
            {
                if (disposed) return;
                consumer = peer;
            }
            listener.Stop(); // Only the player that connected first can consume this session.
            peer.NoDelay = true;
            peer.SendBufferSize = 128 * 1024;
            peer.SendTimeout = 5000;
            source.CopyTo(peer.GetStream(), 32 * 1024);
            if (source.Failure is { } error) throw error;
        });
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            consumer?.Dispose(); listener.Stop();
        }
    }
}
