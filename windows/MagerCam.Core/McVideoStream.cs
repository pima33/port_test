using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;

namespace MagerCam.Core;

public sealed record CameraStatus(string Name, int Width, int Height, int Fps, int Clients, bool CameraOpen, bool Hot);

/// Removes MC framing for the H.264 decoder. Pull-based reading adds no unbounded video queue.
public sealed class McVideoStream(Stream source) : Stream
{
    private byte[] pending = [];
    private int position;
    private bool configured, waitingKey = true;
    private ulong? lastPts;
    private long lastVideo = Stopwatch.GetTimestamp();
    private long frames, videoBytes;
    private volatile bool disposed;
    private CameraStatus? status;
    public CameraStatus? Status => Volatile.Read(ref status);
    public long Frames => Interlocked.Read(ref frames);
    public long VideoBytes => Interlocked.Read(ref videoBytes);
    public Exception? Failure { get; private set; }
    public event Action<Exception>? Failed;

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> destination)
    {
        if (destination.IsEmpty || disposed) return 0;
        try
        {
            while (position == pending.Length)
            {
                if (disposed) return 0;
                if (Stopwatch.GetElapsedTime(lastVideo).TotalSeconds > 8)
                    throw new CameraException("Нет видео от iPhone. Откройте приложение и запустите камеру.");
                ReadPacket();
            }
            var count = Math.Min(destination.Length, pending.Length - position);
            pending.AsSpan(position, count).CopyTo(destination);
            position += count;
            return count;
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or JsonException or InvalidOperationException)
        {
            if (!disposed)
            {
                Failure = error is JsonException or InvalidOperationException
                    ? new CameraException("Камера прислала некорректный статус.", false) : error;
                Failed?.Invoke(Failure);
            }
            return 0; // Never let a managed exception cross the native decoder callback.
        }
    }

    private void ReadPacket()
    {
        Span<byte> header = stackalloc byte[16];
        source.ReadExactly(header);
        if (header[0] != 'M' || header[1] != 'C')
            throw new CameraException("Повреждён заголовок видеопотока.", false);
        var kind = header[2];
        var flags = header[3];
        var length = BinaryPrimitives.ReadUInt32BigEndian(header[4..8]);
        var pts = BinaryPrimitives.ReadUInt64BigEndian(header[8..16]);
        if (length > 32 * 1024 * 1024 || (kind == 5 && length > 65536))
            throw new CameraException("Слишком большой пакет от камеры.", false);
        var payload = new byte[(int)length];
        source.ReadExactly(payload);
        switch (kind)
        {
            case 1:
                if ((flags & 2) != 0) throw new CameraException("Эта версия принимает только H.264.", false);
                CheckAnnexB(payload);
                configured = true;
                waitingKey = true;
                pending = payload; position = 0;
                break;
            case 2:
                if (!configured) throw new CameraException("Камера не передала настройки H.264.");
                if (waitingKey && (flags & 1) == 0) return;
                CheckAnnexB(payload);
                if (lastPts.HasValue && pts <= lastPts.Value)
                    throw new CameraException("Нарушен порядок кадров. Соединение будет восстановлено.");
                waitingKey = false; lastPts = pts;
                lastVideo = Stopwatch.GetTimestamp();
                Interlocked.Increment(ref frames);
                Interlocked.Add(ref videoBytes, payload.Length);
                pending = payload; position = 0;
                break;
            case 5:
                using (var json = JsonDocument.Parse(payload))
                {
                    var root = json.RootElement;
                    int Number(string key, int fallback) => root.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : fallback;
                    bool Flag(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
                    var name = root.TryGetProperty("name", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "iPhone" : "iPhone";
                    Volatile.Write(ref status, new CameraStatus(name[..Math.Min(80, name.Length)], Number("w", 1280), Number("h", 720), Number("fps", 30), Number("clients", 1), Flag("camera_open"), Flag("hot")));
                }
                break;
            case 3: case 4: case 6: break; // MVP is video-only; audio and keepalive are consumed.
            default: throw new CameraException("Неизвестный тип пакета MagerCam.", false);
        }
    }

    private static void CheckAnnexB(byte[] data)
    {
        if (data.Length < 5 || data[0] != 0 || data[1] != 0 || data[2] != 0 || data[3] != 1)
            throw new CameraException("Камера прислала несовместимый H.264-пакет.", false);
    }

    protected override void Dispose(bool disposing)
    {
        disposed = true;
        if (disposing) source.Dispose();
        base.Dispose(disposing);
    }
    public override bool CanRead => !disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
