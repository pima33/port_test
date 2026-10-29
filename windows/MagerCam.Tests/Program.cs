using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LibVLCSharp.Shared;
using MagerCam.Core;

static class Tests
{
    private static readonly byte[] Config = [0, 0, 0, 1, 0x67, 1, 2, 0, 0, 0, 1, 0x68, 3];
    private static readonly byte[] KeyFrame = [0, 0, 0, 1, 0x65, 4, 5];
    private static readonly byte[] Status = Encoding.UTF8.GetBytes("{\"name\":\"Test iPhone\",\"w\":1280,\"h\":720,\"fps\":30,\"clients\":2,\"camera_open\":true,\"hot\":false}");

    static async Task<int> Main()
    {
        try
        {
            ParseFragmentedPackets();
            RejectInvalidPackets();
            await TestHttpAndAuthentication();
            await TestCancellation();
            await DecodeRealVideo();
            Console.WriteLine("PASS: all protocol, network, cancellation and H.264 decoder checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Assert(bool value, string message)
    { if (!value) throw new Exception(message); }

    private static byte[] Packet(byte kind, byte[] payload, ulong pts = 0, byte flags = 0)
    {
        var bytes = new byte[16 + payload.Length];
        bytes[0] = 77; bytes[1] = 67; bytes[2] = kind; bytes[3] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8), pts);
        payload.CopyTo(bytes, 16);
        return bytes;
    }

    private static void ParseFragmentedPackets()
    {
        byte[] bytes = [..Packet(3, [0, 0, 0xbb, 0x80, 1]), ..Packet(5, Status), ..Packet(1, Config, flags:1),
            ..Packet(2, [0,0,0,1,0x41,9], 1), ..Packet(2, KeyFrame, 0x0102030405060708, 1)];
        using var stream = new McVideoStream(new FragmentedStream(bytes));
        var output = new byte[Config.Length + KeyFrame.Length];
        stream.ReadExactly(output);
        Assert(output.SequenceEqual(Config.Concat(KeyFrame)), "MC framing must not leak into H264 input");
        Assert(stream.Frames == 1 && stream.VideoBytes == KeyFrame.Length, "Wait for keyframe, count only video");
        Assert(stream.Status is { Width: 1280, Height: 720, Clients: 2, CameraOpen: true }, "Status parsing");
        Console.WriteLine("PASS: fragmented MC, audio skip, status, config/keyframe ordering, 64-bit PTS");
    }

    private static void RejectInvalidPackets()
    {
        var badMagic = Packet(1, Config); badMagic[0] = 0;
        var huge = Packet(1, []); BinaryPrimitives.WriteUInt32BigEndian(huge.AsSpan(4), 33 * 1024 * 1024);
        byte[][] invalid = [badMagic, huge, Packet(2, KeyFrame, 1, 1), Packet(1, Config, flags:2), Packet(1, [1,2,3]), Packet(5, Encoding.UTF8.GetBytes("invalid")), Packet(5, Encoding.UTF8.GetBytes("[]")), Packet(1, Config)[..^1],
            [..Packet(1, Config), ..Packet(2, KeyFrame, 20, 1), ..Packet(2, KeyFrame, 19, 1)]];
        foreach (var bytes in invalid)
        {
            using var stream = new McVideoStream(new FragmentedStream(bytes));
            stream.CopyTo(Stream.Null);
            Assert(stream.Failure != null, "Malformed MC must terminate without an exception crossing native callbacks");
        }
        Console.WriteLine("PASS: corrupt, truncated, oversized, HEVC, invalid JSON, missing config, backwards PTS");
    }

    private static async Task TestHttpAndAuthentication()
    {
        foreach (var code in new[] { 200, 401, 409, 429 })
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using var peer = await listener.AcceptTcpClientAsync();
                var source = peer.GetStream();
                var request = await ReadHeader(source);
                Assert(request.Contains("X-MagerCam-Key: 123456\r\n") && request.Contains("audio=0&codec=h264"), "Expected iOS handshake");
                var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} Fixture\r\nContent-Type: application/x-magercam\r\n\r\n");
                foreach (var b in header) await source.WriteAsync(new byte[] { b });
                if (code == 200) await source.WriteAsync(Packet(1, Config));
            });
            try
            {
                using var connection = await CameraConnection.ConnectAsync("127.0.0.1", port, "123456", CancellationToken.None);
                Assert(code == 200, "HTTP error must be rejected");
                var data = new byte[Config.Length]; connection.Video.ReadExactly(data);
                Assert(data.SequenceEqual(Config), "HTTP parser must preserve the first MC packet");
            }
            catch (CameraException error) { Assert(code != 200 && !error.Retryable, "Do not retry authentication / rate limit / full server"); }
            await server;
        }
        Console.WriteLine("PASS: real TCP fragmented HTTP, PIN header, no read-ahead loss, 401/409/429 stop retrying");
    }

    private static async Task TestCancellation()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var cancel = new CancellationTokenSource(150);
        var attempt = CameraConnection.ConnectAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, "123456", cancel.Token);
        using var peer = await listener.AcceptTcpClientAsync();
        try { using var ignored = await attempt; throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { }
        Console.WriteLine("PASS: cancel a connection stalled during HTTP handshake");
    }

    private static async Task DecodeRealVideo()
    {
        var data = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "fixture.h264"));
        var nals = new List<byte[]>();
        var offsets = new List<(int Offset, int Length)>();
        for (var i = 0; i < data.Length - 3; i++)
        {
            var size = data[i] == 0 && data[i+1] == 0 ? (data[i+2] == 1 ? 3 : data[i+2] == 0 && data[i+3] == 1 ? 4 : 0) : 0;
            if (size == 0) continue;
            offsets.Add((i, size)); i += size - 1;
        }
        for (var i = 0; i < offsets.Count; i++)
        {
            var begin = offsets[i].Offset + offsets[i].Length;
            var end = i + 1 < offsets.Count ? offsets[i+1].Offset : data.Length;
            nals.Add([0,0,0,1,..data[begin..end]]);
        }
        var config = nals.Where(n => (n[4] & 31) is 7 or 8).SelectMany(n => n).ToArray();
        var frames = new List<(byte[] Bytes, bool Key)>();
        var au = new List<byte>(); var key = false;
        foreach (var nal in nals)
        {
            var type = nal[4] & 31;
            if (type == 9 && au.Count > 0) { frames.Add((au.ToArray(), key)); au.Clear(); key = false; }
            if (type is 7 or 8) continue;
            key |= type == 5; au.AddRange(nal);
        }
        if (au.Count > 0) frames.Add((au.ToArray(), key));
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var finish = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var server = Task.Run(async () =>
        {
            try
            {
                using var peer = await listener.AcceptTcpClientAsync(finish.Token);
                peer.NoDelay = true;
                var stream = peer.GetStream();
                await ReadHeader(stream);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\n\r\n"), finish.Token);
                await stream.WriteAsync(Packet(5, Status).Concat(Packet(1, config, flags:1)).ToArray(), finish.Token);
                for (var i = 0; !finish.IsCancellationRequested; i++)
                {
                    var frame = frames[i % frames.Count];
                    await stream.WriteAsync(Packet(2, frame.Bytes, (ulong)(1000 + i * 33333), (byte)(frame.Key ? 1 : 0)), finish.Token);
                    await Task.Delay(33, finish.Token);
                }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or SocketException) { }
        });
        LibVLCSharp.Shared.Core.Initialize();
        using var vlc = new LibVLC("--vout=dummy", "--no-audio");
        using var player = new MediaPlayer(vlc) { EnableHardwareDecoding = false };
        using var camera = await CameraConnection.ConnectAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, "123456", finish.Token);
        using var bridge = new LoopbackVideoBridge(camera.Video);
        using var media = new Media(vlc, bridge.Location, FromType.FromLocation, ":demux=h264", ":h264-fps=30", ":network-caching=100");
        try
        {
            Assert(player.Play(media), "Start decoder");
            for (var i = 0; i < 80 && media.Statistics.DisplayedPictures < 20; i++) await Task.Delay(100);
            Assert(media.Statistics.DecodedVideo >= 20 && media.Statistics.DisplayedPictures >= 20, $"H264 playback stalled: decoded={media.Statistics.DecodedVideo}, displayed={media.Statistics.DisplayedPictures}");
            Console.WriteLine($"PASS: real H264 playback over TCP → MC parser → LibVLC: {media.Statistics.DecodedVideo} decoded / {media.Statistics.DisplayedPictures} displayed frames");
        }
        finally
        {
            camera.Dispose(); finish.Cancel(); bridge.Dispose();
            await Task.Run(player.Stop);
            player.Media = null;
            await server;
            try { await bridge.Completion; } catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException) { }
        }
    }

    private static async Task<string> ReadHeader(Stream stream)
    {
        var bytes = new List<byte>(); var one = new byte[1];
        while (bytes.Count < 16384)
        {
            await stream.ReadExactlyAsync(one);
            bytes.Add(one[0]);
            if (bytes.Count >= 4 && bytes[^4] == 13 && bytes[^3] == 10 && bytes[^2] == 13 && bytes[^1] == 10)
                return Encoding.ASCII.GetString(bytes.ToArray());
        }
        throw new Exception("Oversized test request");
    }

    private sealed class FragmentedStream(byte[] data) : MemoryStream(data)
    {
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(3, buffer.Length)]);
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 3));
    }
}
