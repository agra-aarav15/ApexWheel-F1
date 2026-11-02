#!/usr/bin/env python3
"""
ApexWheel F1 — Universal Cross-Platform Companion (Linux, macOS, Windows)
High-performance phone steering wheel receiver & EA Sports F1 UDP telemetry hub.

Protocols Supported:
  - UDP 5005: 60Hz inbound Gyropad/Apex phone motion inputs + 20Hz bidirectional heartbeat
  - UDP 5006: Discovery broadcast & instant listener (PW2 ANNOUNCE / PW2 DISCOVER / PW2 PAIRME)
  - TCP 5007: Forward pairing server (PAIR / HELLO handshakes)
  - TCP 7507: Reverse pairing client (automatic connection on PW2 PAIRME fallback)
  - UDP 20777: EA Sports F1 22/23/24/25 UDP Telemetry decoder
  - TCP 8080: Modern Web Cockpit Dashboard & REST API
  - Native Linux Kernel /dev/uinput Xbox 360 controller emulation
"""

import os
import sys
import time
import socket
import struct
import threading
import http.server
import socketserver
import json
import webbrowser
import platform

IS_LINUX = sys.platform.startswith('linux')
IS_MACOS = sys.platform == 'darwin'
IS_WINDOWS = sys.platform.startswith('win')

