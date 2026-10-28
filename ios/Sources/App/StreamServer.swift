import Foundation
import Network

/// All access, including callbacks, happens on the controller's serial queue.
final class StreamServer {
    private let queue: DispatchQueue
    private let pin: String
    private var listener: NWListener?
    private var peers: [UUID: Peer] = [:]
    private var config: Data?
    private var timer: DispatchSourceTimer?
    private var failures: [String: (count: Int, since: TimeInterval)] = [:]
    var status: () -> Data = { Data("{}".utf8) }
    var onKeyFrame: (() -> Void)?
    var onClients: ((Int) -> Void)?
    var onReady: (() -> Void)?
    var onError: ((String) -> Void)?

    init(queue: DispatchQueue, pin: String) { self.queue = queue; self.pin = pin }

    func start() throws {
        stop()
        let tcp = NWProtocolTCP.Options()
        tcp.noDelay = true
        let parameters = NWParameters(tls: nil, tcp: tcp)
        parameters.allowLocalEndpointReuse = true
        let listener = try NWListener(using: parameters, on: 4848)
        self.listener = listener
        listener.stateUpdateHandler = { [weak self, weak listener] state in
            guard let self, let listener, self.listener === listener else { return }
            switch state {
            case .ready: self.onReady?()
            case .failed(let error): self.onError?("Сервер: \(error.localizedDescription)")
            default: break
            }
        }
        listener.newConnectionHandler = { [weak self] connection in self?.accept(connection) }
        listener.start(queue: queue)
        let timer = DispatchSource.makeTimerSource(queue: queue)
        timer.schedule(deadline: .now() + 1, repeating: 1)
        timer.setEventHandler { [weak self] in self?.tick() }
        self.timer = timer
        timer.resume()
    }

    func stop() {
        timer?.cancel(); timer = nil
        listener?.cancel(); listener = nil
        for peer in peers.values { peer.connection.cancel() }
        peers.removeAll(); config = nil; onClients?(0)
    }

    func publish(_ frame: EncodedFrame) {
        if config != frame.configuration {
            config = frame.configuration
            // Existing peers need a configuration barrier and a fresh sync sample.
            for peer in peers.values where peer.streaming {
                peer.waitingKey = true
                peer.needsConfig = true
            }
        }
        for peer in Array(peers.values) where peer.streaming {
            if peer.waitingKey && !frame.key { continue }
            if peer.needsConfig {
                enqueue(MCProtocol.packet(.videoConfig, payload: frame.configuration, key: true), for: peer)
                peer.needsConfig = false
            }
            peer.waitingKey = false
            enqueue(MCProtocol.packet(.videoFrame, payload: frame.bytes, pts: frame.pts, key: frame.key), for: peer)
        }
    }

    private func accept(_ connection: NWConnection) {
        guard peers.count < 12 else { connection.cancel(); return }
        let peer = Peer(connection)
        peers[peer.id] = peer
        connection.stateUpdateHandler = { [weak self, weak peer] state in
            guard let self, let peer else { return }
            switch state {
            case .failed, .cancelled: self.close(peer)
            default: break
            }
        }
        connection.start(queue: queue)
        receive(peer)
    }

    private func receive(_ peer: Peer) {
        peer.connection.receive(minimumIncompleteLength: 1, maximumLength: 4096) { [weak self, weak peer] data, _, done, error in
            guard let self, let peer, self.peers[peer.id] != nil else { return }
            if done || error != nil { self.close(peer); return }
            if let data, !peer.streaming {
                peer.request.append(data)
                do {
                    if let request = try HTTPRequest.parse(peer.request) {
                        peer.request.removeAll()
                        self.handle(request, peer: peer)
                        if peer.streaming { self.receive(peer) }
                        return
                    }
                } catch { self.respond(peer, code: "400 Bad Request", text: "Invalid request"); return }
            }
            self.receive(peer)
        }
    }

