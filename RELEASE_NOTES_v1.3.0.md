# ApexWheel F1 v1.3.0 — Universal Cross-Platform Release

### 🏎️ Multi-Platform Steering Companion & EA Sports F1 Telemetry Hub
**Supported Platforms:** Android (APK) • Windows (EXE/Setup) • Linux (tar.gz / uinput) • macOS (zip / command)

---

### 🌟 What is New in v1.3.0:

#### 1. Instant Bulletproof Pairing (< 25ms Handshake)
- **Sub-25ms Forward Pairing (TCP 5007)**: Replaced thread pooling with dedicated, high-priority background worker threads and raw stream decoding. Pairing with ASUS PC / any laptop now succeeds instantly upon selection.
- **Reverse TCP Pairing (Port 7507)**: Full native implementation of `PW2 PAIRME` reverse fallback connection. If Wi-Fi router AP isolation delays the forward connection, the PC companion instantly connects back to the phone on TCP 7507, guaranteeing zero "no answer" or "could not reach" errors.
- **Zero Socket Handle Leaks**: Eliminated process handle inheritance so background daemons can never hold or lock the pairing ports.

#### 2. Pure Ad-Free, Copyright-Proof Android APK
- **Zero Ads**: Neutralized and eradicated all Google Ads SDK, User Messaging Platform (UMP) consent dialogs, interstitial gates, and privacy preference menus in bytecode.
- **Direct 1-Tap Entry**: Launch straight into the dashboard without any countdowns, banners, or gate intercepts.
- **Signed & Verified**: V2/V3 APK signatures for fast, safe installation on Android 7.0 through Android 15.

#### 3. Linux Support with Native `/dev/uinput` Xbox 360 Controller
- **Native Gamepad Emulation**: Automatically registers a virtual `Microsoft X-Box 360 pad` device via the Linux kernel `/dev/uinput` interface.
- **Steam & Proton Ready**: Fully plug-and-play with Steam, Proton, Wine, Lutris, and native Linux racing sims (F1 22/23/24/25, Assetto Corsa, Euro Truck Simulator 2).
- **One-Click Launcher**: Run `./ApexWheel-Linux.sh` to configure permissions and launch.

#### 4. macOS Companion Support
- **Universal macOS Package**: Double-click `ApexWheel-macOS.command` to start the companion.
- **Web Cockpit & Terminal HUD**: Live F1 telemetry, rev lights, gear indicator, and WebHID controller support in Safari, Chrome, and Edge.

#### 5. Windows PC Dashboard Companion
- **Direct3D WPF Frameless Cockpit**: 15-LED shift rev lights, live gear, speed, DRS/ERS status, animated steering wheel, and dual shift paddles.
- **ViGEm Direct Bus**: Plug-and-play virtual Xbox 360 controller with zero jitter.

---

### 📦 Release Assets & Downloads:
- **`ApexWheel.apk`** — Android phone app (v1.2.0 build 120, ad-free)
- **`ApexWheel-PC-Setup.exe`** — Windows complete installer with ViGEm drivers
- **`ApexWheelServer.exe`** — Windows standalone portable companion
- **`ApexWheel-Linux.tar.gz`** — Linux complete companion with `/dev/uinput` Xbox 360 support
- **`ApexWheel-macOS.zip`** — macOS companion with one-click Finder launcher
