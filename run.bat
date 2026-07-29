@echo off
title "EcoLAB EL File Reader & AI Dataset Auditor Launcher"
color 0A

echo ========================================================
echo   EcoLAB EL File Reader ^& AI Dataset Auditor Launcher
echo ========================================================
echo.

:: Force working directory to script directory
cd /d "%~dp0"

echo [0/3] Terminating any previously running instances to unlock files...
taskkill /F /IM EcoLabReaderApp.exe >nul 2>&1
taskkill /F /IM dotnet.exe >nul 2>&1

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

:: Clean locked obj cache file if present
if exist "obj\Debug\net8.0\rpswa.dswa.cache.json" (
    del /f /q /a "obj\Debug\net8.0\rpswa.dswa.cache.json" >nul 2>&1
)

echo [2/3] Opening browser at http://localhost:5199 ...
timeout /t 2 /nobreak >nul
start "" "http://localhost:5199"

echo [3/3] Starting EcoLAB Reader Application on http://localhost:5199 ...
echo.
dotnet run --urls "http://localhost:5199"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [FIX] Cleaning temporary build files and retrying...
    dotnet clean >nul 2>&1
    if exist obj rd /s /q obj >nul 2>&1
    if exist bin rd /s /q bin >nul 2>&1
    echo [RETRY] Starting application...
    dotnet run --urls "http://localhost:5199"
)

pause
