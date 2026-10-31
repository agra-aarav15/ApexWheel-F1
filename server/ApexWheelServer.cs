using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace ApexWheel
{
    // =========================================================================
    // 1. ViGEmBus Direct Driver Interface (Xbox 360 Virtual Controller)
    // =========================================================================
    public class ViGEmBridge
    {
        private static readonly Guid BusGuid = new Guid("96e42b22-f5e9-42f8-b043-ed0f932f014f");
        private const uint IOCTL_PLUGIN_TARGET = 0x2aa004;
        private const uint IOCTL_UNPLUG_TARGET = 0x2aa008;
        private const uint IOCTL_CHECK_VERSION = 0x2aa00c;
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
                Status = "ViGEmBus driver not found. Run drivers\\ViGEmBus.exe first.";
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
            Status = "ViGEmBus driver connected";
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
                Status = "Failed to plugin virtual Xbox 360 controller.";
                return false;
            }

            _serial = BitConverter.ToInt32(plugReq, 4);
            WaitReady();
            Plugged = true;
            Status = string.Format("Virtual Xbox 360 controller plugged (Serial #{0})", _serial);
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

            // XUSB Submit Packet:
            // [0..3]:   Size = 20
            // [4..7]:   Serial
            // [8..9]:   wButtons (ushort)
            // [10]:     bLeftTrigger (byte 0..255)
            // [11]:     bRightTrigger (byte 0..255)
            // [12..13]: sThumbLX (short)
            // [14..15]: sThumbLY (short)
            // [16..17]: sThumbRX (short)
            // [18..19]: sThumbRY (short)

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
    // 2. F1 25 / F1 24 UDP Telemetry Engine (Port 20777)
    // =========================================================================
    public class F1TelemetryData
    {
        public bool Active = false;
        public int SpeedKmh = 0;
        public int Gear = 0; // -1: R, 0: N, 1..8
        public int EngineRPM = 0;
        public int MaxRPM = 13500;
        public int RevLightsPercent = 0;
        public int RevLightsBitValue = 0;
        public bool DrsAllowed = false;
        public bool DrsActive = false;
        public float ErsPercent = 100f;
        public int ErsMode = 0; // 0=None, 1=Medium, 2=Hotlap, 3=Overtake
        public int EngineTemp = 90;
        public int[] TyreTemps = new int[] { 90, 90, 90, 90 }; // RL, RR, FL, FR
        public int BrakeBias = 56;
        public bool PitLimiter = false;
        public int FiaFlag = 0; // -1: none, 1: green, 2: blue, 3: yellow
        public float CurrentLapTime = 0f;
        public int Position = 1;
        public DateTime LastPacketTime = DateTime.MinValue;

        public string ToJson()
        {
            return string.Format(
                "{{\"active\":{0},\"speed\":{1},\"gear\":{2},\"rpm\":{3},\"maxRpm\":{4},\"revPercent\":{5},\"revBits\":{6},\"drsAllowed\":{7},\"drsActive\":{8},\"ers\":{9:0.0},\"ersMode\":{10},\"engineTemp\":{11},\"tyres\":[{12},{13},{14},{15}],\"brakeBias\":{16},\"pitLimiter\":{17},\"fiaFlag\":{18},\"lapTime\":{19:0.00},\"position\":{20}}}",
                Active ? "true" : "false",
                SpeedKmh,
                Gear,
                EngineRPM,
                MaxRPM,
                RevLightsPercent,
                RevLightsBitValue,
                DrsAllowed ? "true" : "false",
                DrsActive ? "true" : "false",
                ErsPercent,
                ErsMode,
                EngineTemp,
                TyreTemps[0], TyreTemps[1], TyreTemps[2], TyreTemps[3],
                BrakeBias,
                PitLimiter ? "true" : "false",
                FiaFlag,
                CurrentLapTime,
                Position
            );
        }
    }

    public class F1TelemetryListener
    {
        private UdpClient _udp;
        private Thread _thread;
        private bool _running;
        public readonly F1TelemetryData Data = new F1TelemetryData();

        public void Start(int port = 20777)
        {
            try
            {
                _udp = new UdpClient(port);
                _running = true;
                _thread = new Thread(ListenLoop) { IsBackground = true };
                _thread.Start();
                Console.WriteLine("[Telemetry] F1 UDP Telemetry listening on port " + port);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Telemetry] Failed to bind F1 port " + port + ": " + ex.Message);
            }
        }

        public void Stop()
        {
            _running = false;
            if (_udp != null)
            {
                _udp.Close();
                _udp = null;
            }
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

                    // Parse F1 Header
                    // Header structure (2023/2024/2025):
                    // [0..1]  m_packetFormat (uint16) e.g. 2024, 2025
                    // [2]     m_gameYear (uint8)
                    // [3]     m_gameMajorVersion
                    // [4]     m_gameMinorVersion
                    // [5]     m_packetVersion
                    // [6]     m_packetId (uint8)
                    // [7..14] m_sessionUID (uint64)
                    // [15..18] m_sessionTime (float)
                    // [19..22] m_frameIdentifier (uint32)
                    // [23]    m_playerCarIndex (uint8) (in 2023/2024/2025 it's at offset 27 or 23 depending on header size)

                    ushort packetFormat = BitConverter.ToUInt16(packet, 0);
                    int headerSize = (packetFormat >= 2023) ? 29 : 24;
                    if (packet.Length < headerSize) continue;

                    byte packetId = packet[6];
                    byte playerIndex = packet[headerSize - 2]; // Player car index

                    Data.Active = true;
                    Data.LastPacketTime = DateTime.UtcNow;

                    if (packetId == 6) // Car Telemetry Packet
                    {
                        // Each car's telemetry data size is ~60 bytes
                        // Offset: headerSize + playerIndex * 60
                        int carOffset = headerSize + (playerIndex * 60);
                        if (packet.Length >= carOffset + 50)
                        {
                            Data.SpeedKmh = BitConverter.ToUInt16(packet, carOffset);
                            // packet[carOffset + 8]: clutch
                            Data.Gear = (sbyte)packet[carOffset + 9]; // sbyte gear: -1 (R), 0 (N), 1..8
                            Data.EngineRPM = BitConverter.ToUInt16(packet, carOffset + 10);
                            Data.DrsActive = packet[carOffset + 12] == 1;
                            Data.RevLightsPercent = packet[carOffset + 13];
                            Data.RevLightsBitValue = BitConverter.ToUInt16(packet, carOffset + 14);

                            // Brakes temp: [16..23] (4 x uint16)
                            // Tyres surface temp: [24..27] (4 x uint8)
                            Data.TyreTemps[0] = packet[carOffset + 24]; // RL
                            Data.TyreTemps[1] = packet[carOffset + 25]; // RR
                            Data.TyreTemps[2] = packet[carOffset + 26]; // FL
                            Data.TyreTemps[3] = packet[carOffset + 27]; // FR

                            Data.EngineTemp = BitConverter.ToUInt16(packet, carOffset + 32);
                        }
                    }
                    else if (packetId == 7) // Car Status Packet
                    {
                        // Size ~55 bytes per car
                        int carOffset = headerSize + (playerIndex * 55);
                        if (packet.Length >= carOffset + 45)
                        {
                            Data.BrakeBias = packet[carOffset + 3];
                            Data.PitLimiter = packet[carOffset + 4] == 1;
                            Data.MaxRPM = BitConverter.ToUInt16(packet, carOffset + 16);
                            if (Data.MaxRPM == 0) Data.MaxRPM = 13500;
                            Data.DrsAllowed = packet[carOffset + 20] == 1;
                            Data.FiaFlag = (sbyte)packet[carOffset + 24];

                            // ERS: m_ersStoreEnergy is float at offset ~36 (4,000,000 Joules max)
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
                catch
                {
                    // Ignore packet parsing hiccups or network disconnects
                }
            }
        }
    }

    // =========================================================================
    // 3. High-Speed Phone Input Receiver (UDP Port 8081)
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

        // Steering settings
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

        public void Start(int port = 8081)
        {
            try
            {
                _udp = new UdpClient(port);
                _running = true;
                _thread = new Thread(ListenLoop) { IsBackground = true };
                _thread.Start();
                Console.WriteLine("[PhoneReceiver] High-speed Input listener on port " + port);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[PhoneReceiver] Failed to bind port " + port + ": " + ex.Message);
            }
        }

        public void Stop()
        {
            _running = false;
            if (_udp != null)
            {
                _udp.Close();
                _udp = null;
            }
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
                catch
                {
                    // ignore
                }
            }
        }

        public void ProcessMessage(string msg)
        {
            // Message protocol:
            // "APEX:<seq>:<steerAngleDeg>:<throttle 0..1>:<brake 0..1>:<clutch 0..1>:<buttonsMask>"
            // Or simplified: "IN:<steerNorm>:<throttle>:<brake>:<buttons>"

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
                // Format: APEX:seq:angle:gas:brake:clutch:buttons
                double angleDeg = 0.0;
                double.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out angleDeg);
                double.TryParse(parts[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out rawGas);
                double.TryParse(parts[4], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out rawBrake);
                if (parts.Length > 5) double.TryParse(parts[5], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out rawClutch);
                if (parts.Length > 6) ushort.TryParse(parts[6], out btnMask);

                // Convert steering angle in degrees to normalized [-1.0 .. 1.0]
                double halfLock = SteeringLockAngle / 2.0;
                rawSteer = Math.Max(-1.0, Math.Min(1.0, angleDeg / halfLock));
            }
            else if (parts[0] == "IN")
            {
                // Format: IN:steerNorm:gas:brake:buttons
                double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out rawSteer);
                double.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out rawGas);
                double.TryParse(parts[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out rawBrake);
                if (parts.Length > 4) ushort.TryParse(parts[4], out btnMask);
            }

            // Apply deadzone & linearity
            double finalSteer = ApplySteeringCurve(rawSteer);
            double finalGas = Math.Max(0.0, Math.Min(1.0, rawGas));
            double finalBrake = Math.Max(0.0, Math.Min(1.0, rawBrake));

            Steer = finalSteer;
            Throttle = finalGas;
            Brake = finalBrake;
            Clutch = rawClutch;
            Buttons = btnMask;
            PacketsReceived++;
            LastInputTime = DateTime.UtcNow;

            // Feed directly to ViGEm virtual Xbox 360 controller
            _bridge.Feed(Steer, 0.0, 0.0, 0.0, Brake, Throttle, Buttons);
        }

        private double ApplySteeringCurve(double input)
        {
            if (Math.Abs(input) < Deadzone) return 0.0;
            double sign = Math.Sign(input);
            double abs = (Math.Abs(input) - Deadzone) / (1.0 - Deadzone);
            if (abs > 1.0) abs = 1.0;
            // Gamma curve
            double curved = Math.Pow(abs, Linearity);
            return sign * curved;
        }
    }

    // =========================================================================
    // 4. HTTP Server & Web Companion (Port 8080)
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

        public void Start(int port = 8080)
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://*:" + port + "/");
                _listener.Start();
                _running = true;
                _thread = new Thread(ListenLoop) { IsBackground = true };
                _thread.Start();
                Console.WriteLine("[Web] Server running at http://localhost:" + port + "/");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Web] HttpListener error: " + ex.Message + ". Falling back to localhost only.");
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add("http://localhost:" + port + "/");
                    _listener.Start();
                    _running = true;
                    _thread = new Thread(ListenLoop) { IsBackground = true };
                    _thread.Start();
                }
                catch (Exception e2)
                {
                    Console.WriteLine("[Web] Failed to start HTTP server: " + e2.Message);
                }
            }
        }

        public void Stop()
        {
            _running = false;
            if (_listener != null)
            {
                _listener.Close();
                _listener = null;
            }
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
                catch
                {
                    // ignore
                }
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                string rawUrl = ctx.Request.Url.AbsolutePath;

                // API Routes
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
                        "{{\"vigemAvailable\":{0},\"vigemPlugged\":{1},\"vigemStatus\":\"{2}\",\"steer\":{3:0.00},\"throttle\":{4:0.00},\"brake\":{5:0.00},\"packets\":{6},\"f1Active\":{7}}}",
                        _bridge.Available ? "true" : "false",
                        _bridge.Plugged ? "true" : "false",
                        _bridge.Status,
                        _input.Steer,
                        _input.Throttle,
                        _input.Brake,
                        _input.PacketsReceived,
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

                // Serve static files from web root
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
                    else if (ext == ".json") mime = "application/json";
                    else if (ext == ".svg") mime = "image/svg+xml";
                    else if (ext == ".png") mime = "image/png";
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
            catch
            {
                try { ctx.Response.Close(); } catch { }
            }
        }
    }

    // =========================================================================
    // 5. USB ADB Reverse Port Forwarding Helper
    // =========================================================================
    public static class AdbHelper
    {
        public static bool CheckAndForwardPorts()
        {
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "adb.exe";
                p.StartInfo.Arguments = "devices";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                bool hasDevice = false;
                for (int i = 1; i < lines.Length; i++)
                {
                    if (lines[i].Contains("device"))
                    {
                        hasDevice = true;
                        break;
                    }
                }

                if (hasDevice)
                {
                    // Forward HTTP and UDP ports to phone over USB cable
                    RunAdb("reverse tcp:8080 tcp:8080");
                    RunAdb("reverse udp:8081 udp:8081");
                    Console.WriteLine("[USB] ADB Device connected! Forwarded ports 8080 & 8081 for 0ms USB play.");
                    return true;
                }
            }
            catch
            {
                // ADB not installed or not in path
            }
            return false;
        }

        private static void RunAdb(string args)
        {
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "adb.exe";
                p.StartInfo.Arguments = args;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                p.WaitForExit();
            }
            catch { }
        }
    }

    // =========================================================================
    // 6. Main Program & Real-Time Console Dashboard
    // =========================================================================
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "ApexWheel F1 - PC Companion Server";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
    =================================================================
       🏎️  ApexWheel F1 - Phone Steering Wheel Companion Server
       High-Speed Gyro Controller & F1 25 UDP Telemetry Bridge
    =================================================================