# =============================================================================
# 1. Linux Kernel /dev/uinput Virtual Gamepad Driver (Zero-Dependency)
# =============================================================================
class LinuxUInputGamepad:
    def __init__(self):
        self.fd = -1
        self.available = False
        if not IS_LINUX:
            return

        try:
            import fcntl
            UI_SET_EVBIT   = 0x40045564
            UI_SET_KEYBIT  = 0x40045565
            UI_SET_ABSBIT  = 0x40045567
            UI_DEV_CREATE  = 0x5501
            UI_DEV_DESTROY = 0x5502

            EV_SYN = 0x00
            EV_KEY = 0x01
            EV_ABS = 0x03

            # Xbox 360 Controller Button Codes (Linux input-event-codes.h)
            BTN_A = 0x130
            BTN_B = 0x131
            BTN_X = 0x132
            BTN_Y = 0x133
            BTN_TL = 0x136  # LB
            BTN_TR = 0x137  # RB
            BTN_SELECT = 0x13a  # Back
            BTN_START = 0x13b   # Start
            BTN_MODE = 0x13c    # Guide
            BTN_THUMBL = 0x13d
            BTN_THUMBR = 0x13e

            ABS_X = 0x00   # Steer
            ABS_Y = 0x01
            ABS_Z = 0x02   # Brake trigger
            ABS_RZ = 0x05  # Throttle trigger
            ABS_HAT0X = 0x10 # D-Pad X
            ABS_HAT0Y = 0x11 # D-Pad Y

            if not os.path.exists('/dev/uinput'):
                os.system('modprobe uinput 2>/dev/null')

            self.fd = os.open('/dev/uinput', os.O_WRONLY | os.O_NONBLOCK)

            # Enable events
            fcntl.ioctl(self.fd, UI_SET_EVBIT, EV_SYN)
            fcntl.ioctl(self.fd, UI_SET_EVBIT, EV_KEY)
            fcntl.ioctl(self.fd, UI_SET_EVBIT, EV_ABS)

            buttons = [BTN_A, BTN_B, BTN_X, BTN_Y, BTN_TL, BTN_TR, BTN_SELECT, BTN_START, BTN_MODE, BTN_THUMBL, BTN_THUMBR]
            for btn in buttons:
                fcntl.ioctl(self.fd, UI_SET_KEYBIT, btn)

            axes = [ABS_X, ABS_Y, ABS_Z, ABS_RZ, ABS_HAT0X, ABS_HAT0Y]
            for ax in axes:
                fcntl.ioctl(self.fd, UI_SET_ABSBIT, ax)

            # uinput_user_dev struct format (80 chars name, input_id, int ff_effects_max, absmax[64], absmin[64], absfuzz[64], absflat[64])
            # Simplified struct packing for standard Xbox 360 controller:
            name = b"Microsoft X-Box 360 pad\x00" + b"\x00" * (80 - len(b"Microsoft X-Box 360 pad\x00"))
            bustype = 0x03  # BUS_USB
            vendor = 0x045e # Microsoft
            product = 0x028e # Xbox 360
            version = 0x0110

            absmax = [0] * 64
            absmin = [0] * 64
            absfuzz = [0] * 64
            absflat = [0] * 64

            absmin[ABS_X] = -32768
            absmax[ABS_X] = 32767
            absmin[ABS_Y] = -32768
            absmax[ABS_Y] = 32767
            absmin[ABS_Z] = 0
            absmax[ABS_Z] = 255
            absmin[ABS_RZ] = 0
            absmax[ABS_RZ] = 255
            absmin[ABS_HAT0X] = -1
            absmax[ABS_HAT0X] = 1
            absmin[ABS_HAT0Y] = -1
            absmax[ABS_HAT0Y] = 1

            uinput_user_dev = struct.pack(
                '80sHHHHi64i64i64i64i',
                name, bustype, vendor, product, version, 0,
                *absmax, *absmin, *absfuzz, *absflat
            )
            os.write(self.fd, uinput_user_dev)
            fcntl.ioctl(self.fd, UI_DEV_CREATE)
            self.available = True
            print("[\033[92mOK\033[0m] Linux /dev/uinput Virtual Xbox 360 Controller Registered Successfully!")
        except Exception as e:
            self.available = False
            print(f"[\033[93mNOTE\033[0m] /dev/uinput controller creation: {e} (Run 'sudo chmod 666 /dev/uinput' to enable native gamepad)")

    def emit(self, steer, throttle, brake, clutch, buttons_mask):
        if not self.available or self.fd < 0:
            return
        try:
            EV_SYN = 0x00
            EV_KEY = 0x01
            EV_ABS = 0x03
            SYN_REPORT = 0x00

            ABS_X = 0x00
            ABS_Z = 0x02
            ABS_RZ = 0x05
            ABS_HAT0X = 0x10
            ABS_HAT0Y = 0x11

            BTN_A = 0x130
            BTN_B = 0x131
            BTN_X = 0x132
            BTN_Y = 0x133
            BTN_TL = 0x136
            BTN_TR = 0x137
            BTN_SELECT = 0x13a
            BTN_START = 0x13b
            BTN_MODE = 0x13c
            BTN_THUMBL = 0x13d
            BTN_THUMBR = 0x13e

            now_sec = int(time.time())
            now_usec = int((time.time() - now_sec) * 1e6)

            def write_event(etype, code, val):
                # struct input_event: timeval (2x long), unsigned short type, unsigned short code, signed int value
                pkt = struct.pack('llHHi', now_sec, now_usec, etype, code, int(val))
                os.write(self.fd, pkt)

            # Steering (-32767 to 32767)
            steer_val = int(max(-1.0, min(1.0, steer)) * 32767)
            write_event(EV_ABS, ABS_X, steer_val)

            # Throttle (0 to 255)
            throt_val = int(max(0.0, min(1.0, throttle)) * 255)
            write_event(EV_ABS, ABS_RZ, throt_val)

            # Brake (0 to 255)
            brake_val = int(max(0.0, min(1.0, brake)) * 255)
            write_event(EV_ABS, ABS_Z, brake_val)

            # Paddles & Buttons
            write_event(EV_KEY, BTN_TL, 1 if (buttons_mask & (1 << 8)) else 0) # LB Downshift
            write_event(EV_KEY, BTN_TR, 1 if (buttons_mask & (1 << 9)) else 0) # RB Upshift
            write_event(EV_KEY, BTN_A, 1 if (buttons_mask & (1 << 12)) else 0)
            write_event(EV_KEY, BTN_B, 1 if (buttons_mask & (1 << 13)) else 0)
            write_event(EV_KEY, BTN_X, 1 if (buttons_mask & (1 << 14)) else 0)
            write_event(EV_KEY, BTN_Y, 1 if (buttons_mask & (1 << 15)) else 0)

            write_event(EV_KEY, BTN_START, 1 if (buttons_mask & (1 << 4)) else 0)
            write_event(EV_KEY, BTN_SELECT, 1 if (buttons_mask & (1 << 5)) else 0)
            write_event(EV_KEY, BTN_THUMBL, 1 if (buttons_mask & (1 << 6)) else 0)
            write_event(EV_KEY, BTN_THUMBR, 1 if (buttons_mask & (1 << 7)) else 0)

            # D-Pad
            hat_x = 0
            if buttons_mask & (1 << 2): hat_x = -1
            elif buttons_mask & (1 << 3): hat_x = 1
            write_event(EV_ABS, ABS_HAT0X, hat_x)

            hat_y = 0
            if buttons_mask & (1 << 0): hat_y = -1
            elif buttons_mask & (1 << 1): hat_y = 1
            write_event(EV_ABS, ABS_HAT0Y, hat_y)

            # Syn
            write_event(EV_SYN, SYN_REPORT, 0)
        except Exception:
            pass

    def close(self):
        if self.fd >= 0:
            try:
                import fcntl
                UI_DEV_DESTROY = 0x5502
                fcntl.ioctl(self.fd, UI_DEV_DESTROY)
                os.close(self.fd)
            except Exception:
                pass
            self.fd = -1


