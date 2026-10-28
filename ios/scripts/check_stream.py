"""Check the actual iPhone MC stream from Windows/macOS/Linux (Python standard library).

python ios/scripts/check_stream.py 192.168.1.23
PIN is prompted without echo. No captured camera data is saved by default.
"""
import argparse
import getpass
import json
import socket
import struct
import time


def read_exact(sock, count):
    result = bytearray()
    while len(result) < count:
        chunk = sock.recv(count - len(result))
        if not chunk:
            raise RuntimeError('Server closed the connection')
        result.extend(chunk)
    return bytes(result)


def check(host, port, pin, seconds):
    frames = keys = 0
    configured = False
    waiting_key = True
    last_pts = -1
    with socket.create_connection((host, port), timeout=5) as sock:
        sock.settimeout(6)
        request = (f'GET /stream?w=1920&h=1080&fps=30&bitrate=8000&audio=1&camera=back&codec=hevc HTTP/1.1\r\n'
                   f'Host: {host}\r\nX-MagerCam-Key: {pin}\r\n\r\n')
        sock.sendall(request.encode('ascii'))
        header = bytearray()
        while not header.endswith(b'\r\n\r\n'):
            header.extend(read_exact(sock, 1))
            if len(header) > 4096:
                raise RuntimeError('HTTP response header is too large for existing clients')
        if not header.startswith(b'HTTP/1.1 200 '):
            raise RuntimeError('HTTP: ' + header.split(b'\r\n')[0].decode())
        if b'Transfer-Encoding:' in header:
            raise RuntimeError('MC must not use HTTP chunk framing')
        deadline = time.monotonic() + seconds
        seen_status = False
        while time.monotonic() < deadline:
            magic, kind, flags, length, pts = struct.unpack('>2sBBIQ', read_exact(sock, 16))
            if magic != b'MC' or length > 32 * 1024 * 1024:
                raise RuntimeError('Invalid MC header')
            body = read_exact(sock, length)
            if kind == 1:
                if flags & 2 or not body.startswith(b'\0\0\0\1'):
                    raise RuntimeError('Expected H.264 Annex B configuration')
                configured = True
                waiting_key = True
            elif kind == 2:
                if not configured or (waiting_key and not flags & 1):
                    raise RuntimeError('Video arrived before configuration/keyframe')
                if not body.startswith(b'\0\0\0\1') or pts <= last_pts:
                    raise RuntimeError('Invalid Annex B access unit or non-monotonic PTS')
                last_pts = pts
                waiting_key = False
                frames += 1
                keys += bool(flags & 1)
            elif kind == 3 and body != bytes([0, 0, 0xbb, 0x80, 1]):
                raise RuntimeError('Unexpected audio format')
            elif kind == 5:
                state = json.loads(body)
                if state.get('codec') != 'h264' or state.get('audio') is not False:
                    raise RuntimeError('Incorrect MVP status')
                if not seen_status:
                    print('Device:', state.get('name'), 'format:', state.get('w'), 'x', state.get('h'), '@', state.get('fps'))
                seen_status = True
        if frames < 24 or not keys or not seen_status:
            raise RuntimeError(f'Not enough valid video: frames={frames}, keys={keys}, status={seen_status}')
        print(f'PASS: {frames} video frames, {keys} keyframes, ordered PTS, valid MC framing.')
        print('This checks transport/structure, not visual decode or latency. Verify the image in OBS.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('host')
    parser.add_argument('--port', type=int, default=4848)
    parser.add_argument('--seconds', type=int, default=10)
    args = parser.parse_args()
    if args.seconds < 2:
        parser.error('--seconds must be at least 2')
    pin = getpass.getpass('PIN from iPhone: ')
    if len(pin) != 6 or not pin.isascii() or not pin.isdigit():
        parser.error('PIN must have six ASCII digits')
    try:
        check(args.host, args.port, pin, args.seconds)
    except (OSError, RuntimeError, ValueError) as exc:
        raise SystemExit('FAIL: ' + str(exc))
