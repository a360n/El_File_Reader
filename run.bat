@echo off
title "EcoLAB EL File Reader & AI Dataset Auditor Launcher"
color 0A

echo ========================================================
echo   EcoLAB EL File Reader ^& AI Dataset Auditor Launcher
echo ========================================================
echo.

:: Navigate to root directory
cd /d "%~dp0"

echo [0/3] Terminating any previously running instances to unlock files...
taskkill /F /IM EcoLabReaderApp.exe >nul 2>&1

echo [1/3] Checking for updates from GitHub...
git fetch origin main >nul 2>&1
git reset --hard origin/main >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo [OK] Successfully updated to the latest version from GitHub!
) else (
    echo [INFO] Offline mode or local environment. Proceeding...
)
echo.

:: Navigate to EcoLabReaderApp directory
cd /d "%~dp0EcoLabReaderApp"

echo [2/3] Opening browser at http://localhost:5199 ...
timeout /t 2 /nobreak >nul
start "" "http://localhost:5199"

echo [3/3] Starting EcoLAB Reader Application on http://localhost:5199 ...
echo.
dotnet run --urls "http://localhost:5199"

pause
