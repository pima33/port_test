using System.Diagnostics;
using System.Net.Sockets;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using MagerCam.Core;

namespace MagerCam.Windows;

internal sealed class MainForm : Form
{
    private static readonly Color Ink = Color.FromArgb(232, 237, 245);
    private static readonly Color Muted = Color.FromArgb(148, 163, 184);
    private static readonly Color Surface = Color.FromArgb(25, 34, 49);
    private static readonly Color Accent = Color.FromArgb(250, 197, 67);
    private readonly TextBox host = Field("IPv4 iPhone"), pin = Field("PIN iPhone");
    private readonly NumericUpDown port = new() { Minimum = 1, Maximum = 65535, Value = 4848, Width = 90, Height = 32, AccessibleName = "Порт" };
    private readonly CheckBox reconnect = new() { Text = "Переподключаться автоматически", Checked = true, Width = 250, Height = 42, ForeColor = Muted };
    private readonly Button connect = Button("Подключить камеру", true);
    private readonly Label connectionState = Label("Готово к подключению", 11, Muted, 250, 72);
    private readonly Label deviceName = Label("ПРОСМОТР КАМЕРЫ", 10, Muted);
    private readonly Label format = Label("720p · 30 fps · H.264", 11, Ink);
    private readonly Label rate = Label("Поток —", 11, Ink);
    private readonly Label viewers = Label("Подключений —", 11, Ink);
    private readonly Label live = Label("НЕТ СИГНАЛА", 10, Muted);
    private readonly Label placeholder = new() { Dock = DockStyle.Fill, Text = "Видео появится здесь\n\nВведите IP и PIN с экрана iPhone", TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.FromArgb(10, 15, 23), ForeColor = Muted, Font = new Font("Segoe UI", 15) };
    private readonly Panel header = new() { Dock = DockStyle.Top, Height = 76 };
    private readonly Panel sidebar = new() { Dock = DockStyle.Left, Width = 290, BackColor = Surface };
    private readonly Panel videoArea = new() { Dock = DockStyle.Fill, Padding = new Padding(24, 0, 24, 20) };
    private readonly Panel videoTop = new() { Dock = DockStyle.Top, Height = 55 };
    private readonly Panel videoBottom = new() { Dock = DockStyle.Bottom, Height = 66 };
    private readonly VideoView video = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    private readonly LibVLC vlc;
    private readonly LibVLCSharp.Shared.MediaPlayer player;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    private CameraConnection? current;
    private Media? media;
    private CancellationTokenSource? cancellation;
    private Task? session;
    private bool closing, closeAllowed, fullScreen;
    private Rectangle previousBounds;
    private FormWindowState previousState;
    private long previousFrames, previousBytes, measurementTime;
    private long lastDisplayed, displayedAt;
    private string? terminalError;

