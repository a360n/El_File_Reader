#!/bin/bash

echo "========================================================"
echo "  EcoLAB EL File Reader & AI Dataset Auditor Launcher"
echo "========================================================"
echo ""

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
cd "$SCRIPT_DIR/EcoLabReaderApp"

echo "[1/3] Checking for updates from GitHub..."
if git pull origin main 2>/dev/null; then
    echo "[OK] Successfully updated from GitHub!"
else
    echo "[INFO] Offline mode or no new updates. Proceeding locally..."
fi
echo ""

echo "[2/3] Opening browser at http://localhost:5199 ..."
(sleep 2 && (open "http://localhost:5199" 2>/dev/null || xdg-open "http://localhost:5199" 2>/dev/null)) &

echo "[3/3] Starting EcoLAB Reader Application..."
echo ""
dotnet run
