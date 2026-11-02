#!/bin/bash
# =============================================================================
# ApexWheel F1 — macOS Launch Script (Double-Clickable in Finder)
# =============================================================================
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
cd "$DIR"

echo "🏎️  Starting ApexWheel F1 Companion for macOS..."

# Check Python 3
if ! command -v python3 &> /dev/null; then
    echo "❌ Error: Python 3 was not found."
    echo "   Please install Python 3 via https://python.org or 'brew install python3'."
    read -p "Press Enter to exit..."
    exit 1
fi

echo "✅ Python 3 detected. Launching ApexWheel Companion..."
exec python3 server/apexwheel_companion.py
