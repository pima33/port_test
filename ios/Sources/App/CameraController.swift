import AVFoundation
import UIKit
import Combine
import Darwin

final class CameraController: NSObject, ObservableObject, AVCaptureVideoDataOutputSampleBufferDelegate {
    @Published private(set) var message = "Нажмите «Запустить камеру»"
    @Published private(set) var address = "Wi-Fi не подключён"
    @Published private(set) var clients = 0
    @Published private(set) var requested = false
    let session = AVCaptureSession()
    let pin: String

    private let queue = DispatchQueue(label: "MagerCam.capture", qos: .userInitiated)
    private var server: StreamServer!
    private var encoder: H264Encoder?
    private var desired = false
    private var foreground = true
    private var running = false
    private var generation = 0
    private var connected = 0
    private var observers: [NSObjectProtocol] = []
    private var width = 1280
    private var height = 720
    private var encodedCount = 0
    private var lastVideoTime: TimeInterval = 0
    private var watchdog: DispatchSourceTimer?
    private var addresses: [String] = []
    private var encoderSize: (Int, Int)?

    override init() {
        pin = String(Int.random(in: 100_000...999_999))
        super.init()
        server = StreamServer(queue: queue, pin: pin)
        server.status = { [weak self] in self?.statusJSON() ?? Data("{}".utf8) }
        server.onKeyFrame = { [weak self] in self?.encoder?.requestKeyFrame() }
        server.onClients = { [weak self] count in
            guard let self else { return }
            self.connected = count
            self.updateUI()
        }
        server.onReady = { [weak self] in self?.updateUI() }
        server.onError = { [weak self] error in self?.fail(error) }
        observe(AVCaptureSession.runtimeErrorNotification) { [weak self] note in
            let error = note.userInfo?[AVCaptureSessionErrorKey] as? Error
            self?.fail("Ошибка камеры: \(error?.localizedDescription ?? "перезапустите трансляцию")")
        }
        observe(AVCaptureSession.wasInterruptedNotification) { [weak self] _ in
            guard let self else { return }
            self.stopResources()
            self.updateUI("Камера временно недоступна. Ожидаю возобновления.")
        }
        observe(AVCaptureSession.interruptionEndedNotification) { [weak self] _ in
            guard let self, self.desired && self.foreground else { return }
            self.startResources()
        }
    }

    private func observe(_ name: Notification.Name, handler: @escaping (Notification) -> Void) {
        observers.append(NotificationCenter.default.addObserver(forName: name, object: session, queue: nil) { [weak self] note in
            self?.queue.async { handler(note) }
        })
    }

    func toggle() {
        queue.async {
            self.desired.toggle()
            if self.desired { self.authorizeAndStart() }
            else { self.stopResources(); self.updateUI("Трансляция остановлена") }
        }
    }

    func setForeground(_ value: Bool) {
        queue.async {
            self.foreground = value
            if value && self.desired { self.authorizeAndStart() }
            else if !value {
                self.stopResources()
                self.updateUI(self.desired ? "Трансляция приостановлена в фоне" : "Трансляция остановлена")
            }
        }
    }

