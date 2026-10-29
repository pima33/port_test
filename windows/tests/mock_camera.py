"""Local MC camera fixture. No physical camera or personal video is used.

python windows/tests/mock_camera.py --disconnect-after 5
"""
import argparse
import json
from pathlib import Path
import re
import socket
import socketserver
import struct
import time


def packet(kind, payload=b'', pts=0, flags=0):
    return struct.pack('>2sBBIQ', b'MC', kind, flags, len(payload), pts) + payload


def read_fixture():
    data = Path(__file__).with_name('fixture.h264').read_bytes()
    nals = [b'\0\0\0\1' + n for n in re.split(b'\x00\x00\x00?\x01', data) if n]
    config = b''.join(n for n in nals if n[4] & 31 in (7, 8))
    frames, current = [], []
    for nal in nals:
        kind = nal[4] & 31
        if kind == 9 and current:
            frames.append(b''.join(current))
            current = []
        if kind not in (7, 8):
            current.append(nal)
    if current:
        frames.append(b''.join(current))
    return config, [(frame, any(n and n[0] & 31 == 5 for n in re.split(b'\x00\x00\x00?\x01', frame))) for frame in frames]


class Handler(socketserver.BaseRequestHandler):
    def handle(self):
        self.request.settimeout(5)
        self.request.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        try:
            header = bytearray()
            while not header.endswith(b'\r\n\r\n') and len(header) < 16384:
                chunk = self.request.recv(1)
                if not chunk:
                    return
                header.extend(chunk)
            if b'X-MagerCam-Key: 123456\r\n' not in header:
                self.request.sendall(b'HTTP/1.0 401 Unauthorized\r\nContent-Length: 0\r\n\r\n')
                return
            if options.status != 200:
                self.request.sendall(f'HTTP/1.0 {options.status} Fixture\r\nContent-Length: 0\r\n\r\n'.encode())
                return
            response = b'HTTP/1.1 200 OK\r\nContent-Type: application/x-magercam\r\n\r\n'
            for offset in range(0, len(response), 7):
                self.request.sendall(response[offset:offset + 7])
            state = json.dumps(dict(name='Тестовая камера', w=1280, h=720, fps=30, clients=1, camera_open=True, hot=False, codec='h264', audio=False)).encode()
            self.request.sendall(packet(3, bytes([0, 0, 0xbb, 0x80, 1])) + packet(5, state) + packet(1, configuration, flags=1))
            started = time.monotonic()
            count = 0
            while not options.disconnect_after or time.monotonic() - started < options.disconnect_after:
                frame, key = frames[count % len(frames)]
                body = packet(2, frame, pts=1000 + count * 33333, flags=int(key))
                # Deliberately fragment both headers and payloads across TCP writes.
                self.request.sendall(body[:5])
                self.request.sendall(body[5:])
                count += 1
                if count % 30 == 0:
                    self.request.sendall(packet(5, state))
                time.sleep(max(0, started + count / 30 - time.monotonic()))
        except (OSError, TimeoutError):
            pass


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port', type=int, default=4848)
    parser.add_argument('--disconnect-after', type=float, default=0)
    parser.add_argument('--status', type=int, choices=[200, 401, 409, 429], default=200)
    options = parser.parse_args()
    configuration, frames = read_fixture()
    class Server(socketserver.ThreadingTCPServer):
        allow_reuse_address = True
        daemon_threads = True
    with Server(('127.0.0.1', options.port), Handler) as server:
        print(f'MC test camera: 127.0.0.1:{options.port}, PIN 123456, {len(frames)} fixture frames', flush=True)
        server.serve_forever()
