import SwiftUI
import AVFoundation

@main
struct MagerCamApp: App {
    @StateObject private var camera = CameraController()
    @Environment(\.scenePhase) private var scenePhase

    var body: some Scene {
        WindowGroup {
            CameraScreen(camera: camera)
                .preferredColorScheme(.dark)
                .onChange(of: scenePhase) { _, phase in
                    if phase == .active { camera.setForeground(true) }
                    else if phase == .background { camera.setForeground(false) }
                }
        }
    }
}

private struct CameraScreen: View {
    @ObservedObject var camera: CameraController

    var body: some View {
        HStack(spacing: 20) {
            Preview(session: camera.session)
                .aspectRatio(16 / 9, contentMode: .fit)
                .clipShape(RoundedRectangle(cornerRadius: 16))
                .overlay(alignment: .bottomLeading) {
                    Text("H.264 · 720p · 30 fps").font(.caption.monospaced())
                        .padding(8).background(.black.opacity(0.6)).padding(10)
                }
            ScrollView {
                VStack(alignment: .leading, spacing: 12) {
                    Text("MagerCam").font(.title.bold())
                    Text(camera.message).foregroundStyle(.secondary)
                    Text(camera.address).font(.headline.monospaced()).textSelection(.enabled)
                    Text("Порт 4848 · PIN \(camera.pin)").font(.headline.monospaced())
                    Text("Подключений: \(camera.clients)")
                    Button(camera.requested ? "Остановить" : "Запустить камеру") { camera.toggle() }
                        .buttonStyle(.borderedProminent).controlSize(.large)
                    Text("В OBS добавьте источник MagerCam, выберите Wi-Fi и укажите IP, порт и PIN. Оставьте приложение открытым.")
                        .font(.footnote).foregroundStyle(.secondary)
                    Text("Первая версия передаёт только видео. Выберите 1280×720 / 30 fps, отключите звук и управление камерой в OBS.")
                        .font(.footnote).foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .frame(width: 290)
        }
        .padding(20)
        .background(Color(white: 0.06))
    }
}

private struct Preview: UIViewRepresentable {
    let session: AVCaptureSession
    func makeUIView(context: Context) -> PreviewView {
        let view = PreviewView()
        view.layerView.session = session
        view.layerView.videoGravity = .resizeAspect
        return view
    }
    func updateUIView(_ view: PreviewView, context: Context) {
        if let connection = view.layerView.connection, connection.isVideoRotationAngleSupported(0) {
            connection.videoRotationAngle = 0
        }
    }
}

private final class PreviewView: UIView {
    override class var layerClass: AnyClass { AVCaptureVideoPreviewLayer.self }
    var layerView: AVCaptureVideoPreviewLayer { layer as! AVCaptureVideoPreviewLayer }
}