# =============================================================================
# 2. Shared Telemetry & State Hub
# =============================================================================
class TelemetryState:
    def __init__(self):
        self.active = False
        self.speed = 0
        self.gear = 0
        self.rpm = 0
        self.max_rpm = 13500
        self.rev_percent = 0
        self.drs_active = False
        self.drs_allowed = False
        self.ers_percent = 100.0
        self.brake_bias = 56
        self.lap_time = 0.0
        self.tyre_temps = [85, 85, 85, 85]
        self.last_update = 0

class PhoneInputState:
    def __init__(self):
        self.connected = False
        self.device_name = "Ready"
        self.remote_ip = None
        self.steer = 0.0
        self.throttle = 0.0
        self.brake = 0.0
        self.clutch = 0.0
        self.buttons = 0
        self.packets_received = 0
        self.last_packet_time = 0

state_telemetry = TelemetryState()
state_input = PhoneInputState()
gamepad = LinuxUInputGamepad()


# =============================================================================
# 3. EA Sports F1 Telemetry Receiver (UDP 20777)
# =============================================================================
def f1_telemetry_loop():
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        sock.bind(('0.0.0.0', 20777))
    except Exception as e:
        print(f"[\033[93mWARN\033[0m] Port 20777 busy: {e}")
        return

    while True:
        try:
            data, _ = sock.recvfrom(2048)
            if len(data) < 24:
                continue

            packet_format = struct.unpack_from('<H', data, 0)[0]
            header_size = 29 if packet_format >= 2023 else 24
            if len(data) < header_size:
                continue

            packet_id = data[6]
            player_index = data[header_size - 2]
            state_telemetry.active = True
            state_telemetry.last_update = time.time()

            if packet_id == 6: # Car Telemetry
                car_offset = header_size + (player_index * 60)
                if len(data) >= car_offset + 50:
                    state_telemetry.speed = struct.unpack_from('<H', data, car_offset)[0]
                    state_telemetry.gear = struct.unpack_from('<b', data, car_offset + 9)[0]
                    state_telemetry.rpm = struct.unpack_from('<H', data, car_offset + 10)[0]
                    state_telemetry.drs_active = (data[car_offset + 12] == 1)
                    state_telemetry.rev_percent = data[car_offset + 13]
                    state_telemetry.tyre_temps = list(data[car_offset + 24 : car_offset + 28])

            elif packet_id == 7: # Car Status
                car_offset = header_size + (player_index * 55)
                if len(data) >= car_offset + 45:
                    state_telemetry.brake_bias = data[car_offset + 3]
                    max_rpm = struct.unpack_from('<H', data, car_offset + 16)[0]
                    if max_rpm > 0:
                        state_telemetry.max_rpm = max_rpm
                    state_telemetry.drs_allowed = (data[car_offset + 20] == 1)
                    ers_joules = struct.unpack_from('<f', data, car_offset + 36)[0]
                    state_telemetry.ers_percent = max(0.0, min(100.0, (ers_joules / 4000000.0) * 100.0))

            elif packet_id == 2: # Lap Data
                car_offset = header_size + (player_index * 57)
                if len(data) >= car_offset + 30:
                    lap_ms = struct.unpack_from('<I', data, car_offset + 4)[0]
                    state_telemetry.lap_time = lap_ms / 1000.0
        except Exception:
            pass


