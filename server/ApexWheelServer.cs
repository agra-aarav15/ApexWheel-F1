using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
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
        public static readonly Color Bg          = Color.FromArgb(6, 10, 18);           // Darkest motorsport navy
        public static readonly Color Surface     = Color.FromArgb(12, 18, 30);          // Deep cockpit card
        public static readonly Color SurfaceHi   = Color.FromArgb(20, 30, 48);         // Highlight
        public static readonly Color Stroke      = Color.FromArgb(28, 44, 72);         // Navy border
        public static readonly Color StrokeHi    = Color.FromArgb(37, 99, 235);        // Active accent border
        public static readonly Color Ink         = Color.FromArgb(248, 250, 252);      // Ice white text
        public static readonly Color Dim         = Color.FromArgb(148, 163, 184);      // Slate text
        public static readonly Color Faint       = Color.FromArgb(71, 85, 105);        // Muted navy slate
        public static readonly Color Accent      = Color.FromArgb(0, 240, 255);        // #00F0FF Electric Cyan
        public static readonly Color AccentDeep  = Color.FromArgb(37, 99, 235);        // #2563EB Racing Navy Blue
        public static readonly Color Throttle    = Color.FromArgb(0, 245, 160);        // Electric Neon Green/Cyan Gas
        public static readonly Color Brake       = Color.FromArgb(239, 68, 68);        // #EF4444 Racing Red Brake
        public static readonly Color Good        = Color.FromArgb(16, 185, 129);       // Emerald Green
        public static readonly Color Warn        = Color.FromArgb(245, 158, 11);       // Amber
        public static readonly Color F1Red       = Color.FromArgb(225, 6, 0);          // F1 Red
        public static readonly Color Purple      = Color.FromArgb(168, 85, 247);       // Violet

        public static readonly Font H1         = new Font("Segoe UI", 15f, FontStyle.Bold);
        public static readonly Font Title      = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        public static readonly Font Body       = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        public static readonly Font BodyB      = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        public static readonly Font Small      = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        public static readonly Font SmallB     = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        public static readonly Font BadgeFont  = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        public static readonly Font Mono       = new Font("Consolas", 9.5f, FontStyle.Regular);
        public static readonly Font MonoB      = new Font("Consolas", 13f, FontStyle.Bold);
        public static readonly Font LargeDash  = new Font("Consolas", 28f, FontStyle.Bold);
        public static readonly Font SpeedFont  = new Font("Consolas", 13f, FontStyle.Bold);
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
    // 4. High-Speed Phone Input Receiver (Native Gyropad Ports 5005/5006/5007 & 8081)
    // =========================================================================
    public class PhoneInputReceiver
    {
        private UdpClient _udp8081;
        private UdpClient _udp5005;
        private UdpClient _udpDiscovery;
        private Socket _udpDiscoveryListener;
        private TcpListener _tcpPairServer;
        private Thread _thread8081;
        private Thread _thread5005;
        private Thread _threadHeartbeat5005;
        private Thread _threadDiscovery;
        private Thread _threadDiscoveryListen;
        private Thread _threadPair;
        private bool _running;
        private readonly ViGEmBridge _bridge;

        private IPEndPoint _lastPhoneRemote = null;
        private long _lastPhonePacketTicks = 0;
        private readonly object _phoneLock = new object();

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
            _running = true;

            // 1. Primary Gyropad UDP port 5005
            try
            {
                _udp5005 = new UdpClient(5005);
                try
                {
                    _udp5005.Client.ReceiveBufferSize = 2 * 1024 * 1024;
                    _udp5005.Client.SendBufferSize = 1024 * 1024;
                    const int SIO_UDP_CONNRESET = -1744830452;
                    _udp5005.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
                }
                catch { }

                _thread5005 = new Thread(ListenLoop5005);
                _thread5005.IsBackground = true;
                _thread5005.Start();

                _threadHeartbeat5005 = new Thread(HeartbeatLoop5005);
                _threadHeartbeat5005.IsBackground = true;
                _threadHeartbeat5005.Start();
            }
            catch { }

            // 2. Web client UDP port 8081
            try
            {
                _udp8081 = new UdpClient(port);
                _thread8081 = new Thread(ListenLoop8081);
                _thread8081.IsBackground = true;
                _thread8081.Start();
            }
            catch { }

            // 3. Gyropad Discovery UDP port 5006 broadcast sender
            try
            {
                _udpDiscovery = new UdpClient();
                _udpDiscovery.EnableBroadcast = true;
                _threadDiscovery = new Thread(DiscoveryBroadcastLoop);
                _threadDiscovery.IsBackground = true;
                _threadDiscovery.Start();
            }
            catch { }

            // 3b. Dedicated UDP port 5006 listener for instant response to "Search again"
            try
            {
                _udpDiscoveryListener = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _udpDiscoveryListener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpDiscoveryListener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                _udpDiscoveryListener.Bind(new IPEndPoint(IPAddress.Any, 5006));

                _threadDiscoveryListen = new Thread(DiscoveryListenLoop);
                _threadDiscoveryListen.IsBackground = true;
                _threadDiscoveryListen.Start();
            }
            catch { }

            // 4. Gyropad TCP Pair Server on port 5007
            try
            {
                _tcpPairServer = new TcpListener(IPAddress.Any, 5007);
                _tcpPairServer.Start();
                _threadPair = new Thread(PairServerLoop);
                _threadPair.IsBackground = true;
                _threadPair.Start();
            }
            catch { }
        }

        public void Stop()
        {
            _running = false;
            if (_udp5005 != null) { try { _udp5005.Close(); } catch { } _udp5005 = null; }
            if (_udp8081 != null) { try { _udp8081.Close(); } catch { } _udp8081 = null; }
            if (_udpDiscovery != null) { try { _udpDiscovery.Close(); } catch { } _udpDiscovery = null; }
            if (_udpDiscoveryListener != null) { try { _udpDiscoveryListener.Close(); } catch { } _udpDiscoveryListener = null; }
            if (_tcpPairServer != null) { try { _tcpPairServer.Stop(); } catch { } _tcpPairServer = null; }
        }

        private void ListenLoop5005()
        {
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            byte[] feedbackPacket = new byte[16];
            feedbackPacket[0] = 0x54; // 'T' (Telemetry/Heartbeat ACK)
            feedbackPacket[1] = 0x02; // Gyropad protocol ACK
            long lastImmediateAck = 0;

            while (_running && _udp5005 != null)
            {
                try
                {
                    byte[] bytes = _udp5005.Receive(ref remote);
                    if (bytes != null && bytes.Length > 0)
                    {
                        lock (_phoneLock)
                        {
                            _lastPhoneRemote = remote;
                            _lastPhonePacketTicks = Stopwatch.GetTimestamp();
                        }

                        ProcessRawBytes(bytes);

                        // Send immediate heartbeat ACK back to phone at ~20Hz (every 50ms)
                        long now = Stopwatch.GetTimestamp();
                        if ((now - lastImmediateAck) >= (Stopwatch.Frequency / 20))
                        {
                            lastImmediateAck = now;
                            try { _udp5005.Send(feedbackPacket, feedbackPacket.Length, remote); } catch { }
                        }
                    }
                }
                catch { }
            }
        }

        private void HeartbeatLoop5005()
        {
            byte[] feedbackPacket = new byte[16];
            feedbackPacket[0] = 0x54; // 'T'
            feedbackPacket[1] = 0x02; // Gyropad protocol ACK

            while (_running)
            {
                try
                {
                    IPEndPoint target = null;
                    long lastTicks = 0;
                    lock (_phoneLock)
                    {
                        target = _lastPhoneRemote;
                        lastTicks = _lastPhonePacketTicks;
                    }

                    if (target != null && _udp5005 != null)
                    {
                        long elapsedSec = (Stopwatch.GetTimestamp() - lastTicks) / Stopwatch.Frequency;
                        if (elapsedSec < 60)
                        {
                            _udp5005.Send(feedbackPacket, feedbackPacket.Length, target);
                        }
                    }
                }
                catch { }

                Thread.Sleep(100); // 10Hz heartbeat ping to keep phone's watchdog refreshed
            }
        }

        private void ListenLoop8081()
        {
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running && _udp8081 != null)
            {
                try
                {
                    byte[] bytes = _udp8081.Receive(ref remote);
                    if (bytes != null && bytes.Length > 0)
                    {
                        ProcessRawBytes(bytes);
                    }
                }
                catch { }
            }
        }

        private static List<IPAddress> GetBroadcastAddresses()
        {
            var list = new List<IPAddress>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var ipProps = ni.GetIPProperties();
                    foreach (var u in ipProps.UnicastAddresses)
                    {
                        if (u.Address.AddressFamily == AddressFamily.InterNetwork && u.IPv4Mask != null)
                        {
                            byte[] ipBytes = u.Address.GetAddressBytes();
                            byte[] maskBytes = u.IPv4Mask.GetAddressBytes();
                            if (ipBytes.Length == 4 && maskBytes.Length == 4)
                            {
                                byte[] bcastBytes = new byte[4];
                                for (int i = 0; i < 4; i++)
                                {
                                    bcastBytes[i] = (byte)(ipBytes[i] | (~maskBytes[i] & 0xFF));
                                }
                                var bcast = new IPAddress(bcastBytes);
                                if (!list.Contains(bcast))
                                {
                                    list.Add(bcast);
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            if (!list.Contains(IPAddress.Broadcast))
            {
                list.Add(IPAddress.Broadcast);
            }
            return list;
        }

        private void DiscoveryBroadcastLoop()
        {
            string pcName = Environment.MachineName.Replace(" ", "_");
            if (string.IsNullOrEmpty(pcName)) pcName = "ApexWheel";
            byte[] announce = Encoding.ASCII.GetBytes("PW2 ANNOUNCE PC " + pcName + " 5007\n");

            while (_running && _udpDiscovery != null)
            {
                try
                {
                    var targets = GetBroadcastAddresses();
                    foreach (var addr in targets)
                    {
                        try
                        {
                            _udpDiscovery.Send(announce, announce.Length, new IPEndPoint(addr, 5006));
                        }
                        catch { }
                    }
                }
                catch { }
                Thread.Sleep(800);
            }
        }

        private void DiscoveryListenLoop()
        {
            byte[] buf = new byte[1024];
            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
            string pcName = Environment.MachineName.Replace(" ", "_");
            if (string.IsNullOrEmpty(pcName)) pcName = "ApexWheel";
            byte[] announceBytes = Encoding.ASCII.GetBytes("PW2 ANNOUNCE PC " + pcName + " 5007\n");
            byte[] pairAckBytes = Encoding.ASCII.GetBytes("PW2 PAIRACK " + pcName + " 5007\n");

            while (_running && _udpDiscoveryListener != null)
            {
                try
                {
                    int received = _udpDiscoveryListener.ReceiveFrom(buf, ref remoteEP);
                    if (received > 0)
                    {
                        string msg = Encoding.ASCII.GetString(buf, 0, received).Trim();
                        // If phone announces or searches for PC:
                        if (msg.StartsWith("PW2 ANNOUNCE PHONE", StringComparison.OrdinalIgnoreCase) ||
                            msg.StartsWith("PW2 DISCOVER", StringComparison.OrdinalIgnoreCase))
                        {
                            _udpDiscoveryListener.SendTo(announceBytes, remoteEP);
                            var ip = ((IPEndPoint)remoteEP).Address;
                            _udpDiscoveryListener.SendTo(announceBytes, new IPEndPoint(ip, 5006));
                        }
                        else if (msg.StartsWith("PW2 PAIRME", StringComparison.OrdinalIgnoreCase))
                        {
                            _udpDiscoveryListener.SendTo(pairAckBytes, remoteEP);
                            var ip = ((IPEndPoint)remoteEP).Address;
                            _udpDiscoveryListener.SendTo(pairAckBytes, new IPEndPoint(ip, 5006));
                            _udpDiscoveryListener.SendTo(announceBytes, new IPEndPoint(ip, 5006));
                        }
                    }
                }
                catch (SocketException) { }
                catch (Exception) { }
            }
        }

        private void PairServerLoop()
        {
            while (_running && _tcpPairServer != null)
            {
                try
                {
                    TcpClient client = _tcpPairServer.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem((state) =>
                    {
                        try
                        {
                            client.NoDelay = true;
                            client.ReceiveTimeout = 2000;
                            client.SendTimeout = 2000;
                            using (var stream = client.GetStream())
                            {
                                byte[] buf = new byte[256];
                                int read = stream.Read(buf, 0, buf.Length);
                                byte[] ok = Encoding.ASCII.GetBytes("OK 123456\n");
                                stream.Write(ok, 0, ok.Length);
                                stream.Flush();
                            }
                        }
                        catch { }
                        finally { try { client.Close(); } catch { } }
                    });
                }
                catch { }
            }
        }

        public static ushort MapPhoneButtonsToXbox(ushort phoneMask)
        {
            ushort x = 0;
            // Paddle Shifters
            if ((phoneMask & (1 << 0)) != 0) x |= ViGEmBridge.LEFT_SHOULDER;  // Left paddle: Gear Down (LB)
            if ((phoneMask & (1 << 1)) != 0) x |= ViGEmBridge.RIGHT_SHOULDER; // Right paddle: Gear Up (RB)
            // Quick Buttons
            if ((phoneMask & (1 << 2)) != 0) x |= ViGEmBridge.START;          // IGN: Start
            if ((phoneMask & (1 << 3)) != 0) x |= ViGEmBridge.LEFT_THUMB;     // LIGHTS: Left Thumb
            if ((phoneMask & (1 << 4)) != 0) x |= ViGEmBridge.RIGHT_THUMB;    // HORN: Right Thumb
            if ((phoneMask & (1 << 5)) != 0) x |= ViGEmBridge.GUIDE;          // CAM: Guide
            if ((phoneMask & (1 << 7)) != 0) x |= ViGEmBridge.BACK;           // MAP: Back
            // D-Pad
            if ((phoneMask & (1 << 8)) != 0)  x |= ViGEmBridge.DPAD_UP;
            if ((phoneMask & (1 << 9)) != 0)  x |= ViGEmBridge.DPAD_LEFT;
            if ((phoneMask & (1 << 10)) != 0) x |= ViGEmBridge.DPAD_DOWN;
            if ((phoneMask & (1 << 11)) != 0) x |= ViGEmBridge.DPAD_RIGHT;
            // Face Buttons
            if ((phoneMask & (1 << 12)) != 0) x |= ViGEmBridge.Y;
            if ((phoneMask & (1 << 13)) != 0) x |= ViGEmBridge.X;
            if ((phoneMask & (1 << 14)) != 0) x |= ViGEmBridge.A;
            if ((phoneMask & (1 << 15)) != 0) x |= ViGEmBridge.B;
            return x;
        }

        public void ProcessRawBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;

            // Gyropad Native Racepad Binary Packet: 'P' (0x50)
            if (bytes[0] == 80 && bytes.Length >= 16)
            {
                // Bytes 4-5: Steering angle (short, Little Endian)
                short sSteer = BitConverter.ToInt16(bytes, 4);
                double rawSteer = Math.Max(-1.0, Math.Min(1.0, sSteer / 32767.0));

                // Bytes 6-7: Gas/Throttle (ushort, Little Endian)
                ushort uGas = BitConverter.ToUInt16(bytes, 6);
                double rawGas = Math.Max(0.0, Math.Min(1.0, uGas / 65535.0));

                // Bytes 8-9: Brake (ushort, Little Endian)
                ushort uBrake = BitConverter.ToUInt16(bytes, 8);
                double rawBrake = Math.Max(0.0, Math.Min(1.0, uBrake / 65535.0));

                // Bytes 10-11: Clutch (ushort, Little Endian)
                ushort uClutch = BitConverter.ToUInt16(bytes, 10);
                double rawClutch = Math.Max(0.0, Math.Min(1.0, uClutch / 65535.0));

                // Bytes 14-15: Buttons bitmask (ushort)
                ushort rawBtnMask = BitConverter.ToUInt16(bytes, 14);
                ushort mappedButtons = MapPhoneButtonsToXbox(rawBtnMask);

                Steer = rawSteer;
                Throttle = rawGas;
                Brake = rawBrake;
                Clutch = rawClutch;
                Buttons = mappedButtons;
                PacketsReceived++;
                LastInputTime = DateTime.UtcNow;

                _bridge.Feed(Steer, 0.0, 0.0, 0.0, Brake, Throttle, Buttons);
                return;
            }

            // Gyropad Native Gamepad Binary Packet: 'G' (0x47)
            if (bytes[0] == 71 && bytes.Length >= 18)
            {
                short lx = BitConverter.ToInt16(bytes, 4);
                short ly = BitConverter.ToInt16(bytes, 6);
                short rx = BitConverter.ToInt16(bytes, 8);
                short ry = BitConverter.ToInt16(bytes, 10);
                ushort lt = BitConverter.ToUInt16(bytes, 12);
                ushort rt = BitConverter.ToUInt16(bytes, 14);
                ushort btn = BitConverter.ToUInt16(bytes, 16);

                double nLx = Math.Max(-1.0, Math.Min(1.0, lx / 32767.0));
                double nLy = Math.Max(-1.0, Math.Min(1.0, ly / 32767.0));
                double nRx = Math.Max(-1.0, Math.Min(1.0, rx / 32767.0));
                double nRy = Math.Max(-1.0, Math.Min(1.0, ry / 32767.0));
                double nLt = Math.Max(0.0, Math.Min(1.0, lt / 65535.0));
                double nRt = Math.Max(0.0, Math.Min(1.0, rt / 65535.0));

                Steer = nLx;
                Throttle = nRt;
                Brake = nLt;
                Buttons = btn;
                PacketsReceived++;
                LastInputTime = DateTime.UtcNow;

                _bridge.Feed(nLx, nLy, nRx, nRy, nLt, nRt, btn);
                return;
            }

            // Fallback: Text format (Web client)
            try
            {
                string msg = Encoding.ASCII.GetString(bytes);
                ProcessTextMessage(msg);
            }
            catch { }
        }

        public void ProcessMessage(string msg)
        {
            ProcessTextMessage(msg);
        }

        private void ProcessTextMessage(string msg)
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
    // 5. Ultimate F1 Cockpit Interactive Animated PadDiagram
    // =========================================================================
    public class PadDiagram : Control
    {
        private readonly PhoneInputReceiver _input;
        private readonly F1TelemetryListener _telemetry;

        // Smooth 60FPS animation interpolators
        private float _smoothSteer = 0f;
        private float _smoothThrottle = 0f;
        private float _smoothBrake = 0f;
        private float _animTime = 0f;
        private float _pulsePhase = 0f;

        public PadDiagram(PhoneInputReceiver input, F1TelemetryListener telemetry)
        {
            _input = input;
            _telemetry = telemetry;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void UpdateFrame(float dt)
        {
            _animTime += dt;
            _pulsePhase += dt * 4f;

            // Smooth linear interpolation (lerp) for liquid 60 FPS motion
            _smoothSteer += (float)(_input.Steer - _smoothSteer) * 0.22f;
            _smoothThrottle += (float)(_input.Throttle - _smoothThrottle) * 0.28f;
            _smoothBrake += (float)(_input.Brake - _smoothBrake) * 0.28f;

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Theme.Bg);

            int w = Width;
            int h = Height;

            // Outer Cockpit Frame
            Rectangle cockpitRect = new Rectangle(8, 8, w - 16, h - 16);
            using (GraphicsPath path = GetRoundedRect(cockpitRect, 18))
            {
                using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (Pen p = new Pen(Theme.Stroke, 1.5f)) g.DrawPath(p, path);
            }

            // Top Status Header Pill
            DrawTopStatusPill(g, cockpitRect);

            // Left Pedal: Racing Brake (Glowing Coral/Red)
            int pedalW = 54;
            int pedalH = (int)(cockpitRect.Height * 0.68);
            int pedalY = cockpitRect.Y + (cockpitRect.Height - pedalH) / 2 + 10;
            Rectangle brakeRect = new Rectangle(cockpitRect.X + 20, pedalY, pedalW, pedalH);
            DrawPedal(g, brakeRect, "BRAKE", _smoothBrake, Color.FromArgb(239, 68, 68), Color.FromArgb(244, 63, 94));

            // Right Pedal: Racing Gas (Glowing Cyan/Emerald)
            Rectangle throttleRect = new Rectangle(cockpitRect.Right - pedalW - 20, pedalY, pedalW, pedalH);
            DrawPedal(g, throttleRect, "GAS", _smoothThrottle, Color.FromArgb(16, 185, 129), Color.FromArgb(0, 240, 255));

            // Center: Formula 1 Steering Wheel Dashboard
            int wheelCenterX = cockpitRect.X + cockpitRect.Width / 2;
            int wheelCenterY = cockpitRect.Y + cockpitRect.Height / 2 + 14;
            DrawF1SteeringWheel(g, wheelCenterX, wheelCenterY);
        }

        private void DrawTopStatusPill(Graphics g, Rectangle bounds)
        {
            int pillW = Math.Min(420, bounds.Width - 160);
            int pillH = 30;
            Rectangle pillRect = new Rectangle(bounds.X + (bounds.Width - pillW) / 2, bounds.Y + 14, pillW, pillH);

            using (GraphicsPath path = GetRoundedRect(pillRect, 10))
            {
                using (SolidBrush b = new SolidBrush(Theme.SurfaceHi)) g.FillPath(b, path);
                using (Pen p = new Pen(Theme.Stroke, 1.2f)) g.DrawPath(p, path);
            }

            bool connected = _input.PacketsReceived > 0;
            // Pulsing dot
            int alpha = connected ? (int)(180 + 75 * Math.Sin(_pulsePhase)) : 120;
            if (alpha < 60) alpha = 60; if (alpha > 255) alpha = 255;
            Color dotCol = connected ? Color.FromArgb(alpha, 16, 185, 129) : Color.FromArgb(alpha, 245, 158, 11);

            int dotR = 8;
            Rectangle dotRect = new Rectangle(pillRect.X + 14, pillRect.Y + (pillH - dotR) / 2, dotR, dotR);
            using (SolidBrush b = new SolidBrush(dotCol)) g.FillEllipse(b, dotRect);

            string statusText = connected ?
                string.Format("CONNECTED: ASUS PC COMPANION • {0} PKTS", _input.PacketsReceived) :
                "READY: WAITING FOR APEXWHEEL APP (PORT 5005 / 5006)";

            using (SolidBrush b = new SolidBrush(connected ? Theme.Ink : Theme.Dim))
            {
                var sf = new StringFormat { LineAlignment = StringAlignment.Center };
                g.DrawString(statusText, Theme.SmallB, b, new RectangleF(pillRect.X + 28, pillRect.Y, pillRect.Width - 32, pillH), sf);
            }
        }

        private void DrawPedal(Graphics g, Rectangle r, string label, float fillVal, Color colBase, Color colGlow)
        {
            // Pedal Backplate (Dark Carbon)
            using (GraphicsPath path = GetRoundedRect(r, 12))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(12, 17, 28))) g.FillPath(b, path);
                using (Pen p = new Pen(Theme.Stroke, 1.5f)) g.DrawPath(p, path);
            }

            // Metal Anti-Slip Grips (Notches)
            int notchCount = 6;
            int notchSpacing = (r.Height - 30) / notchCount;
            using (Pen np = new Pen(Color.FromArgb(28, 40, 62), 2f))
            {
                for (int i = 0; i < notchCount; i++)
                {
                    int ny = r.Y + 16 + i * notchSpacing;
                    g.DrawLine(np, r.X + 8, ny, r.Right - 8, ny);
                }
            }

            // Active Glowing Fill
            fillVal = Math.Max(0f, Math.Min(1f, fillVal));
            if (fillVal > 0.005f)
            {
                int fillHeight = (int)((r.Height - 4) * fillVal);
                if (fillHeight < 6) fillHeight = 6;
                Rectangle fillR = new Rectangle(r.X + 3, r.Bottom - fillHeight - 3, r.Width - 6, fillHeight);

                using (GraphicsPath fpath = GetRoundedRect(fillR, 10))
                {
                    using (LinearGradientBrush lgb = new LinearGradientBrush(fillR, colBase, colGlow, LinearGradientMode.Vertical))
                    {
                        g.FillPath(lgb, fpath);
                    }
                }

                // Glowing Horizon Cap Line
                int capY = fillR.Y;
                using (Pen cp = new Pen(Color.White, 2.5f))
                {
                    g.DrawLine(cp, fillR.X + 4, capY, fillR.Right - 4, capY);
                }
            }

            // Percentage & Label
            string pctStr = string.Format("{0:0}%", fillVal * 100f);
            using (SolidBrush b = new SolidBrush(Theme.Ink))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString(label, Theme.SmallB, b, r.X + r.Width / 2, r.Bottom - 26, sf);
                using (SolidBrush pb = new SolidBrush(fillVal > 0.01f ? colGlow : Theme.Dim))
                {
                    g.DrawString(pctStr, Theme.SmallB, pb, r.X + r.Width / 2, r.Y + 8, sf);
                }
            }
        }

        private void DrawF1SteeringWheel(Graphics g, int cx, int cy)
        {
            // Rotation angle from smooth steer (-1.0 to 1.0) -> -90 deg to +90 deg
            float steerAngle = _smoothSteer * 90f;

            GraphicsState state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(steerAngle);

            // 1. Shift Paddles Behind Wheel
            bool lbPressed = (_input.Buttons & ViGEmBridge.LEFT_SHOULDER) != 0;
            bool rbPressed = (_input.Buttons & ViGEmBridge.RIGHT_SHOULDER) != 0;

            int paddleW = 22;
            int paddleH = 68;
            int paddleY = -paddleH / 2;

            // Downshift Paddle (Left)
            int leftPadX = lbPressed ? -164 : -156;
            Rectangle leftPadRect = new Rectangle(leftPadX, paddleY, paddleW, paddleH);
            using (GraphicsPath pth = GetRoundedRect(leftPadRect, 8))
            {
                using (SolidBrush b = new SolidBrush(lbPressed ? Color.FromArgb(59, 130, 246) : Color.FromArgb(18, 26, 42))) g.FillPath(b, pth);
                using (Pen p = new Pen(lbPressed ? Color.White : Theme.Stroke, 1.5f)) g.DrawPath(p, pth);
            }

            // Upshift Paddle (Right)
            int rightPadX = rbPressed ? 142 : 134;
            Rectangle rightPadRect = new Rectangle(rightPadX, paddleY, paddleW, paddleH);
            using (GraphicsPath pth = GetRoundedRect(rightPadRect, 8))
            {
                using (SolidBrush b = new SolidBrush(rbPressed ? Color.FromArgb(0, 240, 255) : Color.FromArgb(18, 26, 42))) g.FillPath(b, pth);
                using (Pen p = new Pen(rbPressed ? Color.White : Theme.Stroke, 1.5f)) g.DrawPath(p, pth);
            }

            // 2. F1 Butterfly Yoke Rim & Ergonomic Grips
            // Outer Rim Crossbars
            Rectangle topBar = new Rectangle(-120, -68, 240, 26);
            Rectangle botBar = new Rectangle(-120, 42, 240, 26);
            using (GraphicsPath pth = GetRoundedRect(topBar, 8))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(15, 22, 36))) g.FillPath(b, pth);
                using (Pen p = new Pen(Theme.Stroke, 1.5f)) g.DrawPath(p, pth);
            }
            using (GraphicsPath pth = GetRoundedRect(botBar, 8))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(15, 22, 36))) g.FillPath(b, pth);
                using (Pen p = new Pen(Theme.Stroke, 1.5f)) g.DrawPath(p, pth);
            }

            // Left Hand Grip (Contoured Textured Leather)
            Rectangle leftGrip = new Rectangle(-148, -62, 38, 124);
            using (GraphicsPath pth = GetRoundedRect(leftGrip, 16))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(20, 28, 46))) g.FillPath(b, pth);
                using (Pen p = new Pen(Theme.AccentDeep, 1.5f)) g.DrawPath(p, pth);
            }
            // Right Hand Grip
            Rectangle rightGrip = new Rectangle(110, -62, 38, 124);
            using (GraphicsPath pth = GetRoundedRect(rightGrip, 16))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(20, 28, 46))) g.FillPath(b, pth);
                using (Pen p = new Pen(Theme.AccentDeep, 1.5f)) g.DrawPath(p, pth);
            }

            // Grip Perforations / Stitching lines
            using (Pen sp = new Pen(Color.FromArgb(35, 50, 80), 1f))
            {
                for (int y = -45; y <= 45; y += 12)
                {
                    g.DrawLine(sp, -142, y, -116, y);
                    g.DrawLine(sp, 116, y, 142, y);
                }
            }

            // 3. Central Chassis Hub Plate (Brushed Titanium Carbon)
            Rectangle hubRect = new Rectangle(-94, -68, 188, 136);
            using (GraphicsPath pth = GetRoundedRect(hubRect, 16))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(10, 16, 28))) g.FillPath(b, pth);
                using (Pen p = new Pen(Theme.StrokeHi, 1.8f)) g.DrawPath(p, pth);
            }

            // 4. Face Buttons (ERS, DRS, PIT, RADIO)
            bool otPressed = (_input.Buttons & ViGEmBridge.LEFT_THUMB) != 0;
            bool drsPressed = (_input.Buttons & ViGEmBridge.A) != 0 || _telemetry.Data.DrsActive;
            bool pitPressed = (_input.Buttons & ViGEmBridge.DPAD_DOWN) != 0;
            bool radioPressed = (_input.Buttons & ViGEmBridge.Y) != 0;

            DrawWheelButton(g, new Rectangle(-84, -28, 38, 22), "OT", otPressed, Color.FromArgb(245, 158, 11));
            DrawWheelButton(g, new Rectangle(46, -28, 38, 22), "DRS", drsPressed, Color.FromArgb(16, 185, 129));
            DrawWheelButton(g, new Rectangle(-84, 4, 38, 22), "PIT", pitPressed, Color.FromArgb(168, 85, 247));
            DrawWheelButton(g, new Rectangle(46, 4, 38, 22), "RAD", radioPressed, Color.FromArgb(6, 182, 212));

            // 5. Integrated Digital F1 LCD Cockpit Dash
            Rectangle lcdRect = new Rectangle(-44, -58, 88, 116);
            using (GraphicsPath pth = GetRoundedRect(lcdRect, 10))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(3, 6, 12))) g.FillPath(b, pth);
                using (Pen p = new Pen(Color.FromArgb(35, 52, 85), 1.5f)) g.DrawPath(p, pth);
            }

            // Top 15 Rev LEDs (Shift Lights)
            DrawRevLeds(g, -38, -48, 76);

            // Big Central Gear Number
            string gearText = _telemetry.Data.Gear == -1 ? "R" : (_telemetry.Data.Gear == 0 ? "N" : _telemetry.Data.Gear.ToString());
            Color gearCol = _telemetry.Data.Gear == -1 ? Color.FromArgb(239, 68, 68) :
                            (_telemetry.Data.Gear == 0 ? Color.FromArgb(16, 185, 129) : Color.FromArgb(240, 246, 255));

            using (SolidBrush gb = new SolidBrush(gearCol))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(gearText, Theme.LargeDash, gb, new RectangleF(-40, -32, 80, 48), sf);
            }

            // Speed KM/H Readout
            int speedKmh = _telemetry.Data.Active ? _telemetry.Data.SpeedKmh : (int)(_smoothThrottle * 315);
            string spdStr = speedKmh.ToString();
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(0, 240, 255)))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString(spdStr, Theme.SpeedFont, sb, 0, 16, sf);
            }
            using (SolidBrush lb = new SolidBrush(Theme.Dim))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString("KM/H", Theme.BadgeFont, lb, 0, 34, sf);
            }

            // Bottom DRS & ERS Mini Badges
            string drsStatus = _telemetry.Data.DrsActive ? "DRS OPEN" : (_telemetry.Data.DrsAllowed ? "DRS RDY" : "DRS");
            Color drsColor = _telemetry.Data.DrsActive ? Color.FromArgb(16, 185, 129) :
                             (_telemetry.Data.DrsAllowed ? Color.FromArgb(245, 158, 11) : Theme.Faint);
            using (SolidBrush db = new SolidBrush(drsColor))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString(drsStatus, Theme.BadgeFont, db, 0, 46, sf);
            }

            g.Restore(state);
        }

        private void DrawWheelButton(Graphics g, Rectangle r, string text, bool pressed, Color colActive)
        {
            Color bg = pressed ? colActive : Color.FromArgb(16, 24, 40);
            Color border = pressed ? Color.White : Theme.Stroke;
            Color fg = pressed ? Color.Black : colActive;

            using (GraphicsPath pth = GetRoundedRect(r, 6))
            {
                using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, pth);
                using (Pen p = new Pen(border, 1.2f)) g.DrawPath(p, pth);
            }

            using (SolidBrush b = new SolidBrush(fg))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(text, Theme.SmallB, b, new RectangleF(r.X, r.Y, r.Width, r.Height), sf);
            }
        }

        private void DrawRevLeds(Graphics g, int startX, int startY, int totalWidth)
        {
            int ledCount = 15;
            int ledSpacing = totalWidth / ledCount;
            int ledR = 4;

            // Compute how many LEDs are lit
            int litCount = 0;
            if (_telemetry.Data.Active)
            {
                float frac = (float)_telemetry.Data.EngineRPM / Math.Max(1, _telemetry.Data.MaxRPM);
                litCount = (int)(frac * ledCount);
            }
            else if (_smoothThrottle > 0.05f)
            {
                litCount = (int)(_smoothThrottle * ledCount);
            }
            else
            {
                // Idle pulse wave
                litCount = (int)((Math.Sin(_animTime * 3.5) + 1.0) * 7.5);
            }

            for (int i = 0; i < ledCount; i++)
            {
                int lx = startX + i * ledSpacing + 2;
                Rectangle lr = new Rectangle(lx, startY, ledR, ledR);

                Color onCol;
                if (i < 5) onCol = Color.FromArgb(16, 185, 129);       // Green
                else if (i < 10) onCol = Color.FromArgb(239, 68, 68);  // Red
                else onCol = Color.FromArgb(59, 130, 246);             // Blue / Purple

                bool isLit = (i < litCount);
                Color drawCol = isLit ? onCol : Color.FromArgb(25, 35, 55);

                using (SolidBrush b = new SolidBrush(drawCol)) g.FillEllipse(b, lr);
                if (isLit)
                {
                    // Mini white specular center
                    using (SolidBrush wb = new SolidBrush(Color.White))
                    {
                        g.FillEllipse(wb, lr.X + 1, lr.Y + 1, 2, 2);
                    }
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
                    g.DrawString(TitleText.ToUpperInvariant(), Theme.SmallB, b, 16, 12);
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
    // 7. Modern Toggle Method Button (Wi-Fi / USB)
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
            Color border = Active ? Theme.StrokeHi : Theme.Stroke;

            using (GraphicsPath path = GetRoundedRect(r, 10))
            {
                using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, path);
                using (Pen p = new Pen(border, Active ? 2f : 1f)) g.DrawPath(p, path);
            }

            using (SolidBrush b = new SolidBrush(Active ? Theme.Ink : Theme.Dim))
            {
                g.DrawString(Title, Theme.BodyB, b, 14, 10);
            }
            using (SolidBrush b = new SolidBrush(Active ? Theme.Accent : Theme.Faint))
            {
                g.DrawString(Subtitle, Theme.Small, b, 14, 28);
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

        public ViGEmBridge ViGEm { get { return _vigem; } }
        public F1TelemetryListener Telemetry { get { return _telemetry; } }
        public PhoneInputReceiver Input { get { return _input; } }

        private PadDiagram _diagram;
        private Card _connCard;
        private Card _telemCard;

        private Label _lblIp;
        private Label _lblStatus;
        private Label _lblVigem;
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
            Text = "ApexWheel F1 • Ultimate Cockpit Dashboard v1.1.0";
            Size = new Size(960, 680);
            MinimumSize = new Size(880, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Ink;
            Font = Theme.Body;

            // Header Banner
            Panel header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Surface, Padding = new Padding(20, 14, 20, 14) };
            Label title = new Label
            {
                Text = "🏎️ APEXWHEEL F1",
                Font = Theme.H1,
                ForeColor = Theme.Ink,
                AutoSize = true,
                Location = new Point(20, 14)
            };
            Label version = new Label
            {
                Text = "v1.1.0 PC COMPANION • HIGH-PERFORMANCE 60Hz XBOX 360 & F1 25 UDP TELEMETRY",
                Font = Theme.SmallB,
                ForeColor = Theme.Dim,
                AutoSize = true,
                Location = new Point(220, 20)
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
                Padding = new Padding(14),
                BackColor = Theme.Bg
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330f));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            // Card 1: Connection & Methods
            _connCard = new Card("CONNECTION & CONTROLLER") { Dock = DockStyle.Fill };
            _btnWifi = new MethodBtn("WI-FI LOCAL", GetLocalIp() + ":5005", true) { Location = new Point(14, 38), Size = new Size(142, 54) };
            _btnUsb = new MethodBtn("USB CABLE", "ADB 0ms Jitter", false) { Location = new Point(162, 38), Size = new Size(142, 54) };

            _btnWifi.Click += (s, e) => { _btnWifi.Active = true; _btnUsb.Active = false; _btnWifi.Invalidate(); _btnUsb.Invalidate(); };
            _btnUsb.Click += (s, e) => { _btnWifi.Active = false; _btnUsb.Active = true; CheckUsbForward(); _btnWifi.Invalidate(); _btnUsb.Invalidate(); };

            _lblIp = new Label
            {
                Text = "LAN IP: " + GetLocalIp() + " (Ports 5005, 5006, 5007)",
                ForeColor = Theme.Accent,
                Font = Theme.SmallB,
                Location = new Point(14, 102),
                AutoSize = true
            };
            _lblVigem = new Label
            {
                Text = "● ViGEmBus: Xbox 360 Controller Active",
                ForeColor = Theme.Good,
                Font = Theme.SmallB,
                Location = new Point(14, 126),
                AutoSize = true
            };
            _lblStatus = new Label
            {
                Text = "Waiting for phone connection...",
                ForeColor = Theme.Dim,
                Font = Theme.Small,
                Location = new Point(14, 150),
                AutoSize = true
            };

            _connCard.Controls.Add(_btnWifi);
            _connCard.Controls.Add(_btnUsb);
            _connCard.Controls.Add(_lblIp);
            _connCard.Controls.Add(_lblVigem);
            _connCard.Controls.Add(_lblStatus);
            mainLayout.Controls.Add(_connCard, 0, 0);

            // Card 2: Interactive F1 Pad Diagram (Right Column spanning both rows)
            _diagram = new PadDiagram(_input, _telemetry) { Dock = DockStyle.Fill };
            mainLayout.Controls.Add(_diagram, 1, 0);
            mainLayout.SetRowSpan(_diagram, 2);

            // Card 3: F1 25 UDP Telemetry Status
            _telemCard = new Card("F1 25 LIVE TELEMETRY (PORT 20777)") { Dock = DockStyle.Fill };
            _lblTelemDetail = new Label
            {
                Text = @"F1 25: Waiting for car on track...

Enable in F1 25: Settings -> Telemetry -> UDP On (Port 20777)",
                ForeColor = Theme.Dim,
                Font = Theme.Small,
                Location = new Point(14, 38),
                Size = new Size(295, 120)
            };
            _telemCard.Controls.Add(_lblTelemDetail);
            mainLayout.Controls.Add(_telemCard, 0, 1);

            Controls.Add(mainLayout);

            // 60 FPS Buttery-Smooth Timer
            _tickTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _tickTimer.Tick += (s, e) =>
            {
                _diagram.UpdateFrame(0.016f);

                if (_telemetry.Data.Active)
                {
                    _lblTelemDetail.Text = string.Format(
                        @"F1 25 TELEMETRY: ACTIVE
Gear: {0} | Speed: {1} KM/H
Engine RPM: {2:N0} / {3:N0}
DRS: {4} | ERS: {5:0}%
Brake Bias: {6}% Front",
                        _telemetry.Data.Gear == -1 ? "R" : (_telemetry.Data.Gear == 0 ? "N" : _telemetry.Data.Gear.ToString()),
                        _telemetry.Data.SpeedKmh,
                        _telemetry.Data.EngineRPM,
                        _telemetry.Data.MaxRPM,
                        _telemetry.Data.DrsActive ? "OPEN" : (_telemetry.Data.DrsAllowed ? "READY" : "OFF"),
                        _telemetry.Data.ErsPercent,
                        _telemetry.Data.BrakeBias
                    );
                    _lblTelemDetail.ForeColor = Theme.Good;
                }

                if (_input.PacketsReceived > 0)
                {
                    _lblStatus.Text = string.Format(@"Phone: 60Hz Stream ({0} inputs)
ASUS PC Broadcast: Active", _input.PacketsReceived);
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
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unhandled_domain.log"), e.ExceptionObject.ToString()); } catch { }
            };
            Application.ThreadException += (s, e) =>
            {
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "thread_exception.log"), e.Exception.ToString()); } catch { }
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string rootDir = Path.GetFullPath(Path.Combine(exeDir, ".."));
                string webRoot = Path.Combine(rootDir, "client-web");
                if (!Directory.Exists(webRoot)) webRoot = exeDir;

                MainForm form = new MainForm();

                HttpServer http = new HttpServer(form.Telemetry, form.Input, form.ViGEm, webRoot);
                http.Start(8080);

                Application.Run(form);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_crash.log"), ex.ToString());
            }
        }
    }
}
