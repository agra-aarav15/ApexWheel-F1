using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace ApexWheel
{
    // =========================================================================
    // 1. Gyropad Signature Dark-Purple / Neon F1 Theme
    // =========================================================================
    public static class Theme
    {
        public static readonly Color Bg          = Color.FromArgb(11, 9, 16);          // Pitch dark
        public static readonly Color Surface     = Color.FromArgb(26, 22, 40);         // #1A1628 Dark purple
        public static readonly Color SurfaceHi   = Color.FromArgb(36, 30, 56);         // #241E38 Highlight
        public static readonly Color Stroke      = Color.FromArgb(50, 40, 75);         // Border
        public static readonly Color Ink         = Color.FromArgb(245, 240, 255);      // Pure white/ink
        public static readonly Color Dim         = Color.FromArgb(170, 160, 190);      // Dim gray
        public static readonly Color Faint       = Color.FromArgb(110, 100, 130);      // Faint
        public static readonly Color Accent      = Color.FromArgb(168, 85, 247);       // #A855F7 Vibrant Purple
        public static readonly Color AccentDeep  = Color.FromArgb(124, 58, 237);       // #7C3AED
        public static readonly Color Throttle    = Color.FromArgb(168, 85, 247);       // Purple Gas
        public static readonly Color Brake       = Color.FromArgb(255, 61, 110);       // #FF3D6E Neon Pink/Red Brake
        public static readonly Color Good        = Color.FromArgb(0, 230, 118);        // Green
        public static readonly Color Warn        = Color.FromArgb(255, 214, 0);        // Yellow
        public static readonly Color F1Red       = Color.FromArgb(225, 6, 0);          // F1 Red

        public static readonly Font H1      = new Font("Segoe UI", 16f, FontStyle.Bold);
        public static readonly Font Title   = new Font("Segoe UI", 12f, FontStyle.Bold);
        public static readonly Font Body    = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        public static readonly Font BodyB   = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        public static readonly Font Small   = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        public static readonly Font Mono    = new Font("Consolas", 9.5f, FontStyle.Regular);
        public static readonly Font MonoB   = new Font("Consolas", 14f, FontStyle.Bold);
    }

    // =========================================================================
    // 2. ViGEmBus Direct Driver Interface (Xbox 360 Virtual Controller)
    // =========================================================================
    public class ViGEmBridge
    {
        private static readonly Guid BusGuid = new Guid("96e42b22-f5e9-42f8-b043-ed0f932f014f");
        private const uint IOCTL_PLUGIN_TARGET = 0x2aa004;
        private const uint IOCTL_UNPLUG_TARGET = 0x2aa008;
        private const uint IOCTL_WAIT_READY    = 0x2aa010;
        private const uint IOCTL_XUSB_SUBMIT   = 0x2aa808;

        private const uint DIGCF_PRESENT = 0x2;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_RW = 3;
        private const uint OPEN_EXISTING = 3;
        private static readonly IntPtr INVALID = new IntPtr(-1);

        public const ushort DPAD_UP        = 0x0001;
        public const ushort DPAD_DOWN      = 0x0002;
        public const ushort DPAD_LEFT      = 0x0004;
        public const ushort DPAD_RIGHT     = 0x0008;
        public const ushort START          = 0x0010;
        public const ushort BACK           = 0x0020;
        public const ushort LEFT_THUMB     = 0x0040;
        public const ushort RIGHT_THUMB    = 0x0080;
        public const ushort LEFT_SHOULDER  = 0x0100;
        public const ushort RIGHT_SHOULDER = 0x0200;
        public const ushort GUIDE          = 0x0400;
        public const ushort A              = 0x1000;
        public const ushort B              = 0x2000;
        public const ushort X              = 0x4000;
        public const ushort Y              = 0x8000;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, int DeviceInterfaceDetailDataSize, out int RequiredSize, IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, byte[] lpInBuffer, int nInBufferSize, byte[] lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private IntPtr _bus = INVALID;
        private int _serial = 0;
        private readonly byte[] _report = new byte[20];

        public bool Available { get; private set; }
        public bool Plugged { get; private set; }
        public string Status { get; private set; }

        public ViGEmBridge()
        {
            Status = "Not connected";
        }

        public static string FindBus()
        {
            Guid g = BusGuid;
            IntPtr devs = SetupDiGetClassDevsW(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (devs == INVALID) return null;

            try
            {
                SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));

                if (!SetupDiEnumDeviceInterfaces(devs, IntPtr.Zero, ref g, 0, ref ifData))
                    return null;

                int req = 0;
                SetupDiGetDeviceInterfaceDetailW(devs, ref ifData, IntPtr.Zero, 0, out req, IntPtr.Zero);
                if (req <= 0) return null;

                IntPtr buf = Marshal.AllocHGlobal(req);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(devs, ref ifData, buf, req, out req, IntPtr.Zero))
                        return null;

                    IntPtr pPath = new IntPtr(buf.ToInt64() + 4);
                    return Marshal.PtrToStringUni(pPath);
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devs);
            }
        }

        public bool Open()
        {
            if (_bus != INVALID) return true;
            string path = FindBus();
            if (string.IsNullOrEmpty(path))
            {
                Status = "ViGEmBus driver not installed.";
                Available = false;
                return false;
            }

            _bus = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (_bus == INVALID)
            {
                Status = "Failed to open ViGEmBus device handle.";
                Available = false;
                return false;
            }

            Available = true;
            Status = "ViGEmBus ready";
            return true;
        }

        public bool Plug()
        {
            if (_bus == INVALID && !Open()) return false;
            if (Plugged) return true;

            byte[] plugReq = new byte[16];
            Put32(plugReq, 0, 16);               // Size
            Put32(plugReq, 4, 0);                // TARGET_XBOX360_WIRED = 0
            Put32(plugReq, 8, 1);                // Version 1

            int returned;
            if (!DeviceIoControl(_bus, IOCTL_PLUGIN_TARGET, plugReq, plugReq.Length, plugReq, plugReq.Length, out returned, IntPtr.Zero))
            {
                Status = "Failed to plug virtual Xbox 360 controller.";
                return false;
            }

            _serial = BitConverter.ToInt32(plugReq, 4);
            WaitReady();
            Plugged = true;
            Status = string.Format("Xbox 360 Controller Active (Serial #{0})", _serial);
            return true;
        }

        private void WaitReady()
        {
            if (_bus == INVALID || _serial == 0) return;
            byte[] waitReq = new byte[8];
            Put32(waitReq, 0, 8);
            Put32(waitReq, 4, _serial);
            int ret;
            DeviceIoControl(_bus, IOCTL_WAIT_READY, waitReq, waitReq.Length, null, 0, out ret, IntPtr.Zero);
        }

        public void Unplug()
        {
            if (_bus == INVALID || !Plugged) return;
            byte[] req = new byte[8];
            Put32(req, 0, 8);
            Put32(req, 4, _serial);
            int ret;
            DeviceIoControl(_bus, IOCTL_UNPLUG_TARGET, req, req.Length, null, 0, out ret, IntPtr.Zero);
            Plugged = false;
            _serial = 0;
            Status = "Virtual controller unplugged";
        }

        public void Close()
        {
            Unplug();
            if (_bus != INVALID)
            {
                CloseHandle(_bus);
                _bus = INVALID;
            }
            Available = false;
        }

        public bool Feed(double lx, double ly, double rx, double ry, double lt, double rt, ushort buttons)
        {
            if (!Plugged || _bus == INVALID) return false;

            Put32(_report, 0, 20);
            Put32(_report, 4, _serial);
            Put16(_report, 8, (short)buttons);
            _report[10] = Trig(lt);
            _report[11] = Trig(rt);
            Put16(_report, 12, Axis(lx));
            Put16(_report, 14, Axis(ly));
            Put16(_report, 16, Axis(rx));
            Put16(_report, 18, Axis(ry));

            int ret;
            return DeviceIoControl(_bus, IOCTL_XUSB_SUBMIT, _report, _report.Length, null, 0, out ret, IntPtr.Zero);
        }

        private static void Put32(byte[] b, int off, int val)
        {
            b[off]     = (byte)(val & 0xFF);
            b[off + 1] = (byte)((val >> 8) & 0xFF);
            b[off + 2] = (byte)((val >> 16) & 0xFF);
            b[off + 3] = (byte)((val >> 24) & 0xFF);
        }

        private static void Put16(byte[] b, int off, short val)
        {
            b[off]     = (byte)(val & 0xFF);
            b[off + 1] = (byte)((val >> 8) & 0xFF);
        }

        public static short Axis(double v)
        {
            if (v <= -1.0) return -32768;
            if (v >= 1.0)  return 32767;
            return (short)Math.Round(v * 32767.0);
        }

        public static byte Trig(double v)
        {
            if (v <= 0.0) return 0;
            if (v >= 1.0) return 255;
            return (byte)Math.Round(v * 255.0);
        }
    }

    // =========================================================================
    // 3. F1 25 UDP Telemetry Engine (Port 20777)
    // =========================================================================
    public class F1TelemetryData
    {
        public bool Active = false;
        public int SpeedKmh = 0;
        public int Gear = 0;
        public int EngineRPM = 0;
        public int MaxRPM = 13500;
        public int RevLightsPercent = 0;
        public int RevLightsBitValue = 0;
        public bool DrsAllowed = false;
        public bool DrsActive = false;
        public float ErsPercent = 100f;
        public int ErsMode = 0;
        public int EngineTemp = 90;
        public int[] TyreTemps = new int[] { 90, 90, 90, 90 };
        public int BrakeBias = 56;
        public bool PitLimiter = false;
        public int FiaFlag = 0;
        public float CurrentLapTime = 0f;
        public int Position = 1;

        public string ToJson()
        {
            return string.Format(
                "{{\"active\":{0},\"speed\":{1},\"gear\":{2},\"rpm\":{3},\"maxRpm\":{4},\"revPercent\":{5},\"revBits\":{6},\"drsAllowed\":{7},\"drsActive\":{8},\"ers\":{9:0.0},\"ersMode\":{10},\"engineTemp\":{11},\"tyres\":[{12},{13},{14},{15}],\"brakeBias\":{16},\"pitLimiter\":{17},\"fiaFlag\":{18},\"lapTime\":{19:0.00},\"position\":{20}}}",
                Active ? "true" : "false", SpeedKmh, Gear, EngineRPM, MaxRPM,
                RevLightsPercent, RevLightsBitValue, DrsAllowed ? "true" : "false",
                DrsActive ? "true" : "false", ErsPercent, ErsMode, EngineTemp,
                TyreTemps[0], TyreTemps[1], TyreTemps[2], TyreTemps[3],
                BrakeBias, PitLimiter ? "true" : "false", FiaFlag, CurrentLapTime, Position
            );
        }
    }

    public class F1TelemetryListener
    {
        private UdpClient _udp;
        private Thread _thread;
        private bool _running;
        public readonly F1TelemetryData Data = new F1TelemetryData();

        public void Start(int port)
        {
            try
            {
                _udp = new UdpClient(port);
                _running = true;
                _thread = new Thread(ListenLoop);
                _thread.IsBackground = true;
                _thread.Start();
            }
            catch { }
        }

        public void Stop()
        {
            _running = false;
            if (_udp != null) { _udp.Close(); _udp = null; }
        }

        private void ListenLoop()
        {
            IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] packet = _udp.Receive(ref sender);
                    if (packet == null || packet.Length < 24) continue;

                    ushort packetFormat = BitConverter.ToUInt16(packet, 0);
                    int headerSize = (packetFormat >= 2023) ? 29 : 24;
                    if (packet.Length < headerSize) continue;

                    byte packetId = packet[6];
                    byte playerIndex = packet[headerSize - 2];

                    Data.Active = true;

                    if (packetId == 6) // Car Telemetry
                    {
                        int carOffset = headerSize + (playerIndex * 60);
                        if (packet.Length >= carOffset + 50)
                        {
                            Data.SpeedKmh = BitConverter.ToUInt16(packet, carOffset);
                            Data.Gear = (sbyte)packet[carOffset + 9];
                            Data.EngineRPM = BitConverter.ToUInt16(packet, carOffset + 10);
                            Data.DrsActive = packet[carOffset + 12] == 1;
                            Data.RevLightsPercent = packet[carOffset + 13];
                            Data.RevLightsBitValue = BitConverter.ToUInt16(packet, carOffset + 14);

                            Data.TyreTemps[0] = packet[carOffset + 24]; // RL
                            Data.TyreTemps[1] = packet[carOffset + 25]; // RR
                            Data.TyreTemps[2] = packet[carOffset + 26]; // FL
                            Data.TyreTemps[3] = packet[carOffset + 27]; // FR
                            Data.EngineTemp = BitConverter.ToUInt16(packet, carOffset + 32);
                        }
                    }
                    else if (packetId == 7) // Car Status
                    {
                        int carOffset = headerSize + (playerIndex * 55);
                        if (packet.Length >= carOffset + 45)
                        {
                            Data.BrakeBias = packet[carOffset + 3];
                            Data.PitLimiter = packet[carOffset + 4] == 1;
                            Data.MaxRPM = BitConverter.ToUInt16(packet, carOffset + 16);
                            if (Data.MaxRPM == 0) Data.MaxRPM = 13500;
                            Data.DrsAllowed = packet[carOffset + 20] == 1;
                            Data.FiaFlag = (sbyte)packet[carOffset + 24];

                            float ersJoules = BitConverter.ToSingle(packet, carOffset + 36);
                            Data.ErsPercent = Math.Max(0f, Math.Min(100f, (ersJoules / 4000000f) * 100f));
                            Data.ErsMode = packet[carOffset + 40];
                        }
                    }
                    else if (packetId == 2) // Lap Data
                    {
                        int carOffset = headerSize + (playerIndex * 57);
                        if (packet.Length >= carOffset + 30)
                        {
                            uint lapTimeMs = BitConverter.ToUInt32(packet, carOffset + 4);
                            Data.CurrentLapTime = lapTimeMs / 1000f;
                            Data.Position = packet[carOffset + 21];
                        }
                    }
                }
                catch { }
            }
        }
    }

    // =========================================================================
    // 4. High-Speed Phone Input Receiver (UDP Port 8081)
    // =========================================================================
    public class PhoneInputReceiver
    {
        private UdpClient _udp;
        private Thread _thread;
        private bool _running;
        private readonly ViGEmBridge _bridge;

        public double Steer { get; private set; }
        public double Throttle { get; private set; }
        public double Brake { get; private set; }
        public double Clutch { get; private set; }
        public ushort Buttons { get; private set; }
        public long PacketsReceived { get; private set; }
        public DateTime LastInputTime { get; private set; }

        public double SteeringLockAngle { get; set; }
        public double Deadzone { get; set; }
        public double Linearity { get; set; }

        public PhoneInputReceiver(ViGEmBridge bridge)
        {
            _bridge = bridge;
            LastInputTime = DateTime.MinValue;
            SteeringLockAngle = 360.0;
            Deadzone = 0.02;
            Linearity = 1.0;
        }

        public void Start(int port)
        {
            try
            {
                _udp = new UdpClient(port);
                _running = true;
                _thread = new Thread(ListenLoop);
                _thread.IsBackground = true;
                _thread.Start();
            }
            catch { }
        }

        public void Stop()
        {
            _running = false;
            if (_udp != null) { _udp.Close(); _udp = null; }
        }

        private void ListenLoop()
        {
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] bytes = _udp.Receive(ref remote);
                    if (bytes == null || bytes.Length == 0) continue;
                    string msg = Encoding.ASCII.GetString(bytes);
                    ProcessMessage(msg);
                }
                catch { }
            }
        }

        public void ProcessMessage(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;

            string[] parts = msg.Split(':');
            if (parts.Length < 4) return;

            double rawSteer = 0.0;
            double rawGas = 0.0;
            double rawBrake = 0.0;
            double rawClutch = 0.0;
            ushort btnMask = 0;

            if (parts[0] == "APEX")
            {
                double angleDeg = 0.0;
                double.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out angleDeg);
                double.TryParse(parts[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out rawGas);
                double.TryParse(parts[4], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out rawBrake);
                if (parts.Length > 5) double.TryParse(parts[5], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out rawClutch);
                if (parts.Length > 6) ushort.TryParse(parts[6], out btnMask);

                double halfLock = SteeringLockAngle / 2.0;
                rawSteer = Math.Max(-1.0, Math.Min(1.0, angleDeg / halfLock));
            }
            else if (parts[0] == "IN")
            {
                double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out rawSteer);
                double.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out rawGas);
                double.TryParse(parts[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out rawBrake);
                if (parts.Length > 4) ushort.TryParse(parts[4], out btnMask);
            }

            // Apply deadzone & curve
            if (Math.Abs(rawSteer) < Deadzone) rawSteer = 0.0;
            else
            {
                double sign = Math.Sign(rawSteer);
                double abs = (Math.Abs(rawSteer) - Deadzone) / (1.0 - Deadzone);
                if (abs > 1.0) abs = 1.0;
                rawSteer = sign * Math.Pow(abs, Linearity);
            }

            Steer = rawSteer;
            Throttle = Math.Max(0.0, Math.Min(1.0, rawGas));
            Brake = Math.Max(0.0, Math.Min(1.0, rawBrake));
            Clutch = rawClutch;
            Buttons = btnMask;
            PacketsReceived++;
            LastInputTime = DateTime.UtcNow;

            _bridge.Feed(Steer, 0.0, 0.0, 0.0, Brake, Throttle, Buttons);
        }
    }

    // =========================================================================
    // 5. Gyropad-Style Interactive Live PadDiagram
    // =========================================================================
    public class PadDiagram : Control
    {
        private readonly PhoneInputReceiver _input;
        private readonly F1TelemetryListener _telemetry;

        public PadDiagram(PhoneInputReceiver input, F1TelemetryListener telemetry)
        {
            _input = input;
            _telemetry = telemetry;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);

            int w = Width;
            int h = Height;

            // Draw Phone Outline (Bezel)
            int phoneW = w - 20;
            int phoneH = h - 20;
            Rectangle phoneRect = new Rectangle(10, 10, phoneW, phoneH);

            using (GraphicsPath path = GetRoundedRect(phoneRect, 24))
            {
                using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (Pen p = new Pen(Theme.Stroke, 2f)) g.DrawPath(p, path);
            }

            // Draw Left Pedal (Brake - Neon Red/Pink)
            int pedalW = (int)(phoneW * 0.14);
            int pedalH = (int)(phoneH * 0.65);
            int pedalY = (int)(phoneH * 0.22);

            Rectangle brakeRect = new Rectangle(phoneRect.X + 16, phoneRect.Y + pedalY, pedalW, pedalH);
            DrawPedal(g, brakeRect, "BRAKE", (float)_input.Brake, Theme.Brake);

            // Draw Right Pedal (Throttle - Neon Purple)
            Rectangle throttleRect = new Rectangle(phoneRect.Right - pedalW - 16, phoneRect.Y + pedalY, pedalW, pedalH);
            DrawPedal(g, throttleRect, "GAS", (float)_input.Throttle, Theme.Throttle);

            // Draw Steering Arc in Center
            int arcCenterW = phoneW / 2;
            int arcCenterH = phoneH / 2;
            int arcRadius = (int)(phoneH * 0.38);
            Rectangle arcBounds = new Rectangle(phoneRect.X + arcCenterW - arcRadius, phoneRect.Y + arcCenterH - arcRadius - 10, arcRadius * 2, arcRadius * 2);

            using (Pen p = new Pen(Theme.Stroke, 4f))
            {
                g.DrawArc(p, arcBounds, 140, 260);
            }

            // Steering needle based on angle
            double steerNorm = _input.Steer; // -1.0 to 1.0
            float steerAngle = 270f + (float)(steerNorm * 90f);
            float rad = steerAngle * (float)Math.PI / 180f;
            float nX = (phoneRect.X + arcCenterW) + (float)Math.Cos(rad) * (arcRadius - 15);
            float nY = (phoneRect.Y + arcCenterH - 10) + (float)Math.Sin(rad) * (arcRadius - 15);

            using (Pen p = new Pen(Theme.Accent, 3f))
            {
                g.DrawLine(p, phoneRect.X + arcCenterW, phoneRect.Y + arcCenterH - 10, nX, nY);
            }

            // Draw Center Gear & Telemetry
            string gearStr = _telemetry.Data.Gear == -1 ? "R" : (_telemetry.Data.Gear == 0 ? "N" : _telemetry.Data.Gear.ToString());
            string speedStr = string.Format("{0} KM/H", _telemetry.Data.SpeedKmh);

            using (SolidBrush b = new SolidBrush(Theme.Warn))
            {
                g.DrawString(gearStr, Theme.MonoB, b, phoneRect.X + arcCenterW - 10, phoneRect.Y + arcCenterH - 24);
            }
            using (SolidBrush b = new SolidBrush(Theme.Ink))
            {
                g.DrawString(speedStr, Theme.Small, b, phoneRect.X + arcCenterW - 24, phoneRect.Y + arcCenterH + 12);
            }

            // Top F1 Buttons (Pills)
            int pillW = (int)(phoneW * 0.13);
            int pillH = 26;
            int topY = phoneRect.Y + 16;

            DrawPillButton(g, new Rectangle(phoneRect.X + 16, topY, pillW, pillH), "ERS / OT", (_input.Buttons & ViGEmBridge.LEFT_THUMB) != 0);
            DrawPillButton(g, new Rectangle(phoneRect.X + 24 + pillW, topY, pillW, pillH), "PIT", (_input.Buttons & ViGEmBridge.DPAD_DOWN) != 0);
            DrawPillButton(g, new Rectangle(phoneRect.Right - (pillW * 2) - 24, topY, pillW, pillH), "◀ DOWN", (_input.Buttons & ViGEmBridge.LEFT_SHOULDER) != 0);
            DrawPillButton(g, new Rectangle(phoneRect.Right - pillW - 16, topY, pillW, pillH), "UP ▶", (_input.Buttons & ViGEmBridge.RIGHT_SHOULDER) != 0);

            // Center Status Pill
            int statusW = (int)(phoneW * 0.28);
            Rectangle statusPill = new Rectangle(phoneRect.X + arcCenterW - (statusW / 2), topY, statusW, pillH);
            using (GraphicsPath path = GetRoundedRect(statusPill, 8))
            {
                using (SolidBrush b = new SolidBrush(Theme.SurfaceHi)) g.FillPath(b, path);
                using (Pen p = new Pen(Theme.Stroke, 1f)) g.DrawPath(p, path);
            }
            using (SolidBrush b = new SolidBrush(_telemetry.Data.Active ? Theme.Good : Theme.Dim))
            {
                string statusText = _telemetry.Data.Active ? "● F1 25 LIVE TELEMETRY" : "○ F1 WAITING (PORT 20777)";
                g.DrawString(statusText, Theme.Small, b, statusPill.X + 14, statusPill.Y + 6);
            }
        }

        private void DrawPedal(Graphics g, Rectangle r, string label, float fillVal, Color fillCol)
        {
            using (GraphicsPath path = GetRoundedRect(r, 12))
            {
                using (SolidBrush b = new SolidBrush(Theme.SurfaceHi)) g.FillPath(b, path);
                using (Pen p = new Pen(Theme.Stroke, 1.5f)) g.DrawPath(p, path);
            }

            if (fillVal > 0.01f)
            {
                int fillHeight = (int)(r.Height * Math.Min(1.0f, fillVal));
                Rectangle fillR = new Rectangle(r.X + 2, r.Bottom - fillHeight - 2, r.Width - 4, fillHeight);
                using (GraphicsPath fpath = GetRoundedRect(fillR, 10))
                {
                    using (SolidBrush b = new SolidBrush(fillCol)) g.FillPath(b, fpath);
                }
            }

            using (SolidBrush b = new SolidBrush(Theme.Ink))
            {
                g.DrawString(label, Theme.BodyB, b, r.X + 10, r.Bottom - 26);
            }
        }

        private void DrawPillButton(Graphics g, Rectangle r, string text, bool pressed)
        {
            Color bg = pressed ? Theme.Accent : Theme.SurfaceHi;
            Color border = pressed ? Theme.Ink : Theme.Stroke;

            using (GraphicsPath path = GetRoundedRect(r, 8))
            {
                using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, path);
                using (Pen p = new Pen(border, 1.5f)) g.DrawPath(p, path);
            }
            using (SolidBrush b = new SolidBrush(Theme.Ink))
            {
                g.DrawString(text, Theme.Small, b, r.X + 8, r.Y + 6);
            }
        }

        private static GraphicsPath GetRoundedRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // =========================================================================
    // 6. Gyropad-Style Rounded Modern Card
    // =========================================================================
    public class Card : Panel
    {
        public string TitleText { get; set; }

        public Card(string title)
        {
            TitleText = title;
            DoubleBuffered = true;
            Padding = new Padding(16, 36, 16, 16);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            using (GraphicsPath path = GetRoundedRect(r, 12))
            {
                using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (Pen p = new Pen(Theme.Stroke, 1.5f)) g.DrawPath(p, path);
            }

            if (!string.IsNullOrEmpty(TitleText))
            {
                using (SolidBrush b = new SolidBrush(Theme.Dim))
                {
                    g.DrawString(TitleText.ToUpperInvariant(), Theme.Small, b, 16, 12);
                }
            }
        }

        private static GraphicsPath GetRoundedRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // =========================================================================
    // 7. Gyropad-Style Toggle Method Button (Wi-Fi / USB)
    // =========================================================================
    public class MethodBtn : Control
    {
        public bool Active { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }

        public MethodBtn(string title, string subtitle, bool active)
        {
            Title = title;
            Subtitle = subtitle;
            Active = active;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            Color bg = Active ? Theme.SurfaceHi : Theme.Surface;
            Color border = Active ? Theme.Accent : Theme.Stroke;

            using (GraphicsPath path = GetRoundedRect(r, 10))
            {
                using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, path);
                using (Pen p = new Pen(border, Active ? 2f : 1f)) g.DrawPath(p, path);
            }

            using (SolidBrush b = new SolidBrush(Active ? Theme.Ink : Theme.Dim))
            {
                g.DrawString(Title, Theme.BodyB, b, 14, 12);
            }
            using (SolidBrush b = new SolidBrush(Theme.Faint))
            {
                g.DrawString(Subtitle, Theme.Small, b, 14, 30);
            }
        }

        private static GraphicsPath GetRoundedRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // =========================================================================
    // 8. Main Form - Authentic Gyropad PC Layout in F1 Edition
    // =========================================================================
    public class MainForm : Form
    {
        private readonly ViGEmBridge _vigem;
        private readonly F1TelemetryListener _telemetry;
        private readonly PhoneInputReceiver _input;

        private PadDiagram _diagram;
        private Card _connCard;
        private Card _telemCard;

        private Label _lblIp;
        private Label _lblStatus;
        private Label _lblTelemDetail;
        private MethodBtn _btnWifi;
        private MethodBtn _btnUsb;

        private System.Windows.Forms.Timer _tickTimer;

        public MainForm()
        {
            // Bridge components
            _vigem = new ViGEmBridge();
            _vigem.Open();
            _vigem.Plug();

            _telemetry = new F1TelemetryListener();
            _telemetry.Start(20777);

            _input = new PhoneInputReceiver(_vigem);
            _input.Start(8081);

            // Check USB Port Forwarding
            CheckUsbForward();

            InitializeUI();
        }

        private void InitializeUI()
        {
            Text = "ApexWheel F1 - Companion Server";
            Size = new Size(820, 680);
            MinimumSize = new Size(760, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Ink;
            Font = Theme.Body;

            // Header Banner
            Panel header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Surface, Padding = new Padding(20, 16, 20, 16) };
            Label title = new Label
            {
                Text = "🏎️ APEXWHEEL F1",
                Font = Theme.H1,
                ForeColor = Theme.Ink,
                AutoSize = true,
                Location = new Point(20, 16)
            };
            Label version = new Label
            {
                Text = "v1.37 F1 EDITION (ViGEm Xbox 360 + F1 25 UDP Telemetry)",
                Font = Theme.Small,
                ForeColor = Theme.Dim,
                AutoSize = true,
                Location = new Point(240, 22)
            };
            header.Controls.Add(title);
            header.Controls.Add(version);
            Controls.Add(header);

            // Main Layout Container
            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(16),
                BackColor = Theme.Bg
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));

            // Card 1: Connection & Methods
            _connCard = new Card("CONNECTION") { Dock = DockStyle.Fill };
            _btnWifi = new MethodBtn("WI-FI LOCAL", GetLocalIp() + ":8080", true) { Location = new Point(16, 38), Size = new Size(140, 56) };
            _btnUsb = new MethodBtn("USB CABLE", "ADB 0ms Jitter", false) { Location = new Point(166, 38), Size = new Size(140, 56) };

            _btnWifi.Click += (s, e) => { _btnWifi.Active = true; _btnUsb.Active = false; _btnWifi.Invalidate(); _btnUsb.Invalidate(); };
            _btnUsb.Click += (s, e) => { _btnWifi.Active = false; _btnUsb.Active = true; CheckUsbForward(); _btnWifi.Invalidate(); _btnUsb.Invalidate(); };

            _lblIp = new Label
            {
                Text = "Phone URL: http://" + GetLocalIp() + ":8080",
                ForeColor = Theme.Accent,
                Font = Theme.BodyB,
                Location = new Point(16, 108),
                AutoSize = true
            };
            _lblStatus = new Label
            {
                Text = "Status: Waiting for phone connection on port 8081...",
                ForeColor = Theme.Dim,
                Font = Theme.Small,
                Location = new Point(16, 134),
                AutoSize = true
            };

            _connCard.Controls.Add(_btnWifi);
            _connCard.Controls.Add(_btnUsb);
            _connCard.Controls.Add(_lblIp);
            _connCard.Controls.Add(_lblStatus);
            mainLayout.Controls.Add(_connCard, 0, 0);

            // Card 2: Interactive F1 Pad Diagram (Right Column)
            _diagram = new PadDiagram(_input, _telemetry) { Dock = DockStyle.Fill };
            mainLayout.Controls.Add(_diagram, 1, 0);
            mainLayout.SetRowSpan(_diagram, 2);

            // Card 3: F1 25 UDP Telemetry Status
            _telemCard = new Card("F1 25 TELEMETRY ENGINE (PORT 20777)") { Dock = DockStyle.Fill };
            _lblTelemDetail = new Label
            {
                Text = "F1 25: Waiting for car on track...\n\nEnable in F1 25: Settings -> Telemetry -> UDP On (Port 20777)",
                ForeColor = Theme.Dim,
                Font = Theme.Small,
                Location = new Point(16, 36),
                Size = new Size(290, 70)
            };
            _telemCard.Controls.Add(_lblTelemDetail);
            mainLayout.Controls.Add(_telemCard, 0, 1);

            Controls.Add(mainLayout);

            // Timer for UI refresh
            _tickTimer = new System.Windows.Forms.Timer { Interval = 33 }; // 30fps
            _tickTimer.Tick += (s, e) =>
            {
                _diagram.Invalidate();

                if (_telemetry.Data.Active)
                {
                    _lblTelemDetail.Text = string.Format(
                        "F1 25: ACTIVE\nGear: {0} | Speed: {1} KM/H | RPM: {2}\nDRS: {3} | ERS: {4:0}% | Brake Bias: {5}%",
                        _telemetry.Data.Gear == -1 ? "R" : (_telemetry.Data.Gear == 0 ? "N" : _telemetry.Data.Gear.ToString()),
                        _telemetry.Data.SpeedKmh,
                        _telemetry.Data.EngineRPM,
                        _telemetry.Data.DrsActive ? "OPEN" : (_telemetry.Data.DrsAllowed ? "READY" : "OFF"),
                        _telemetry.Data.ErsPercent,
                        _telemetry.Data.BrakeBias
                    );
                    _lblTelemDetail.ForeColor = Theme.Good;
                }

                if (_input.PacketsReceived > 0)
                {
                    _lblStatus.Text = string.Format("Phone: Connected ({0} inputs) | ViGEm: Active", _input.PacketsReceived);
                    _lblStatus.ForeColor = Theme.Good;
                }
            };
            _tickTimer.Start();
        }

        private void CheckUsbForward()
        {
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "adb.exe";
                p.StartInfo.Arguments = "reverse tcp:8080 tcp:8080";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.Start();

                Process p2 = new Process();
                p2.StartInfo.FileName = "adb.exe";
                p2.StartInfo.Arguments = "reverse udp:8081 udp:8081";
                p2.StartInfo.UseShellExecute = false;
                p2.StartInfo.CreateNoWindow = true;
                p2.Start();
            }
            catch { }
        }

        private static string GetLocalIp()
        {
            try
            {
                using (Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    s.Connect("8.8.8.8", 65530);
                    IPEndPoint ep = s.LocalEndPoint as IPEndPoint;
                    if (ep != null) return ep.Address.ToString();
                }
            }
            catch { }
            return "127.0.0.1";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _tickTimer.Stop();
            _input.Stop();
            _telemetry.Stop();
            _vigem.Close();
            base.OnFormClosing(e);
        }
    }

    // =========================================================================
    // 9. HTTP Companion Server for Phone Browser / APK
    // =========================================================================
    public class HttpServer
    {
        private HttpListener _listener;
        private Thread _thread;
        private bool _running;
        private readonly F1TelemetryListener _telemetry;
        private readonly PhoneInputReceiver _input;
        private readonly ViGEmBridge _bridge;
        private readonly string _webRoot;

        public HttpServer(F1TelemetryListener telemetry, PhoneInputReceiver input, ViGEmBridge bridge, string webRoot)
        {
            _telemetry = telemetry;
            _input = input;
            _bridge = bridge;
            _webRoot = webRoot;
        }

        public void Start(int port)
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://*:" + port + "/");
                _listener.Start();
                _running = true;
                _thread = new Thread(ListenLoop);
                _thread.IsBackground = true;
                _thread.Start();
            }
            catch
            {
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add("http://localhost:" + port + "/");
                    _listener.Start();
                    _running = true;
                    _thread = new Thread(ListenLoop);
                    _thread.IsBackground = true;
                    _thread.Start();
                }
                catch { }
            }
        }

        public void Stop()
        {
            _running = false;
            if (_listener != null) { _listener.Close(); _listener = null; }
        }

        private void ListenLoop()
        {
            while (_running && _listener != null && _listener.IsListening)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem((state) => HandleRequest(ctx));
                }
                catch { }
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                string rawUrl = ctx.Request.Url.AbsolutePath;

                if (rawUrl.Equals("/api/telemetry", StringComparison.OrdinalIgnoreCase))
                {
                    string json = _telemetry.Data.ToJson();
                    byte[] data = Encoding.UTF8.GetBytes(json);
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                    ctx.Response.OutputStream.Write(data, 0, data.Length);
                    ctx.Response.Close();
                    return;
                }
                else if (rawUrl.Equals("/api/status", StringComparison.OrdinalIgnoreCase))
                {
                    string json = string.Format(
                        "{{\"vigemAvailable\":{0},\"vigemPlugged\":{1},\"steer\":{2:0.00},\"throttle\":{3:0.00},\"brake\":{4:0.00},\"packets\":{5},\"f1Active\":{6}}}",
                        _bridge.Available ? "true" : "false", _bridge.Plugged ? "true" : "false",
                        _input.Steer, _input.Throttle, _input.Brake, _input.PacketsReceived,
                        _telemetry.Data.Active ? "true" : "false"
                    );
                    byte[] data = Encoding.UTF8.GetBytes(json);
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                    ctx.Response.OutputStream.Write(data, 0, data.Length);
                    ctx.Response.Close();
                    return;
                }
                else if (rawUrl.Equals("/api/input", StringComparison.OrdinalIgnoreCase) && ctx.Request.HttpMethod == "POST")
                {
                    using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                    {
                        string body = reader.ReadToEnd();
                        _input.ProcessMessage(body);
                    }
                    byte[] resp = Encoding.UTF8.GetBytes("{\"ok\":true}");
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                    ctx.Response.OutputStream.Write(resp, 0, resp.Length);
                    ctx.Response.Close();
                    return;
                }

                // Static files
                string file = rawUrl.TrimStart('/');
                if (string.IsNullOrEmpty(file)) file = "index.html";
                string fullPath = Path.Combine(_webRoot, file);

                if (File.Exists(fullPath))
                {
                    byte[] bytes = File.ReadAllBytes(fullPath);
                    string ext = Path.GetExtension(fullPath).ToLowerInvariant();
                    string mime = "text/plain";
                    if (ext == ".html") mime = "text/html; charset=utf-8";
                    else if (ext == ".css") mime = "text/css";
                    else if (ext == ".js") mime = "application/javascript";
                    else if (ext == ".apk") mime = "application/vnd.android.package-archive";

                    ctx.Response.ContentType = mime;
                    ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    ctx.Response.Close();
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                }
            }
            catch { try { ctx.Response.Close(); } catch { } }
        }
    }

    // =========================================================================
    // 10. Program Entry Point
    // =========================================================================
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string rootDir = Path.GetFullPath(Path.Combine(exeDir, ".."));
            string webRoot = Path.Combine(rootDir, "client-web");
            if (!Directory.Exists(webRoot)) webRoot = exeDir;

            MainForm form = new MainForm();

            // Also start HTTP server for mobile phone pairing
            ViGEmBridge bridge = new ViGEmBridge();
            bridge.Open();
            F1TelemetryListener telemetry = new F1TelemetryListener();
            telemetry.Start(20777);
            PhoneInputReceiver input = new PhoneInputReceiver(bridge);
            input.Start(8081);

            HttpServer http = new HttpServer(telemetry, input, bridge, webRoot);
            http.Start(8080);

            Application.Run(form);
        }
    }
}
