# ⚡ USB Cable Tethering Guide (Zero-Latency Mode)

While ApexWheel works wirelessly over your local Wi-Fi, using a **USB cable** gives you the ultimate **0ms latency, zero-jitter** esports competitive experience.

---

## 🚀 Setup Instructions

### 1. Enable USB Debugging on Your Android Phone
1. Open **Settings** on your Android phone.
2. Go to **About Phone** and tap **Build Number** 7 times to enable Developer Mode.
3. Go back to **Settings** $\rightarrow$ **Developer Options** and turn ON **USB Debugging**.

### 2. Connect Your Phone
1. Connect your phone to your PC/laptop with a USB cable.
2. If prompted on your phone screen with *"Allow USB debugging?"*, check *"Always allow from this computer"* and tap **Allow**.

### 3. Launch ApexWheel Companion
1. Run `start_server.bat` on your PC.
2. The server will detect your USB-connected device and automatically run:
   ```cmd
   adb reverse tcp:8080 tcp:8080
   adb reverse udp:8081 udp:8081
   ```
3. Open the **ApexWheel APK** on your phone (or browse to `http://localhost:8080` in Chrome).
4. You are now playing over USB with absolute zero latency!