# =============================================================================
# 4. Inbound Phone Steering/Pedal Inputs (UDP 5005 & Heartbeat)
# =============================================================================
def phone_input_loop():
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind(('0.0.0.0', 5005))

    feedback_packet = bytearray(16)
    feedback_packet[0] = 0x54 # 'T' Telemetry / Heartbeat ACK
    feedback_packet[1] = 0x02 # Gyropad protocol ACK

    last_ack_time = 0.0

    while True:
        try:
            data, addr = sock.recvfrom(2048)
            now = time.time()
            state_input.remote_ip = addr[0]
            state_input.connected = True
            state_input.last_packet_time = now
            state_input.packets_received += 1

            # Send 20Hz heartbeat feedback back to phone to keep connection alive
            if now - last_ack_time >= 0.05:
                last_ack_time = now
                try:
                    sock.sendto(feedback_packet, addr)
                except Exception:
                    pass

            # Parse Motion Packet
            if len(data) >= 16 and data[0] == 80: # 'P' Native Racepad
                s_steer = struct.unpack_from('<h', data, 4)[0]
                u_gas = struct.unpack_from('<H', data, 6)[0]
                u_brake = struct.unpack_from('<H', data, 8)[0]
                u_clutch = struct.unpack_from('<H', data, 10)[0]
                btn_mask = struct.unpack_from('<H', data, 14)[0]

                state_input.steer = max(-1.0, min(1.0, s_steer / 32767.0))
                state_input.throttle = max(0.0, min(1.0, u_gas / 65535.0))
                state_input.brake = max(0.0, min(1.0, u_brake / 65535.0))
                state_input.clutch = max(0.0, min(1.0, u_clutch / 65535.0))
                state_input.buttons = btn_mask

                gamepad.emit(state_input.steer, state_input.throttle, state_input.brake, state_input.clutch, state_input.buttons)

            elif len(data) >= 18 and data[0] == 71: # 'G' Native Gamepad
                lx = struct.unpack_from('<h', data, 4)[0]
                lt = struct.unpack_from('<H', data, 12)[0]
                rt = struct.unpack_from('<H', data, 14)[0]
                btn = struct.unpack_from('<H', data, 16)[0]

                state_input.steer = max(-1.0, min(1.0, lx / 32767.0))
                state_input.brake = max(0.0, min(1.0, lt / 65535.0))
                state_input.throttle = max(0.0, min(1.0, rt / 65535.0))
                state_input.buttons = btn

                gamepad.emit(state_input.steer, state_input.throttle, state_input.brake, state_input.clutch, state_input.buttons)

            elif data.startswith(b'APEX:'): # Web Client / ASCII Stream
                parts = data.decode('ascii', errors='ignore').strip().split(':')
                if len(parts) >= 7:
                    state_input.steer = float(parts[2]) / 360.0
                    state_input.throttle = float(parts[3])
                    state_input.brake = float(parts[4])
                    state_input.clutch = float(parts[5])
                    state_input.buttons = int(parts[6])
                    gamepad.emit(state_input.steer, state_input.throttle, state_input.brake, state_input.clutch, state_input.buttons)
        except Exception:
            pass


