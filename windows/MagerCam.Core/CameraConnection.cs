using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MagerCam.Core;

public sealed class CameraException(string message, bool retryable = true) : IOException(message)
{
    public bool Retryable { get; } = retryable;
}

public sealed class CameraConnection : IDisposable
{
    private readonly TcpClient client;
    public McVideoStream Video { get; }

    private CameraConnection(TcpClient client)
    {
        this.client = client;
        Video = new McVideoStream(client.GetStream());
    }

    public static async Task<CameraConnection> ConnectAsync(string host, int port, string pin, CancellationToken cancellation)
    {
        if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            throw new CameraException("Введите IPv4 с экрана iPhone, например 192.168.1.23.", false);
        if (port is < 1 or > 65535 || pin.Length != 6 || pin.Any(c => c is < '0' or > '9'))
            throw new CameraException("Укажите порт 1–65535 и шестизначный PIN с iPhone.", false);

        var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true, ReceiveTimeout = 6000 };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            await client.ConnectAsync(address, port, timeout.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            stream.ReadTimeout = 6000;
            var request = $"GET /stream?w=1280&h=720&fps=30&bitrate=4000&audio=0&codec=h264 HTTP/1.1\r\nHost: {address}:{port}\r\nX-MagerCam-Key: {pin}\r\nConnection: keep-alive\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(request), timeout.Token).ConfigureAwait(false);
            // Read only the header: a read-ahead buffer could consume the first MC packet.
            var header = new List<byte>();
            var one = new byte[1];
            while (header.Count < 16_384)
            {
                if (await stream.ReadAsync(one, timeout.Token).ConfigureAwait(false) == 0)
                    throw new CameraException("iPhone закрыл соединение до начала передачи.");
                header.Add(one[0]);
                if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10)
                    return ValidateResponse(client, Encoding.ASCII.GetString(header.ToArray()));
            }
            throw new CameraException("Сервер прислал слишком большой HTTP-заголовок.", false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            client.Dispose();
            throw new CameraException("iPhone не отвечает. Проверьте IP, Wi-Fi и запуск камеры.");
        }
        catch { client.Dispose(); throw; }
    }

    private static CameraConnection ValidateResponse(TcpClient client, string header)
    {
        var lines = header.Split("\r\n");
        var status = lines[0].Split(' ');
        if (status.Length < 2 || status[0] is not ("HTTP/1.0" or "HTTP/1.1") || !int.TryParse(status[1], out var code))
            throw new CameraException("На этом порту нет совместимого сервера MagerCam.", false);
        if (code != 200)
            throw code switch
            {
                401 or 403 => new CameraException("Неверный PIN. Введите код, показанный на iPhone.", false),
                429 => new CameraException("Слишком много неверных PIN. Подождите минуту и подключитесь снова.", false),
                409 => new CameraException("Все подключения заняты. Закройте лишние просмотры камеры.", false),
                _ => new CameraException($"Камера вернула HTTP {code}.", code >= 500)
            };
        if (lines.Any(l => l.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase)))
            throw new CameraException("Этот HTTP-поток не соответствует протоколу MagerCam.", false);
        return new CameraConnection(client);
    }

    public void Dispose() { client.Dispose(); Video.Dispose(); }
}