    public MainForm()
    {
        Text = "MagerCam — камера iPhone";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        ClientSize = new Size(1120, 720);
        MinimumSize = new Size(940, 660);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(15, 23, 36);
        ForeColor = Ink;
        KeyPreview = true;
        BuildLayout();
        var saved = Preferences.Load();
        host.Text = saved.Host;
        port.Value = Math.Clamp(saved.Port, 1, 65535);
        reconnect.Checked = saved.Reconnect;
        pin.MaxLength = 6;
        pin.UseSystemPasswordChar = true;
        pin.PlaceholderText = "6 цифр";
        host.PlaceholderText = "192.168.1.23";
        host.MaxLength = 15;
        vlc = new LibVLC("--no-video-title-show", "--no-audio", "--no-snapshot-preview");
        player = new LibVLCSharp.Shared.MediaPlayer(vlc) { EnableMouseInput = false, EnableKeyInput = false, EnableHardwareDecoding = false };
        video.MediaPlayer = player;
        connect.Click += async (_, _) => await ToggleConnection();
        timer.Tick += (_, _) => RefreshStats();
        timer.Start();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F11 || (e.KeyCode == Keys.Escape && fullScreen))
            { SetFullScreen(!fullScreen); e.Handled = true; }
        };
        FormClosing += OnClosing;
        AcceptButton = connect;
    }

    private void BuildLayout()
    {
        header.Width = ClientSize.Width;
        var title = Label("MagerCam", 23, Ink, 240, 48);
        title.Font = new Font("Segoe UI Semibold", 23);
        title.Location = new Point(24, 12);
        header.Controls.Add(title);
        var subtitle = Label("iPhone → Windows   /   Wi-Fi", 11, Muted, 300, 32);
        subtitle.Location = new Point(282, 26);
        header.Controls.Add(subtitle);
        var help = Button("Как подключить");
        help.Size = new Size(155, 36);
        help.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        help.Location = new Point(ClientSize.Width - 180, 20);
        help.Click += (_, _) => MessageBox.Show(this,
            "1. Подключите iPhone и ПК к одной сети Wi-Fi.\n2. Откройте MagerCam на iPhone и нажмите «Запустить камеру».\n3. Введите IPv4, порт 4848 и PIN с iPhone.\n4. Нажмите «Подключить камеру».\n\nПриложение iPhone должно оставаться открытым. «Отключить» завершает только просмотр на ПК.\n\nВ OBS можно использовать источник MagerCam напрямую с теми же IP и PIN. Это окно не создаёт виртуальную веб-камеру.",
            "Подключение камеры", MessageBoxButtons.OK, MessageBoxIcon.Information);
        header.Controls.Add(help);

        var fields = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(20, 20, 16, 10) };
        fields.Controls.Add(Label("ПОДКЛЮЧЕНИЕ", 9, Accent, 250, 26));
        fields.Controls.Add(Label("Камера iPhone", 18, Ink, 250, 42));
        fields.Controls.Add(Label("Запустите камеру на телефоне\nи введите данные с его экрана.", 10, Muted, 250, 55));
        fields.Controls.Add(Label("IP-адрес", 10, Muted, 250, 24));
        host.Width = 250; fields.Controls.Add(host);
        var pair = new TableLayoutPanel { Width = 250, Height = 80, Margin = new Padding(0, 12, 0, 8), ColumnCount = 2, RowCount = 2 };
        pair.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pair.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        pair.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        pair.Controls.Add(Label("Порт", 10, Muted, 90, 24), 0, 0);
        pair.Controls.Add(Label("PIN", 10, Muted, 145, 24), 1, 0);
        port.BackColor = Color.FromArgb(36, 47, 65); port.ForeColor = Ink; port.BorderStyle = BorderStyle.FixedSingle;
        pair.Controls.Add(port, 0, 1);
        pin.Width = 145; pair.Controls.Add(pin, 1, 1);
        fields.Controls.Add(pair);
        connect.Size = new Size(250, 44); connect.Margin = new Padding(0, 0, 0, 8);
        fields.Controls.Add(connect);
        fields.Controls.Add(reconnect);
        fields.Controls.Add(connectionState);
        fields.Controls.Add(Label("ТЕКУЩИЙ РЕЖИМ", 9, Accent, 250, 30));
        fields.Controls.Add(Label("Задняя камера\n1280 × 720 · 30 fps\nH.264 · без звука", 11, Muted, 250, 82));
        sidebar.Controls.Add(fields);

        var fullscreen = Button("На весь экран · F11");
        fullscreen.Dock = DockStyle.Right; fullscreen.Width = 180;
        fullscreen.Click += (_, _) => SetFullScreen(!fullScreen);
        deviceName.Dock = DockStyle.Fill;
        videoTop.Padding = new Padding(0, 8, 0, 12);
        videoTop.Controls.Add(deviceName); videoTop.Controls.Add(fullscreen);
        var stats = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(0, 10, 0, 0) };
        for (var i = 0; i < 3; i++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        stats.Controls.Add(format, 0, 0); stats.Controls.Add(rate, 1, 0); stats.Controls.Add(viewers, 2, 0);
        stats.Controls.Add(live, 0, 1); stats.SetColumnSpan(live, 3);
        videoBottom.Controls.Add(stats);
        var canvas = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
        canvas.Controls.Add(video); canvas.Controls.Add(placeholder); placeholder.BringToFront();
        videoArea.Controls.Add(canvas); videoArea.Controls.Add(videoBottom); videoArea.Controls.Add(videoTop);
        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(videoArea); body.Controls.Add(sidebar);
        Controls.Add(body); Controls.Add(header);
    }

    private async Task ToggleConnection()
    {
        if (cancellation != null)
        {
            connect.Enabled = false;
            cancellation.Cancel();
            current?.Dispose(); // Unblock the decoder's synchronous read before stopping it.
            if (session != null) await session;
            return;
        }
        var ip = host.Text.Trim();
        var code = pin.Text.Trim();
        if (!System.Net.IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetwork || code.Length != 6 || code.Any(c => c is < '0' or > '9'))
        { State("Введите IPv4 и шестизначный PIN с экрана iPhone.", true); return; }
        new Preferences(ip, (int)port.Value, reconnect.Checked).Save(); // Never persist the PIN.
        cancellation = new CancellationTokenSource();
        host.Enabled = pin.Enabled = port.Enabled = false;
        connect.Text = "Отключить";
        terminalError = null;
        session = RunSession(ip, (int)port.Value, code, cancellation.Token);
        await session;
    }

    private async Task RunSession(string ip, int tcpPort, string code, CancellationToken token)
    {
        try
        {
            do
            {
                CameraConnection? connection = null;
                LoopbackVideoBridge? bridge = null;
                Media? attemptMedia = null;
                var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnEnd(object? _, EventArgs e) => ended.TrySetResult();
                void OnError(object? _, EventArgs e) => ended.TrySetException(new CameraException("Не удалось воспроизвести видео от камеры."));
                void OnStreamError(Exception e) => ended.TrySetException(e);
                try
                {
                    State("Подключение к iPhone…");
                    connection = await CameraConnection.ConnectAsync(ip, tcpPort, code, token);
                    current = connection;
                    bridge = new LoopbackVideoBridge(connection.Video);
                    attemptMedia = new Media(vlc, bridge.Location, FromType.FromLocation, ":demux=h264", ":h264-fps=30", ":live-caching=100", ":network-caching=100", ":no-audio");
                    media = attemptMedia;
                    previousFrames = previousBytes = lastDisplayed = 0;
                    measurementTime = displayedAt = Stopwatch.GetTimestamp();
                    connection.Video.Failed += OnStreamError;
                    player.EndReached += OnEnd; player.EncounteredError += OnError;
                    if (!player.Play(attemptMedia)) throw new CameraException("Не удалось запустить видеоплеер.", false);
                    State("Соединение установлено. Ожидаю видео…");
                    var completed = await Task.WhenAny(ended.Task, bridge.Completion).WaitAsync(token);
                    await completed;
                    throw new CameraException("Камера остановила передачу.");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception error) when (error is IOException or SocketException or OperationCanceledException)
                {
                    if (token.IsCancellationRequested) break;
                    terminalError = Friendly(error);
                    if (error is CameraException { Retryable: false } || !reconnect.Checked)
                    { State(terminalError, true); break; }
                    State(terminalError + "\nПовтор через 3 секунды…");
                }
                finally
                {
                    media = null; current = null;
                    player.EndReached -= OnEnd; player.EncounteredError -= OnError;
                    if (connection != null) { connection.Video.Failed -= OnStreamError; connection.Dispose(); }
                    bridge?.Dispose();
                    await Task.Run(() => player.Stop()); // Keep the UI message loop alive during native teardown.
                    player.Media = null;
                    attemptMedia?.Dispose();
                    if (bridge != null) { try { await bridge.Completion; } catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException) { } }
                    placeholder.Visible = true; placeholder.BringToFront();
                    live.Text = "НЕТ СИГНАЛА"; live.ForeColor = Muted;
                    rate.Text = "Поток —"; viewers.Text = "Подключений —";
                }
                await Task.Delay(3000, token);
                if (!reconnect.Checked) break;
            } while (!token.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            cancellation?.Dispose(); cancellation = null;
            host.Enabled = pin.Enabled = port.Enabled = connect.Enabled = true;
            connect.Text = "Подключить камеру";
            if (token.IsCancellationRequested) State("Отключено. Камера на iPhone продолжает работать.");
            else if (terminalError != null) State(terminalError, true);
            deviceName.Text = "ПРОСМОТР КАМЕРЫ";
        }
    }

    private void RefreshStats()
    {
        if (current == null || media == null) return;
        var videoStream = current.Video;
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(measurementTime, now).TotalSeconds;
        var frameCount = videoStream.Frames; var bytes = videoStream.VideoBytes;
        var displayed = (long)media.Statistics.DisplayedPictures;
        if (displayed > lastDisplayed)
        {
            lastDisplayed = displayed; displayedAt = now;
            placeholder.Visible = false;
            live.Text = "●  ПРЯМОЙ ЭФИР"; live.ForeColor = Color.FromArgb(82, 211, 163);
            State("Камера подключена"); terminalError = null;
        }
        if (Stopwatch.GetElapsedTime(displayedAt, now).TotalSeconds > 10)
        {
            current.Dispose(); // Forces reconnect if native decoding stalls while packets still arrive.
            State("Видео не воспроизводится. Восстанавливаю соединение…");
            return;
        }
        if (elapsed >= 0.9)
        {
            rate.Text = $"{(frameCount - previousFrames) / elapsed:0} fps  ·  {(bytes - previousBytes) * 8 / elapsed / 1_000_000:0.0} Мбит/с";
            previousFrames = frameCount; previousBytes = bytes; measurementTime = now;
        }
        if (videoStream.Status is { } info)
        {
            deviceName.Text = info.Name;
            format.Text = $"{info.Width} × {info.Height} · H.264";
            viewers.Text = $"Подключений: {info.Clients}";
            if (info.Hot) State("iPhone нагревается. Дайте телефону остыть.", true);
        }
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (closeAllowed) return;
        e.Cancel = true;
        if (closing) return;
        closing = true; timer.Stop(); Enabled = false;
        cancellation?.Cancel(); current?.Dispose();
        if (session != null) await session;
        video.MediaPlayer = null;
        player.Dispose(); vlc.Dispose(); timer.Dispose();
        closeAllowed = true; Close();
    }

    private void SetFullScreen(bool value)
    {
        if (fullScreen == value) return;
        fullScreen = value;
        if (value)
        {
            previousBounds = Bounds; previousState = WindowState;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = Screen.FromControl(this).Bounds;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            Bounds = previousBounds; WindowState = previousState;
        }
        header.Visible = sidebar.Visible = videoTop.Visible = videoBottom.Visible = !value;
        videoArea.Padding = value ? Padding.Empty : new Padding(24, 0, 24, 20);
    }

    private void State(string text, bool error = false)
    { connectionState.Text = text; connectionState.ForeColor = error ? Color.FromArgb(255, 145, 145) : Muted; }

    private static string Friendly(Exception error) => error switch
    {
        CameraException => error.Message,
        SocketException => "Нет связи с iPhone. Проверьте IP и общую сеть Wi-Fi.",
        EndOfStreamException => "iPhone завершил передачу. Откройте приложение камеры.",
        _ => "Связь прервалась. Проверьте Wi-Fi и запущенную камеру на iPhone."
    };

    private static TextBox Field(string accessibleName) => new() { AccessibleName = accessibleName, Width = 250, AutoSize = false, Height = 34, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(36, 47, 65), ForeColor = Ink, Font = new Font("Segoe UI", 12), Margin = new Padding(0, 0, 0, 5) };
    private static Label Label(string text, float size, Color color, int width = 240, int height = 28) => new() { Text = text, Font = new Font("Segoe UI", size), ForeColor = color, Width = width, Height = height, AutoEllipsis = true, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft };
    private static Button Button(string text, bool primary = false)
    {
        var result = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Surface, ForeColor = primary ? Color.FromArgb(20, 27, 40) : Ink, Cursor = Cursors.Hand, Font = new Font("Segoe UI Semibold", 10), UseVisualStyleBackColor = false };
        result.FlatAppearance.BorderSize = 0;
        return result;
    }
}