# =============================================================================
# 5. Reverse Pairing Client (PW2 PAIRME fallback on Port 7507)
# =============================================================================
def reverse_pair_worker(phone_ip, phone_port, phone_name):
    try:
        hostname = socket.gethostname().replace(" ", "_") or "ApexWheel"
        s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        s.settimeout(3.0)
        s.connect((phone_ip, phone_port))
        s.sendall(f"PAIR {hostname}\n".encode('ascii'))
        resp = s.recv(256)
        if resp:
            state_input.connected = True
            state_input.remote_ip = phone_ip
            if phone_name:
                state_input.device_name = phone_name
            state_input.last_packet_time = time.time()
        s.close()
    except Exception:
        pass


# =============================================================================
# 6. UDP Discovery & Instant Pairing Beacon (UDP 5006)
# =============================================================================
def discovery_loop():
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
    try:
        sock.bind(('0.0.0.0', 5006))
    except Exception as e:
        print(f"[\033[93mWARN\033[0m] Port 5006 busy: {e}")
        return

    hostname = socket.gethostname().replace(" ", "_") or "ApexWheel"
    announce_pkt = f"PW2 ANNOUNCE PC {hostname} 5007\n".encode('ascii')
    pairack_pkt = f"PW2 PAIRACK {hostname} 5007\n".encode('ascii')

    # Background broadcast thread
    def bcast():
        bcast_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        bcast_sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
        while True:
            try:
                bcast_sock.sendto(announce_pkt, ('255.255.255.255', 5006))
                if state_input.remote_ip:
                    bcast_sock.sendto(announce_pkt, (state_input.remote_ip, 5006))
            except Exception:
                pass
            time.sleep(0.8)

    t = threading.Thread(target=bcast, daemon=True)
    t.start()

    # Receiver loop
    while True:
        try:
            data, addr = sock.recvfrom(1024)
            msg = data.decode('ascii', errors='ignore').strip()

            if msg.startswith('PW2 ANNOUNCE PHONE') or msg.startswith('PW2 DISCOVER'):
                sock.sendto(announce_pkt, addr)
                sock.sendto(announce_pkt, (addr[0], 5006))

            elif msg.startswith('PW2 PAIRME'):
                sock.sendto(pairack_pkt, addr)
                sock.sendto(pairack_pkt, (addr[0], 5006))
                sock.sendto(announce_pkt, (addr[0], 5006))

                # Reverse TCP pairing fallback
                parts = msg.split()
                phone_name = parts[2] if len(parts) > 2 else "Phone"
                phone_port = 7507
                for p in parts[2:]:
                    if p.isdigit() and 1024 < int(p) < 65535:
                        phone_port = int(p)
                        break
                threading.Thread(target=reverse_pair_worker, args=(addr[0], phone_port, phone_name), daemon=True).start()
        except Exception:
            pass


# =============================================================================
# 7. Forward TCP Pairing Server (TCP 5007)
# =============================================================================
def pair_client_handler(conn, addr):
    try:
        conn.settimeout(5.0)
        data = conn.recv(1024)
        if data:
            line = data.decode('ascii', errors='ignore').strip()
            parts = line.split()
            cmd = parts[0].upper() if parts else ""
            token = parts[1] if len(parts) > 1 else "123456"

            if cmd == "HELLO":
                conn.sendall(b"OK\n")
            else:
                conn.sendall(f"OK {token}\n".encode('ascii'))

            state_input.connected = True
            state_input.remote_ip = addr[0]
            if len(parts) > 1 and cmd == "PAIR":
                state_input.device_name = parts[1]
            state_input.last_packet_time = time.time()
            time.sleep(0.15)
    except Exception:
        pass
    finally:
        try:
            conn.close()
        except Exception:
            pass

def pair_server_loop():
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind(('0.0.0.0', 5007))
    sock.listen(8)

    while True:
        try:
            conn, addr = sock.accept()
            threading.Thread(target=pair_client_handler, args=(conn, addr), daemon=True).start()
        except Exception:
            pass