    private func handle(_ request: HTTPRequest, peer: Peer) {
        if request.path == "/" {
            respond(peer, code: "200 OK", text: "MagerCam iOS: connect the MagerCam OBS source to this IP, port 4848, using the PIN shown on iPhone. H.264 1280x720@30, video only.")
            return
        }
        let now = ProcessInfo.processInfo.systemUptime
        let address: String
        if case .hostPort(let host, _) = peer.connection.endpoint { address = "\(host)" }
        else { address = "\(peer.connection.endpoint)" }
        if let f = failures[address], f.count >= 10, now - f.since < 60 {
            respond(peer, code: "429 Too Many Requests", text: "Too many attempts"); return
        }
        guard request.accessKey?.filter({ $0.isASCII && $0.isNumber }) == pin else {
            if request.accessKey != nil {
                let old = failures[address]
                failures[address] = old.map { now - $0.since < 60 ? ($0.count + 1, $0.since) : (1, now) } ?? (1, now)
            }
            respond(peer, code: "401 Unauthorized", text: "Access code required"); return
        }
        failures.removeValue(forKey: address)
        switch request.path {
        case "/api/status", "/api/set":
            // MVP is deliberately fixed-format; return actual status even if OBS sends controls.
            respond(peer, code: "200 OK", body: status(), mime: "application/json; charset=utf-8")
        case "/stream":
            guard peers.values.filter({ $0.streaming }).count < 4 else {
                respond(peer, code: "409 Conflict", text: "Too many clients"); return
            }
            peer.streaming = true
            enqueue(Data("HTTP/1.1 200 OK\r\nContent-Type: application/x-magercam\r\nCache-Control: no-cache\r\n\r\n".utf8), for: peer)
            enqueue(MCProtocol.packet(.audioConfig, payload: Data([0, 0, 0xbb, 0x80, 1])), for: peer)
            enqueue(MCProtocol.packet(.status, payload: status()), for: peer)
            onClients?(peers.values.filter { $0.streaming }.count)
            onKeyFrame?()
        default: respond(peer, code: "404 Not Found", text: "Not found")
        }
    }

    private func respond(_ peer: Peer, code: String, text: String) {
        respond(peer, code: code, body: Data(text.utf8), mime: "text/plain; charset=utf-8")
    }

    private func respond(_ peer: Peer, code: String, body: Data, mime: String) {
        var response = Data("HTTP/1.0 \(code)\r\nContent-Type: \(mime)\r\nContent-Length: \(body.count)\r\nConnection: close\r\nCache-Control: no-cache\r\n\r\n".utf8)
        response.append(body)
        peer.connection.send(content: response, completion: .contentProcessed { [weak self, weak peer] _ in
            if let peer { self?.close(peer) }
        })
    }

    private func enqueue(_ bytes: Data, for peer: Peer) {
        guard peers[peer.id] != nil else { return }
        // A stuck TCP consumer is disconnected instead of accumulating old video in memory.
        guard peer.pendingBytes + bytes.count <= 6_000_000,
              peer.pending.first.map({ ProcessInfo.processInfo.systemUptime - $0.time <= 0.5 }) ?? true else {
            close(peer); return
        }
        peer.pending.append(Item(bytes: bytes, time: ProcessInfo.processInfo.systemUptime))
        peer.pendingBytes += bytes.count
        sendNext(peer)
    }

    private func sendNext(_ peer: Peer) {
        guard !peer.sending, let item = peer.pending.first, peers[peer.id] != nil else { return }
        peer.sending = true
        peer.connection.send(content: item.bytes, completion: .contentProcessed { [weak self, weak peer] error in
            guard let self, let peer, self.peers[peer.id] != nil else { return }
            if error != nil { self.close(peer); return }
            peer.pending.removeFirst(); peer.pendingBytes -= item.bytes.count; peer.sending = false
            self.sendNext(peer)
        })
    }

    private func tick() {
        let now = ProcessInfo.processInfo.systemUptime
        failures = failures.filter { now - $0.value.since < 60 }
        for peer in Array(peers.values) {
            if !peer.streaming && now - peer.created > 5 { close(peer); continue }
            if let first = peer.pending.first, now - first.time > 2 { close(peer); continue }
            if peer.streaming { enqueue(MCProtocol.packet(.status, payload: status()), for: peer) }
        }
    }

    private func close(_ peer: Peer) {
        guard peers.removeValue(forKey: peer.id) != nil else { return }
        peer.connection.cancel(); peer.pending.removeAll()
        if peer.streaming { onClients?(peers.values.filter { $0.streaming }.count) }
    }

    private struct Item { let bytes: Data; let time: TimeInterval }
    private final class Peer {
        let id = UUID()
        let connection: NWConnection
        let created = ProcessInfo.processInfo.systemUptime
        var request = Data()
        var streaming = false
        var waitingKey = true
        var needsConfig = true
        var pending: [Item] = []
        var pendingBytes = 0
        var sending = false
        init(_ connection: NWConnection) { self.connection = connection }
    }
}
