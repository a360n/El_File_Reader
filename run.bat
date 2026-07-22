@echo off
title EcoLAB EL File Reader ^& AI Dataset Auditor Launcher
color 0A

echo ========================================================
echo   EcoLAB EL File Reader ^& AI Dataset Auditor Launcher
echo ========================================================
echo.

:: Navigate to EcoLabReaderApp directory
cd /d "%~dp0EcoLabReaderApp"

echo [1/3] Checking for updates from GitHub...
git pull origin main >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo [OK] Successfully updated from GitHub!
) else (
    echo [INFO] Offline mode or no new updates. Proceeding locally...
)
echo.

echo [2/3] Opening browser at http://localhost:5199 ...
timeout /t 2 /nobreak >nul
start "" "http://localhost:5199"

echo [3/3] Starting EcoLAB Reader Application...
echo.
dotnet run

pause