# =============================================================================
# 8. Web Cockpit HTTP Server & REST API (Port 8080)
# =============================================================================
class CockpitHttpHandler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        script_dir = os.path.dirname(os.path.abspath(__file__))
        root_dir = os.path.abspath(os.path.join(script_dir, '..'))
        web_dir = os.path.join(root_dir, 'client-web')
        if not os.path.exists(web_dir):
            web_dir = script_dir
        super().__init__(*args, directory=web_dir, **kwargs)

    def do_GET(self):
        if self.path == '/api/telemetry':
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()
            payload = {
                'active': state_telemetry.active and (time.time() - state_telemetry.last_update < 3.0),
                'speed': state_telemetry.speed,
                'gear': state_telemetry.gear,
                'rpm': state_telemetry.rpm,
                'maxRpm': state_telemetry.max_rpm,
                'revPercent': state_telemetry.rev_percent,
                'drsActive': state_telemetry.drs_active,
                'drsAllowed': state_telemetry.drs_allowed,
                'ersPercent': state_telemetry.ers_percent,
                'brakeBias': state_telemetry.brake_bias,
                'lapTime': state_telemetry.lap_time,
                'tyreTemps': state_telemetry.tyre_temps
            }
            self.wfile.write(json.dumps(payload).encode('utf-8'))
            return

        elif self.path == '/api/status':
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()
            payload = {
                'connected': state_input.connected and (time.time() - state_input.last_packet_time < 5.0),
                'deviceName': state_input.device_name,
                'remoteIp': state_input.remote_ip,
                'packets': state_input.packets_received,
                'steer': state_input.steer,
                'throttle': state_input.throttle,
                'brake': state_input.brake,
                'gamepadAvailable': gamepad.available,
                'f1Active': state_telemetry.active and (time.time() - state_telemetry.last_update < 3.0)
            }
            self.wfile.write(json.dumps(payload).encode('utf-8'))
            return

        return super().do_GET()

    def do_POST(self):
        if self.path == '/api/input':
            length = int(self.headers.get('Content-Length', 0))
            body = self.rfile.read(length).decode('ascii', errors='ignore')
            parts = body.strip().split(':')
            if len(parts) >= 7 and parts[0] == 'APEX':
                state_input.steer = float(parts[2]) / 360.0
                state_input.throttle = float(parts[3])
                state_input.brake = float(parts[4])
                state_input.clutch = float(parts[5])
                state_input.buttons = int(parts[6])
                state_input.packets_received += 1
                state_input.last_packet_time = time.time()
                gamepad.emit(state_input.steer, state_input.throttle, state_input.brake, state_input.clutch, state_input.buttons)

            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()
            self.wfile.write(b'{"ok":true}')
            return

        return super().do_POST()

    def log_message(self, format, *args):
        pass # Suppress HTTP access logging

def http_server_loop():
    class ReusableServer(socketserver.TCPServer):
        allow_reuse_address = True
    try:
        with ReusableServer(('0.0.0.0', 8080), CockpitHttpHandler) as httpd:
            httpd.serve_forever()
    except Exception as e:
        print(f"[\033[93mWARN\033[0m] HTTP Server on 8080: {e}")