    private func authorizeAndStart() {
        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .authorized: startResources()
        case .notDetermined:
            updateUI("Разрешите доступ к камере")
            AVCaptureDevice.requestAccess(for: .video) { [weak self] allowed in
                guard let self else { return }
                self.queue.async {
                    guard self.desired else { return }
                    if allowed { self.startResources() }
                    else { self.fail("Разрешите доступ к камере в Настройках iPhone.") }
                }
            }
        default: fail("Разрешите доступ к камере в Настройках iPhone.")
        }
    }

    private func startResources() {
        guard desired && foreground && !running else { return }
        do {
            stopResources()
            guard let device = AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .back) else {
                throw CameraError.unavailable
            }
            session.beginConfiguration()
            for input in session.inputs { session.removeInput(input) }
            for output in session.outputs { session.removeOutput(output) }
            do {
                guard session.canSetSessionPreset(.hd1280x720) else { throw CameraError.unsupported }
                session.sessionPreset = .hd1280x720
                let input = try AVCaptureDeviceInput(device: device)
                guard session.canAddInput(input) else { throw CameraError.unavailable }
                session.addInput(input)
                let output = AVCaptureVideoDataOutput()
                output.alwaysDiscardsLateVideoFrames = true
                output.videoSettings = [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange]
                output.setSampleBufferDelegate(self, queue: queue)
                guard session.canAddOutput(output) else { throw CameraError.unavailable }
                session.addOutput(output)
                if let connection = output.connection(with: .video) {
                    if connection.isVideoRotationAngleSupported(0) { connection.videoRotationAngle = 0 }
                    if connection.isVideoMirroringSupported {
                        connection.automaticallyAdjustsVideoMirroring = false
                        connection.isVideoMirrored = false
                    }
                }
                session.commitConfiguration()
            } catch {
                session.commitConfiguration()
                throw error
            }
            try device.lockForConfiguration()
            if device.activeFormat.videoSupportedFrameRateRanges.contains(where: { $0.minFrameRate <= 30 && $0.maxFrameRate >= 30 }) {
                device.activeVideoMinFrameDuration = CMTime(value: 1, timescale: 30)
                device.activeVideoMaxFrameDuration = CMTime(value: 1, timescale: 30)
            } else { device.unlockForConfiguration(); throw CameraError.unsupported }
            if device.isFocusModeSupported(.continuousAutoFocus) { device.focusMode = .continuousAutoFocus }
            if device.isExposureModeSupported(.continuousAutoExposure) { device.exposureMode = .continuousAutoExposure }
            device.unlockForConfiguration()
            generation += 1
            running = true
            addresses = Self.localIPv4()
            try server.start()
            session.startRunning()
            lastVideoTime = ProcessInfo.processInfo.systemUptime
            let timer = DispatchSource.makeTimerSource(queue: queue)
            timer.schedule(deadline: .now() + 1, repeating: 1)
            timer.setEventHandler { [weak self] in
                guard let self, self.running else { return }
                self.addresses = Self.localIPv4()
                if ProcessInfo.processInfo.thermalState == .critical {
                    self.fail("iPhone перегрелся. Дайте ему остыть и запустите камеру снова.")
                } else if ProcessInfo.processInfo.systemUptime - self.lastVideoTime > 6 {
                    self.fail("Нет кадров от камеры. Запустите трансляцию снова.")
                } else { self.updateUI() }
            }
            watchdog = timer; timer.resume()
            updateUI()
        } catch { fail("Не удалось запустить камеру: \(error.localizedDescription)") }
    }

    func captureOutput(_ output: AVCaptureOutput, didOutput sample: CMSampleBuffer, from connection: AVCaptureConnection) {
        guard running, let buffer = CMSampleBufferGetImageBuffer(sample) else { return }
        let w = CVPixelBufferGetWidth(buffer), h = CVPixelBufferGetHeight(buffer)
        if encoder == nil {
            let current = generation
            let encoder = H264Encoder()
            encoder.onFrame = { [weak self] frame in
                self?.queue.async { [weak self] in
                    guard let self, self.running, self.generation == current else { return }
                    self.encodedCount += 1
                    self.lastVideoTime = ProcessInfo.processInfo.systemUptime
                    self.server.publish(frame)
                }
            }
            encoder.onError = { [weak self] error in
                self?.queue.async { [weak self] in
                    guard let self, self.running, self.generation == current else { return }
                    self.fail(error)
                }
            }
            do {
                try encoder.start(width: Int32(w), height: Int32(h))
                self.encoder = encoder; encoderSize = (w, h); width = w; height = h
            } catch { fail("H.264 encoder недоступен: \(error)"); return }
        }
        guard encoderSize?.0 == w && encoderSize?.1 == h else {
            fail("Камера изменила размер кадра. Перезапустите трансляцию."); return
        }
        encoder?.encode(sample)
    }

    private func stopResources() {
        generation += 1
        running = false
        watchdog?.cancel(); watchdog = nil
        server.stop()
        session.stopRunning()
        encoder?.stop(); encoder = nil; encoderSize = nil; encodedCount = 0
    }

    private func fail(_ reason: String) {
        desired = false
        stopResources()
        updateUI(reason)
    }

    private func updateUI(_ text: String? = nil) {
        let count = connected, intent = desired, active = running
        let ip = addresses.isEmpty ? "Wi-Fi не подключён" : addresses.joined(separator: " / ")
        let state = text ?? (active ? (count > 0 ? "Передача видео в OBS" : "Готово к подключению OBS") : "Трансляция остановлена")
        DispatchQueue.main.async {
            self.clients = count; self.requested = intent; self.address = ip; self.message = state
            UIApplication.shared.isIdleTimerDisabled = active
        }
    }

    private func statusJSON() -> Data {
        let object: [String: Any] = [
            "name": "MagerCam iPhone", "ver": 2, "w": width, "h": height, "fps": 30,
            "bitrate": 4000, "bitrate_now": 4000, "camera": "back", "front": false,
            "cameras": [["id": "back", "front": false, "label": "Основная камера"]],
            "sizes": ["1280x720"], "fps_list": [30], "orient": 0, "orientation_mode": "landscape",
            "audio": false, "codec": "h264", "codec_pref": "h264", "hevc": false,
            "auth": true, "clients": connected, "viewers": 0, "camera_open": running,
            "battery": -1, "charging": false, "rec": false, "hot": ProcessInfo.processInfo.thermalState == .serious,
            "framing": false, "framing_available": false, "bg": "off", "bg_image": false,
            "torch": false, "zoom": 1, "zoom_min": 1, "zoom_max": 1, "ev": 0, "ev_min": 0, "ev_max": 0,
            "focus": "continuous", "wb": "auto", "stab": false, "lock": false, "mirror": false,
            "abr": false, "profile": "", "port": 4848, "ips": addresses,
            "ios_mvp": true, "encoded_frames": encodedCount,
            "limitations": "Fixed back camera, H.264 720p30, video only; remote controls are not implemented."
        ]
        return (try? JSONSerialization.data(withJSONObject: object, options: [.sortedKeys])) ?? Data("{}".utf8)
    }

    private static func localIPv4() -> [String] {
        var first: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&first) == 0 else { return [] }
        defer { freeifaddrs(first) }
        var result: [String] = []
        var cursor = first
        while let entry = cursor {
            let info = entry.pointee
            defer { cursor = info.ifa_next }
            guard let address = info.ifa_addr, address.pointee.sa_family == UInt8(AF_INET),
                  info.ifa_flags & UInt32(IFF_UP) != 0, info.ifa_flags & UInt32(IFF_LOOPBACK) == 0,
                  info.ifa_flags & UInt32(IFF_BROADCAST) != 0 else { continue }
            var host = [CChar](repeating: 0, count: Int(NI_MAXHOST))
            if getnameinfo(address, socklen_t(address.pointee.sa_len), &host, socklen_t(host.count), nil, 0, NI_NUMERICHOST) == 0 {
                result.append(String(cString: host))
            }
        }
        return Array(Set(result)).sorted()
    }

    enum CameraError: LocalizedError {
        case unavailable, unsupported
        var errorDescription: String? { self == .unavailable ? "Задняя камера недоступна" : "Режим 720p30 не поддерживается" }
    }
}
