#!/bin/bash
# =============================================================================
# ApexWheel F1 — Linux Launch & Setup Script
# =============================================================================
set -e

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
cd "$DIR"

echo "🏎️  Starting ApexWheel F1 for Linux..."

# 1. Check Python 3
if ! command -v python3 &> /dev/null; then
    echo "❌ Error: Python 3 is not installed. Please install it with:"
    echo "   sudo apt install python3 (Ubuntu/Debian)"
    echo "   sudo dnf install python3 (Fedora)"
    echo "   sudo pacman -S python (Arch Linux)"
    exit 1
fi

# 2. Check /dev/uinput permissions for Virtual Xbox 360 Gamepad
if [ -e /dev/uinput ]; then
    if [ ! -w /dev/uinput ]; then
        echo "⚠️  /dev/uinput needs read/write permissions for virtual controller support."
        echo "   Setting permissions now (may prompt for sudo password)..."
        sudo chmod 666 /dev/uinput || true
        # Install permanent udev rule if not present
        if [ ! -f /etc/udev/rules.d/99-uinput-apexwheel.rules ]; then
            echo 'KERNEL=="uinput", MODE="0666"' | sudo tee /etc/udev/rules.d/99-uinput-apexwheel.rules > /dev/null 2>&1 || true
            sudo udevadm control --reload-rules && sudo udevadm trigger || true
            echo "✅ Permanent udev rule installed for ApexWheel controller."
        fi
    fi
else
    echo "ℹ️  Loading uinput kernel module..."
    sudo modprobe uinput || true
    sudo chmod 666 /dev/uinput 2>/dev/null || true
fi

# 3. Launch Universal Companion
exec python3 server/apexwheel_companion.py