# =============================================================================
# 9. Interactive Terminal Cockpit HUD
# =============================================================================
def terminal_hud():
    os_name = "Linux" if IS_LINUX else ("macOS" if IS_MACOS else "Windows")
    time.sleep(1.0)
    print("\033[2J\033[H", end="") # Clear screen

    while True:
        try:
            now = time.time()
            is_conn = state_input.connected and (now - state_input.last_packet_time < 5.0)
            f1_live = state_telemetry.active and (now - state_telemetry.last_update < 3.0)

            # Steering Bar: -1.0 to +1.0
            s = state_input.steer
            deg = s * 360.0
            steer_bar = list("--------------------|--------------------")
            pos = int((s + 1.0) * 19.5)
            pos = max(0, min(39, pos))
            steer_bar[pos] = "█"
            steer_str = "".join(steer_bar)

            # Throttle Bar: 0 to 1
            t_fill = int(state_input.throttle * 20)
            t_bar = "█" * t_fill + "░" * (20 - t_fill)

            # Brake Bar: 0 to 1
            b_fill = int(state_input.brake * 20)
            b_bar = "█" * b_fill + "░" * (20 - b_fill)

            # Rev LEDs: 15 LEDs
            lit_leds = int((state_telemetry.rev_percent / 100.0) * 15)
            leds = ""
            for i in range(15):
                if i < lit_leds:
                    if i < 5: leds += "\033[92m●\033[0m " # Green
                    elif i < 10: leds += "\033[91m●\033[0m " # Red
                    else: leds += "\033[94m●\033[0m " # Blue
                else:
                    leds += "\033[90m○\033[0m "

            gear_str = "R" if state_telemetry.gear == -1 else ("N" if state_telemetry.gear == 0 else str(state_telemetry.gear))
            drs_str = "\033[92mOPEN\033[0m" if state_telemetry.drs_active else ("\033[93mREADY\033[0m" if state_telemetry.drs_allowed else "\033[90mOFF\033[0m")
            status_badge = "\033[92m● CONNECTED\033[0m" if is_conn else "\033[93m○ SEARCHING\033[0m"
            f1_badge = "\033[92m● LIVE ON TRACK\033[0m" if f1_live else "\033[90m○ WAITING (PORT 20777)\033[0m"
            pad_badge = "\033[92mXBOX 360 (/dev/uinput)\033[0m" if gamepad.available else "\033[90mWEB & TELEMETRY MODE\033[0m"

            hud = f"""
\033[H\033[1m╔══════════════════════════════════════════════════════════════════════╗
║  🏎️  APEXWHEEL F1 — UNIVERSAL COMPANION ({os_name})                      ║
╠══════════════════════════════════════════════════════════════════════╣
║  Phone Status : {status_badge:<30} Device: {state_input.device_name:<18}║
║  Controller   : {pad_badge:<45}           ║
║  F1 25 UDP    : {f1_badge:<45}           ║
║  Web Cockpit  : \033[96mhttp://localhost:8080\033[0m                                        ║
╠══════════════════════════════════════════════════════════════════════╣
║  STEERING WHEEL  : [{steer_str}]  {deg:+6.1f}° ║
║  THROTTLE (GAS)  : [{t_bar}]  {state_input.throttle * 100:5.1f}% ║
║  BRAKE PEDAL     : [{b_bar}]  {state_input.brake * 100:5.1f}% ║
║  INPUT PACKETS   : {state_input.packets_received:<15} Phone IP : {str(state_input.remote_ip):<16}║
╠══════════════════════════════════════════════════════════════════════╣
║  F1 25 REV LIGHTS: {leds}             ║
║  GEAR: \033[1m{gear_str}\033[0m   SPEED: {state_telemetry.speed:>3} KM/H   RPM: {state_telemetry.rpm:>5}/{state_telemetry.max_rpm}   DRS: {drs_str:<12} ║
╚══════════════════════════════════════════════════════════════════════╝\033[0m
"""
            print(hud, end="", flush=True)
            time.sleep(0.05)
        except Exception:
            time.sleep(0.1)


# =============================================================================
# 10. Main Entry Point
# =============================================================================
def main():
    print(f"Starting ApexWheel F1 Universal Companion on {platform.system()} ({platform.machine()})...")

    # Start Background Workers
    threads = [
        threading.Thread(target=phone_input_loop, daemon=True, name="PhoneInput"),
        threading.Thread(target=discovery_loop, daemon=True, name="Discovery"),
        threading.Thread(target=pair_server_loop, daemon=True, name="PairServer"),
        threading.Thread(target=f1_telemetry_loop, daemon=True, name="F1Telemetry"),
        threading.Thread(target=http_server_loop, daemon=True, name="HttpCockpit")
    ]
    for t in threads:
        t.start()

    # Open Cockpit in Browser
    try:
        webbrowser.open('http://localhost:8080')
    except Exception:
        pass

    try:
        terminal_hud()
    except KeyboardInterrupt:
        print("\nShutting down ApexWheel Companion...")
        gamepad.close()
        sys.exit(0)

if __name__ == '__main__':
    main()