");
            Console.ResetColor();

            // Locate base directory
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string rootDir = Path.GetFullPath(Path.Combine(exeDir, ".."));
            string webRoot = Path.Combine(rootDir, "client-web");
            if (!Directory.Exists(webRoot))
            {
                webRoot = Path.Combine(exeDir, "client-web");
                if (!Directory.Exists(webRoot)) webRoot = exeDir;
            }

            // 1. Initialize ViGEm virtual controller
            ViGEmBridge bridge = new ViGEmBridge();
            if (!bridge.Open())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[Driver] ViGEmBus driver not installed on this PC.");
                Console.WriteLine("[Driver] Please run: drivers\\ViGEmBus.exe to enable virtual Xbox 360 controller.");
                Console.ResetColor();
            }
            else
            {
                if (bridge.Plug())
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[Driver] " + bridge.Status);
                    Console.ResetColor();
                }
            }

            // 2. Start F1 25 Telemetry Listener
            F1TelemetryListener telemetry = new F1TelemetryListener();
            telemetry.Start(20777);

            // 3. Start Phone Input Receiver
            PhoneInputReceiver phoneReceiver = new PhoneInputReceiver(bridge);
            phoneReceiver.Start(8081);

            // 4. Start Web Companion Server
            HttpServer server = new HttpServer(telemetry, phoneReceiver, bridge, webRoot);
            server.Start(8080);

            // 5. Check USB Tethering
            AdbHelper.CheckAndForwardPorts();

            // Find local IP address
            string localIp = "127.0.0.1";
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    if (endPoint != null) localIp = endPoint.Address.ToString();
                }
            }
            catch { }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("\n[Ready] Connect your phone:");
            Console.WriteLine("   -> Wi-Fi:  Open http://" + localIp + ":8080 on your phone browser / ApexWheel APK");
            Console.WriteLine("   -> USB:    Connect via USB cable and open http://localhost:8080");
            Console.WriteLine("\nPress Ctrl+C to stop.\n");
            Console.ResetColor();

            // Live Dashboard Loop
            while (true)
            {
                Thread.Sleep(200);

                string steerMeter = RenderBar(phoneReceiver.Steer, -1.0, 1.0, 20);
                string gasMeter = RenderBar(phoneReceiver.Throttle, 0.0, 1.0, 10);
                string brakeMeter = RenderBar(phoneReceiver.Brake, 0.0, 1.0, 10);

                string f1Status = telemetry.Data.Active 
                    ? string.Format("F1 Active | Gear: {0} | {1} KM/H | {2} RPM | DRS: {3}", 
                        telemetry.Data.Gear, telemetry.Data.SpeedKmh, telemetry.Data.EngineRPM, telemetry.Data.DrsActive ? "ON" : "OFF")
                    : "F1 25 Waiting (Port 20777)";

                string line = string.Format("\r[Steer {0}] [Gas {1}] [Brake {2}] | {3}",
                    steerMeter, gasMeter, brakeMeter, f1Status);

                Console.Write(line.PadRight(Console.WindowWidth - 1));
            }
        }

        private static string RenderBar(double val, double min, double max, int width)
        {
            double norm = (val - min) / (max - min);
            norm = Math.Max(0.0, Math.Min(1.0, norm));
            int filled = (int)Math.Round(norm * width);
            return new string('#', filled) + new string('-', width - filled);
        }
    }
}
