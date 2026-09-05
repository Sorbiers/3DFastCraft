@echo off
setlocal
cd /d "%~dp0"

rem Prefer the standalone exe from build.bat.
if exist "build\3DFastCraft.exe" (
    start "" "build\3DFastCraft.exe"
    exit /b 0
)

rem Otherwise run straight from source, which needs the .NET SDK.
where dotnet >nul 2>&1
if errorlevel 1 (
    echo.
    echo No built exe found and the .NET SDK is not on PATH.
    echo Either run build.bat on a machine with the SDK, or install the .NET 8 SDK
    echo from https://dotnet.microsoft.com/download
    echo.
    exit /b 1
)

echo No standalone exe yet - run build.bat to make one.
echo Starting from source instead...
echo.
dotnet run --project src\FastCraft3D -c Release --nologo

if errorlevel 1 (
    echo.
    echo Failed to start.
    exit /b 1
)
