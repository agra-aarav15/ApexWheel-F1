# 🏎️ EA Sports F1 25 / F1 24 Telemetry Setup Guide

ApexWheel F1 features a real-time bi-directional telemetry engine that listens to official UDP telemetry broadcasted by **EA Sports F1 25, F1 24, F1 23, and F1 22**.

---

## 🛠️ In-Game Configuration Steps

Follow these exact steps inside the F1 game to activate real-time telemetry on your phone:

1. Launch **F1 25** (or F1 24 / F1 23) on your PC.
2. In the Main Menu or Pause Menu, navigate to:
   **Game Options** $\rightarrow$ **Settings** $\rightarrow$ **Telemetry Settings**
3. Configure the following fields:
   - **UDP Telemetry**: `On`
   - **UDP Broadcast**: `Off` (or `On` if using across network)
   - **UDP IP Address**: `127.0.0.1` (or your laptop's local Wi-Fi IP)
   - **UDP Port**: `20777`
   - **UDP Send Rate**: `60Hz` (recommended for silky smooth shift lights)
   - **UDP Format**: `2025` (or `2024` / `2023` matching your game version)

---

## ⚡ What Telemetry Data Is Displayed On Your Phone?

Once enabled, your phone dashboard will display live telemetry from the game:
- **15 Shift LEDs**: 5 Green $\rightarrow$ 5 Red $\rightarrow$ 5 Blue flashing shift points matching the actual car's steering wheel!
- **Large Gear Readout**: Real-time gear display (`N`, `R`, `1` through `8`).
- **Digital Speedometer**: Real-time vehicle speed in KM/H.
- **Engine RPM Gauge**: Dynamic RPM bar and exact rev counter.
- **DRS Status**:
  - `Dim`: Outside DRS zone
  - `Yellow Glowing`: DRS Available / Detected
  - `Bright Green`: DRS Wing Open
- **ERS Battery Gauge**: Real-time energy store percentage (0–100%) and deployment mode (`OVERTAKE`, `HOTLAP`, `BALANCED`).
- **Tire Temperatures**: 4-corner heat indicators (Front-Left, Front-Right, Rear-Left, Rear-Right) with color-coded heat zones (optimal green $\sim$90–100°C, overheated red $>105$°C).
- **Pit Limiter**: Visual confirmation when pit lane speed limiter is engaged.
- **FIA Track Flags**: Live Green, Yellow (Safety Car / Hazard), or Blue flag alerts.
