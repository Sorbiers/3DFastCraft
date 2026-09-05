@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo.
    echo The .NET SDK was not found on PATH.
    echo Install the .NET 8 SDK from https://dotnet.microsoft.com/download and try again.
    echo.
    exit /b 1
)

rem A running instance holds a lock on the exe. Without this check the publish fails with a
rem MSBuild stack trace that says nothing about the actual cause.
tasklist /FI "IMAGENAME eq 3DFastCraft.exe" 2>nul | find /I "3DFastCraft.exe" >nul
if not errorlevel 1 (
    echo.
    echo 3DFastCraft is currently running.
    echo Close it first - the build cannot replace an exe that is in use.
    echo.
    exit /b 1
)

echo Building 3DFastCraft...
echo.

rem Self-contained single file, so the result runs on any 64-bit Windows PC with no .NET install.
rem PublishTrimmed is deliberately NOT used: WPF resolves XAML types reflectively and trimming
rem breaks the app at runtime.
dotnet publish src\FastCraft3D -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "build" --nologo

if errorlevel 1 (
    echo.
    echo BUILD FAILED.
    exit /b 1
)

echo.
echo Built: "%CD%\build\3DFastCraft.exe"
echo Runs on any 64-bit Windows PC - no .NET install needed.
echo Start it with run.bat, or just double-click the exe.
echo.
