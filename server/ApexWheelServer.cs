using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Windows.Input;

namespace ApexWheel
{
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
                _udp5005 = new UdpClient();
                _udp5005.ExclusiveAddressUse = false;
                _udp5005.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp5005.Client.Bind(new IPEndPoint(IPAddress.Any, 5005));
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
                _udp8081 = new UdpClient();
                _udp8081.ExclusiveAddressUse = false;
                _udp8081.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp8081.Client.Bind(new IPEndPoint(IPAddress.Any, port));
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
                _tcpPairServer.ExclusiveAddressUse = false;
                _tcpPairServer.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
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
                            client.LingerState = new LingerOption(true, 2);
                            client.ReceiveTimeout = 4000;
                            client.SendTimeout = 4000;

                            IPEndPoint remoteEp = null;
                            try { remoteEp = (IPEndPoint)client.Client.RemoteEndPoint; } catch { }

                            using (var stream = client.GetStream())
                            using (var reader = new StreamReader(stream, Encoding.ASCII))
                            using (var writer = new StreamWriter(stream, Encoding.ASCII))
                            {
                                writer.NewLine = "\n";
                                string line = reader.ReadLine();
                                if (!string.IsNullOrEmpty(line))
                                {
                                    line = line.Trim();
                                    string[] parts = line.Split(' ');
                                    string cmd = parts.Length > 0 ? parts[0].ToUpperInvariant() : "";

                                    string token = "123456";
                                    if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
                                    {
                                        token = parts[1];
                                    }

                                    if (cmd == "HELLO")
                                    {
                                        writer.WriteLine("OK");
                                        writer.Flush();
                                    }
                                    else
                                    {
                                        writer.WriteLine("OK " + token);
                                        writer.Flush();
                                    }

                                    if (remoteEp != null)
                                    {
                                        lock (_phoneLock)
                                        {
                                            _lastPhoneRemote = new IPEndPoint(remoteEp.Address, 5005);
                                            _lastPhonePacketTicks = Stopwatch.GetTimestamp();
                                        }
                                    }

                                    try { client.Client.Shutdown(SocketShutdown.Send); } catch { }
                                    Thread.Sleep(50);
                                }
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
    // 5. Ultra-Modern Fluent WPF F1 Cockpit Dashboard
    // =========================================================================
    public class MainWindow : Window
    {
        private readonly ViGEmBridge _vigem;
        private readonly F1TelemetryListener _telemetry;
        private readonly PhoneInputReceiver _input;

        public ViGEmBridge ViGEm { get { return _vigem; } }
        public F1TelemetryListener Telemetry { get { return _telemetry; } }
        public PhoneInputReceiver Input { get { return _input; } }

        // Interpolation
        private float _smoothSteer = 0f;
        private float _smoothThrottle = 0f;
        private float _smoothBrake = 0f;
        private float _animTime = 0f;

        // UI Controls - Header & Status
        private TextBlock _txtStatus;
        private Ellipse _dotStatus;

        // Left Column Controls
        private TextBlock _lblIp;
        private TextBlock _lblVigem;
        private TextBlock _lblPackets;
        private TextBlock _lblTelemDetail;
        private Border _btnWifi;
        private Border _btnUsb;

        // Right Column - Cockpit Elements
        private Canvas _cockpitCanvas;
        private Rectangle _brakeFill;
        private Rectangle _brakeGlow;
        private TextBlock _txtBrakePct;
        private Rectangle _gasFill;
        private Rectangle _gasGlow;
        private TextBlock _txtGasPct;

        // Steering Wheel Components
        private Canvas _wheelCanvas;
        private RotateTransform _wheelRot;
        private Border _leftPaddle;
        private Border _rightPaddle;
        private Border _btnOt;
        private Border _btnDrs;
        private Border _btnPit;
        private Border _btnRad;
        private TextBlock _txtGear;
        private TextBlock _txtSpeed;
        private TextBlock _txtDrs;
        private List<Ellipse> _revLeds;

        private DispatcherTimer _timer;

        public MainWindow()
        {
            // 1. Initialize Network & Drivers
            _vigem = new ViGEmBridge();
            _vigem.Open();
            _vigem.Plug();

            _telemetry = new F1TelemetryListener();
            _telemetry.Start(20777);

            _input = new PhoneInputReceiver(_vigem);
            _input.Start(8081);

            CheckUsbForward();

            // 2. Window Setup
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Width = 1000;
            Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            InitializeUI();

            // 3. 60 FPS Render Timer
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += OnFrameTick;
            _timer.Start();

            Closing += (s, e) =>
            {
                _timer.Stop();
                _input.Stop();
                _telemetry.Stop();
                _vigem.Close();
            };
        }

        private void InitializeUI()
        {
            // Root Outer Shell (Rounded Dark Glass with Drop Shadow)
            Border rootBorder = new Border
            {
                Margin = new Thickness(16),
                Background = new SolidColorBrush(Color.FromRgb(7, 10, 18)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(26, 40, 68)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(16),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 32,
                    ShadowDepth = 2,
                    Opacity = 0.9
                }
            };

            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 1. Modern Custom Titlebar
            Border titleBar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(11, 16, 28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(22, 34, 58)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(14, 14, 0, 0)
            };
            titleBar.MouseDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };

            Grid titleGrid = new Grid();
            titleGrid.ColumnDefinitions.Add(new ColumnStyleAuto());
            titleGrid.ColumnDefinitions.Add(new ColumnStyleStar());
            titleGrid.ColumnDefinitions.Add(new ColumnStyleAuto());

            // Left Title Group
            StackPanel titleGroup = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            TextBlock iconF1 = new TextBlock { Text = "🏎️", FontSize = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            TextBlock txtTitle = new TextBlock { Text = "APEXWHEEL F1", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            TextBlock txtVer = new TextBlock { Text = " • COCKPIT DASHBOARD v1.1.0", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 2, 0, 0) };
            titleGroup.Children.Add(iconF1);
            titleGroup.Children.Add(txtTitle);
            titleGroup.Children.Add(txtVer);
            Grid.SetColumn(titleGroup, 0);
            titleGrid.Children.Add(titleGroup);

            // Center Status Pill
            Border statusPill = new Border
            {
                Height = 30,
                Padding = new Thickness(14, 0, 14, 0),
                Background = new SolidColorBrush(Color.FromRgb(14, 22, 38)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(28, 44, 76)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(15),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            StackPanel pillStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _dotStatus = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)), Margin = new Thickness(0, 0, 8, 0) };
            _txtStatus = new TextBlock { Text = "READY • WAITING FOR APEXWHEEL APP (PORT 5005 / 5006)", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), VerticalAlignment = VerticalAlignment.Center };
            pillStack.Children.Add(_dotStatus);
            pillStack.Children.Add(_txtStatus);
            statusPill.Child = pillStack;
            Grid.SetColumn(statusPill, 1);
            titleGrid.Children.Add(statusPill);

            // Window Controls (Min / Close)
            StackPanel winBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            Button btnMin = CreateSysButton("—", (s, e) => WindowState = WindowState.Minimized);
            Button btnClose = CreateSysButton("✕", (s, e) => Close(), true);
            winBtns.Children.Add(btnMin);
            winBtns.Children.Add(btnClose);
            Grid.SetColumn(winBtns, 2);
            titleGrid.Children.Add(winBtns);

            titleBar.Child = titleGrid;
            mainGrid.Children.Add(titleBar);
            Grid.SetRow(titleBar, 0);

            // 2. Main Body Grid (Left Column: Cards, Right Column: Cockpit)
            Grid bodyGrid = new Grid { Margin = new Thickness(14) };
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left Column Stack
            StackPanel leftStack = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };

            // Card 1: Connection & Hardware
            Border cardConn = CreateCard("CONNECTION & HARDWARE");
            StackPanel connContent = new StackPanel();

            Grid methodGrid = new Grid { Margin = new Thickness(0, 4, 0, 12) };
            methodGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            methodGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _btnWifi = CreateMethodTab("WI-FI LOCAL", GetLocalIp() + ":5005", true);
            _btnUsb = CreateMethodTab("USB CABLE", "ADB 0ms Jitter", false);
            _btnWifi.MouseLeftButtonDown += (s, e) => { SetTabActive(_btnWifi, true); SetTabActive(_btnUsb, false); };
            _btnUsb.MouseLeftButtonDown += (s, e) => { SetTabActive(_btnWifi, false); SetTabActive(_btnUsb, true); CheckUsbForward(); };
            Grid.SetColumn(_btnWifi, 0);
            Grid.SetColumn(_btnUsb, 1);
            methodGrid.Children.Add(_btnWifi);
            methodGrid.Children.Add(_btnUsb);
            connContent.Children.Add(methodGrid);

            _lblIp = new TextBlock { Text = "LAN IP: " + GetLocalIp() + " (UDP 5005 / TCP 5007)", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0, 240, 255)), Margin = new Thickness(2, 0, 0, 6) };
            _lblVigem = new TextBlock { Text = "● ViGEmBus: Xbox 360 Controller Active", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)), Margin = new Thickness(2, 0, 0, 6) };
            _lblPackets = new TextBlock { Text = "Stream: 20Hz Heartbeat ACK • Standby", FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)), Margin = new Thickness(2, 0, 0, 4) };

            connContent.Children.Add(_lblIp);
            connContent.Children.Add(_lblVigem);
            connContent.Children.Add(_lblPackets);
            cardConn.Child = connContent;
            leftStack.Children.Add(cardConn);

            // Card 2: F1 25 Telemetry
            Border cardTelem = CreateCard("F1 25 UDP TELEMETRY (PORT 20777)", 12);
            StackPanel telemContent = new StackPanel();

            _lblTelemDetail = new TextBlock
            {
                Text = @"F1 25: Waiting for car on track...

Enable in F1 25: Settings -> Telemetry -> UDP On (Port 20777)",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                LineHeight = 18,
                TextWrapping = TextWrapping.Wrap
            };
            telemContent.Children.Add(_lblTelemDetail);
            cardTelem.Child = telemContent;
            leftStack.Children.Add(cardTelem);

            Grid.SetColumn(leftStack, 0);
            bodyGrid.Children.Add(leftStack);

            // Right Column: Master F1 Cockpit Canvas
            Border cockpitFrame = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(9, 13, 24)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(24, 38, 64)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(14),
                ClipToBounds = true
            };

            _cockpitCanvas = new Canvas();
            _cockpitCanvas.SizeChanged += (s, e) => LayoutCockpit();
            cockpitFrame.Child = _cockpitCanvas;

            Grid.SetColumn(cockpitFrame, 1);
            bodyGrid.Children.Add(cockpitFrame);

            mainGrid.Children.Add(bodyGrid);
            Grid.SetRow(bodyGrid, 1);

            rootBorder.Child = mainGrid;
            Content = rootBorder;
        }

        private void LayoutCockpit()
        {
            _cockpitCanvas.Children.Clear();
            double cw = _cockpitCanvas.ActualWidth;
            double ch = _cockpitCanvas.ActualHeight;
            if (cw <= 10 || ch <= 10) return;

            // 1. Left Pedal: BRAKE
            double pedalW = 54;
            double pedalH = ch * 0.70;
            double pedalY = (ch - pedalH) / 2;

            Border brakeBase = CreatePedalBase("BRAKE", 24, pedalY, pedalW, pedalH, out _brakeFill, out _brakeGlow, out _txtBrakePct, Color.FromRgb(239, 68, 68), Color.FromRgb(244, 63, 94));
            _cockpitCanvas.Children.Add(brakeBase);

            // 2. Right Pedal: GAS
            Border gasBase = CreatePedalBase("GAS", cw - pedalW - 24, pedalY, pedalW, pedalH, out _gasFill, out _gasGlow, out _txtGasPct, Color.FromRgb(16, 185, 129), Color.FromRgb(0, 240, 255));
            _cockpitCanvas.Children.Add(gasBase);

            // 3. Center: Rotating Formula 1 Steering Wheel
            double cx = cw / 2;
            double cy = ch / 2 + 10;

            _wheelCanvas = new Canvas
            {
                Width = 360,
                Height = 220,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            _wheelRot = new RotateTransform(0);
            _wheelCanvas.RenderTransform = _wheelRot;

            Canvas.SetLeft(_wheelCanvas, cx - 180);
            Canvas.SetTop(_wheelCanvas, cy - 110);

            BuildF1Wheel(_wheelCanvas);
            _cockpitCanvas.Children.Add(_wheelCanvas);
        }

        private Border CreatePedalBase(string label, double x, double y, double w, double h, out Rectangle fill, out Rectangle glow, out TextBlock pctText, Color c1, Color c2)
        {
            Border b = new Border
            {
                Width = w,
                Height = h,
                Background = new SolidColorBrush(Color.FromRgb(13, 19, 32)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(28, 42, 68)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(12),
                ClipToBounds = true
            };
            Canvas.SetLeft(b, x);
            Canvas.SetTop(b, y);

            Grid g = new Grid();

            // Grip Notches
            for (int i = 0; i < 6; i++)
            {
                Line l = new Line
                {
                    X1 = 8,
                    X2 = w - 8,
                    Y1 = 20 + i * ((h - 40) / 6),
                    Y2 = 20 + i * ((h - 40) / 6),
                    Stroke = new SolidColorBrush(Color.FromRgb(24, 34, 54)),
                    StrokeThickness = 2
                };
                g.Children.Add(l);
            }

            // Fill Bar
            fill = new Rectangle
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 0,
                RadiusX = 10,
                RadiusY = 10,
                Fill = new LinearGradientBrush(c1, c2, new Point(0, 1), new Point(0, 0))
            };
            g.Children.Add(fill);

            // Glowing Horizon Cap Line
            glow = new Rectangle
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 3,
                Margin = new Thickness(4, 0, 4, 0),
                Fill = Brushes.White,
                Visibility = Visibility.Hidden
            };
            g.Children.Add(glow);

            // Bottom Label
            TextBlock lbl = new TextBlock
            {
                Text = label,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 10)
            };
            g.Children.Add(lbl);

            // Top Percentage Text
            pctText = new TextBlock
            {
                Text = "0%",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 0, 0)
            };
            g.Children.Add(pctText);

            b.Child = g;
            return b;
        }

        private void BuildF1Wheel(Canvas c)
        {
            // 1. Shift Paddles Behind Wheel
            _leftPaddle = new Border
            {
                Width = 24,
                Height = 68,
                Background = new SolidColorBrush(Color.FromRgb(18, 26, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 45, 72)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(8)
            };
            Canvas.SetLeft(_leftPaddle, 22);
            Canvas.SetTop(_leftPaddle, 76);
            c.Children.Add(_leftPaddle);

            _rightPaddle = new Border
            {
                Width = 24,
                Height = 68,
                Background = new SolidColorBrush(Color.FromRgb(18, 26, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 45, 72)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(8)
            };
            Canvas.SetLeft(_rightPaddle, 314);
            Canvas.SetTop(_rightPaddle, 76);
            c.Children.Add(_rightPaddle);

            // 2. Wheel Rim Crossbars (Top & Bottom)
            Border topBar = new Border
            {
                Width = 240,
                Height = 26,
                Background = new SolidColorBrush(Color.FromRgb(15, 22, 36)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(28, 42, 68)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(8)
            };
            Canvas.SetLeft(topBar, 60);
            Canvas.SetTop(topBar, 42);
            c.Children.Add(topBar);

            Border botBar = new Border
            {
                Width = 240,
                Height = 26,
                Background = new SolidColorBrush(Color.FromRgb(15, 22, 36)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(28, 42, 68)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(8)
            };
            Canvas.SetLeft(botBar, 60);
            Canvas.SetTop(botBar, 152);
            c.Children.Add(botBar);

            // 3. Left & Right Ergonomic Leather Grips
            Border leftGrip = new Border
            {
                Width = 40,
                Height = 126,
                Background = new SolidColorBrush(Color.FromRgb(20, 28, 46)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(16)
            };
            Canvas.SetLeft(leftGrip, 32);
            Canvas.SetTop(leftGrip, 47);
            c.Children.Add(leftGrip);

            Border rightGrip = new Border
            {
                Width = 40,
                Height = 126,
                Background = new SolidColorBrush(Color.FromRgb(20, 28, 46)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(16)
            };
            Canvas.SetLeft(rightGrip, 288);
            Canvas.SetTop(rightGrip, 47);
            c.Children.Add(rightGrip);

            // 4. Central Hub Plate (Brushed Titanium Carbon)
            Border hubPlate = new Border
            {
                Width = 196,
                Height = 138,
                Background = new SolidColorBrush(Color.FromRgb(10, 15, 26)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(16)
            };
            Canvas.SetLeft(hubPlate, 82);
            Canvas.SetTop(hubPlate, 41);
            c.Children.Add(hubPlate);

            // 5. Thumb Buttons (OT, DRS, PIT, RAD)
            _btnOt = CreateWheelBtn("OT", 92, 80, Color.FromRgb(245, 158, 11));
            _btnDrs = CreateWheelBtn("DRS", 228, 80, Color.FromRgb(16, 185, 129));
            _btnPit = CreateWheelBtn("PIT", 92, 114, Color.FromRgb(168, 85, 247));
            _btnRad = CreateWheelBtn("RAD", 228, 114, Color.FromRgb(6, 182, 212));

            c.Children.Add(_btnOt);
            c.Children.Add(_btnDrs);
            c.Children.Add(_btnPit);
            c.Children.Add(_btnRad);

            // 6. Center Digital LCD Dashboard
            Border lcd = new Border
            {
                Width = 88,
                Height = 118,
                Background = new SolidColorBrush(Color.FromRgb(3, 5, 10)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(32, 50, 80)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10)
            };
            Canvas.SetLeft(lcd, 136);
            Canvas.SetTop(lcd, 51);

            Grid lcdGrid = new Grid();

            // Rev LEDs Row
            StackPanel revRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };
            _revLeds = new List<Ellipse>();
            for (int i = 0; i < 15; i++)
            {
                Ellipse e = new Ellipse
                {
                    Width = 4,
                    Height = 4,
                    Margin = new Thickness(0.5, 0, 0.5, 0),
                    Fill = new SolidColorBrush(Color.FromRgb(24, 34, 52))
                };
                _revLeds.Add(e);
                revRow.Children.Add(e);
            }
            lcdGrid.Children.Add(revRow);

            // Gear Number
            _txtGear = new TextBlock
            {
                Text = "N",
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 16)
            };
            lcdGrid.Children.Add(_txtGear);

            // Speed & Unit
            StackPanel spdStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 24)
            };
            _txtSpeed = new TextBlock
            {
                Text = "0",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(0, 240, 255)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            TextBlock txtKmh = new TextBlock
            {
                Text = "KM/H",
                FontSize = 7.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            spdStack.Children.Add(_txtSpeed);
            spdStack.Children.Add(txtKmh);
            lcdGrid.Children.Add(spdStack);

            // DRS Mini Badge
            _txtDrs = new TextBlock
            {
                Text = "DRS",
                FontSize = 7.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 8)
            };
            lcdGrid.Children.Add(_txtDrs);

            lcd.Child = lcdGrid;
            c.Children.Add(lcd);
        }

        private Border CreateWheelBtn(string text, double x, double y, Color col)
        {
            Border b = new Border
            {
                Width = 40,
                Height = 24,
                Background = new SolidColorBrush(Color.FromRgb(16, 24, 40)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(28, 42, 68)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            Canvas.SetLeft(b, x);
            Canvas.SetTop(b, y);

            TextBlock tb = new TextBlock
            {
                Text = text,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(col),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            b.Child = tb;
            return b;
        }

        private void OnFrameTick(object sender, EventArgs e)
        {
            _animTime += 0.016f;

            // Smooth linear interpolation for buttery 60 FPS motion
            _smoothSteer += (float)(_input.Steer - _smoothSteer) * 0.22f;
            _smoothThrottle += (float)(_input.Throttle - _smoothThrottle) * 0.28f;
            _smoothBrake += (float)(_input.Brake - _smoothBrake) * 0.28f;

            // 1. Steering Wheel Rotation
            if (_wheelRot != null)
            {
                _wheelRot.Angle = _smoothSteer * 90f;
            }

            // 2. Pedals Update
            if (_brakeFill != null && _txtBrakePct != null)
            {
                double bh = _smoothBrake * 240;
                _brakeFill.Height = bh;
                _brakeGlow.Margin = new Thickness(4, 0, 4, bh);
                _brakeGlow.Visibility = bh > 4 ? Visibility.Visible : Visibility.Hidden;
                _txtBrakePct.Text = string.Format("{0:0}%", _smoothBrake * 100);
            }

            if (_gasFill != null && _txtGasPct != null)
            {
                double gh = _smoothThrottle * 240;
                _gasFill.Height = gh;
                _gasGlow.Margin = new Thickness(4, 0, 4, gh);
                _gasGlow.Visibility = gh > 4 ? Visibility.Visible : Visibility.Hidden;
                _txtGasPct.Text = string.Format("{0:0}%", _smoothThrottle * 100);
            }

            // 3. Shift Paddles Glow
            bool lbPressed = (_input.Buttons & ViGEmBridge.LEFT_SHOULDER) != 0;
            bool rbPressed = (_input.Buttons & ViGEmBridge.RIGHT_SHOULDER) != 0;
            if (_leftPaddle != null)
            {
                _leftPaddle.Background = lbPressed ? new SolidColorBrush(Color.FromRgb(59, 130, 246)) : new SolidColorBrush(Color.FromRgb(18, 26, 42));
                Canvas.SetLeft(_leftPaddle, lbPressed ? 18 : 22);
            }
            if (_rightPaddle != null)
            {
                _rightPaddle.Background = rbPressed ? new SolidColorBrush(Color.FromRgb(0, 240, 255)) : new SolidColorBrush(Color.FromRgb(18, 26, 42));
                Canvas.SetLeft(_rightPaddle, rbPressed ? 318 : 314);
            }

            // 4. Buttons Glow
            bool otPressed = (_input.Buttons & ViGEmBridge.LEFT_THUMB) != 0;
            bool drsPressed = (_input.Buttons & ViGEmBridge.A) != 0 || _telemetry.Data.DrsActive;
            bool pitPressed = (_input.Buttons & ViGEmBridge.DPAD_DOWN) != 0;
            bool radPressed = (_input.Buttons & ViGEmBridge.Y) != 0;

            if (_btnOt != null) _btnOt.Background = otPressed ? new SolidColorBrush(Color.FromRgb(245, 158, 11)) : new SolidColorBrush(Color.FromRgb(16, 24, 40));
            if (_btnDrs != null) _btnDrs.Background = drsPressed ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) : new SolidColorBrush(Color.FromRgb(16, 24, 40));
            if (_btnPit != null) _btnPit.Background = pitPressed ? new SolidColorBrush(Color.FromRgb(168, 85, 247)) : new SolidColorBrush(Color.FromRgb(16, 24, 40));
            if (_btnRad != null) _btnRad.Background = radPressed ? new SolidColorBrush(Color.FromRgb(6, 182, 212)) : new SolidColorBrush(Color.FromRgb(16, 24, 40));

            // 5. Rev LEDs
            if (_revLeds != null)
            {
                int litCount = 0;
                if (_telemetry.Data.Active)
                {
                    float frac = (float)_telemetry.Data.EngineRPM / Math.Max(1, _telemetry.Data.MaxRPM);
                    litCount = (int)(frac * 15f);
                }
                else if (_smoothThrottle > 0.05f)
                {
                    litCount = (int)(_smoothThrottle * 15f);
                }
                else
                {
                    litCount = (int)((Math.Sin(_animTime * 3.5) + 1.0) * 7.5);
                }

                for (int i = 0; i < 15; i++)
                {
                    Color onCol;
                    if (i < 5) onCol = Color.FromRgb(16, 185, 129);
                    else if (i < 10) onCol = Color.FromRgb(239, 68, 68);
                    else onCol = Color.FromRgb(59, 130, 246);

                    _revLeds[i].Fill = (i < litCount) ? new SolidColorBrush(onCol) : new SolidColorBrush(Color.FromRgb(24, 34, 52));
                }
            }

            // 6. Gear & Speed
            if (_txtGear != null)
            {
                string gearText = _telemetry.Data.Gear == -1 ? "R" : (_telemetry.Data.Gear == 0 ? "N" : _telemetry.Data.Gear.ToString());
                Color gearCol = _telemetry.Data.Gear == -1 ? Color.FromRgb(239, 68, 68) :
                                (_telemetry.Data.Gear == 0 ? Color.FromRgb(16, 185, 129) : Color.FromRgb(240, 246, 255));
                _txtGear.Text = gearText;
                _txtGear.Foreground = new SolidColorBrush(gearCol);
            }

            if (_txtSpeed != null)
            {
                int spd = _telemetry.Data.Active ? _telemetry.Data.SpeedKmh : (int)(_smoothThrottle * 315);
                _txtSpeed.Text = spd.ToString();
            }

            if (_txtDrs != null)
            {
                string drsStatus = _telemetry.Data.DrsActive ? "DRS OPEN" : (_telemetry.Data.DrsAllowed ? "DRS RDY" : "DRS");
                Color drsColor = _telemetry.Data.DrsActive ? Color.FromRgb(16, 185, 129) :
                                 (_telemetry.Data.DrsAllowed ? Color.FromRgb(245, 158, 11) : Color.FromRgb(71, 85, 105));
                _txtDrs.Text = drsStatus;
                _txtDrs.Foreground = new SolidColorBrush(drsColor);
            }

            // 7. Status Banner & Pill
            bool connected = _input.PacketsReceived > 0;
            if (_txtStatus != null && _dotStatus != null)
            {
                _dotStatus.Fill = connected ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) : new SolidColorBrush(Color.FromRgb(245, 158, 11));
                _txtStatus.Text = connected ?
                    string.Format("CONNECTED: ASUS PC COMPANION • {0} INPUTS", _input.PacketsReceived) :
                    "READY • WAITING FOR APEXWHEEL APP (PORT 5005 / 5006)";
                _txtStatus.Foreground = connected ? Brushes.White : new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }

            if (connected && _lblPackets != null)
            {
                _lblPackets.Text = string.Format("Stream: 60Hz Active ({0} inputs) • 0ms Jitter", _input.PacketsReceived);
                _lblPackets.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            }

            if (_telemetry.Data.Active && _lblTelemDetail != null)
            {
                _lblTelemDetail.Text = string.Format(
                    "F1 25 TELEMETRY: ACTIVE\nGear: {0} | Speed: {1} KM/H\nRPM: {2:N0} / {3:N0}\nDRS: {4} | ERS: {5:0}%\nBrake Bias: {6}% Front",
                    _telemetry.Data.Gear == -1 ? "R" : (_telemetry.Data.Gear == 0 ? "N" : _telemetry.Data.Gear.ToString()),
                    _telemetry.Data.SpeedKmh,
                    _telemetry.Data.EngineRPM,
                    _telemetry.Data.MaxRPM,
                    _telemetry.Data.DrsActive ? "OPEN" : (_telemetry.Data.DrsAllowed ? "READY" : "OFF"),
                    _telemetry.Data.ErsPercent,
                    _telemetry.Data.BrakeBias
                );
                _lblTelemDetail.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            }
        }

        private Border CreateCard(string title, double topMargin = 0)
        {
            Border card = new Border
            {
                Margin = new Thickness(0, topMargin, 0, 0),
                Padding = new Thickness(14),
                Background = new SolidColorBrush(Color.FromRgb(11, 16, 28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(24, 36, 62)),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(12)
            };
            return card;
        }

        private Border CreateMethodTab(string title, string sub, bool active)
        {
            Border b = new Border
            {
                Height = 52,
                Margin = new Thickness(active ? 0 : 4, 0, active ? 4 : 0, 0),
                Background = new SolidColorBrush(active ? Color.FromRgb(16, 26, 46) : Color.FromRgb(12, 18, 32)),
                BorderBrush = new SolidColorBrush(active ? Color.FromRgb(0, 240, 255) : Color.FromRgb(24, 36, 60)),
                BorderThickness = new Thickness(active ? 1.5 : 1),
                CornerRadius = new CornerRadius(10),
                Cursor = Cursors.Hand
            };
            StackPanel sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
            TextBlock t = new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
            TextBlock s = new TextBlock { Text = sub, FontSize = 8.5, Foreground = new SolidColorBrush(active ? Color.FromRgb(0, 240, 255) : Color.FromRgb(100, 116, 139)), Margin = new Thickness(0, 2, 0, 0) };
            sp.Children.Add(t);
            sp.Children.Add(s);
            b.Child = sp;
            return b;
        }

        private void SetTabActive(Border b, bool active)
        {
            b.Background = new SolidColorBrush(active ? Color.FromRgb(16, 26, 46) : Color.FromRgb(12, 18, 32));
            b.BorderBrush = new SolidColorBrush(active ? Color.FromRgb(0, 240, 255) : Color.FromRgb(24, 36, 60));
            b.BorderThickness = new Thickness(active ? 1.5 : 1);
        }

        private Button CreateSysButton(string text, RoutedEventHandler handler, bool isClose = false)
        {
            Button btn = new Button
            {
                Content = text,
                Width = 34,
                Height = 28,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(3, 0, 0, 0)
            };
            btn.Click += handler;
            btn.MouseEnter += (s, e) =>
            {
                btn.Background = new SolidColorBrush(isClose ? Color.FromRgb(239, 68, 68) : Color.FromRgb(24, 36, 60));
                btn.Foreground = Brushes.White;
            };
            btn.MouseLeave += (s, e) =>
            {
                btn.Background = Brushes.Transparent;
                btn.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            };
            return btn;
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
    }

    class ColumnStyleAuto : ColumnDefinition
    {
        public ColumnStyleAuto() { Width = GridLength.Auto; }
    }
    class ColumnStyleStar : ColumnDefinition
    {
        public ColumnStyleStar() { Width = new GridLength(1, GridUnitType.Star); }
    }


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
                string fullPath = System.IO.Path.Combine(_webRoot, file);

                if (File.Exists(fullPath))
                {
                    byte[] bytes = File.ReadAllBytes(fullPath);
                    string ext = System.IO.Path.GetExtension(fullPath).ToLowerInvariant();
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
                try { File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unhandled_domain.log"), e.ExceptionObject.ToString()); } catch { }
            };

            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string rootDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, ".."));
                string webRoot = System.IO.Path.Combine(rootDir, "client-web");
                if (!Directory.Exists(webRoot)) webRoot = exeDir;

                MainWindow window = new MainWindow();

                HttpServer http = new HttpServer(window.Telemetry, window.Input, window.ViGEm, webRoot);
                http.Start(8080);

                Application app = new Application();
                app.Run(window);
            }
            catch (Exception ex)
            {
                File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_crash.log"), ex.ToString());
            }
        }
    }
}