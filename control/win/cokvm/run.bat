@echo off
REM ============================================================================
REM  CoKVM - run script (launches the unpackaged self-contained build)
REM
REM  Usage:
REM    run.bat             Run the Debug build (builds first if missing)
REM    run.bat release     Run the Release build
REM    run.bat debug       Run the Debug build
REM
REM  The app needs a USB video capture device (MS2130/MS2130S) and the
REM  CoKVM BLE dongle to be useful, but it launches without them.
REM ============================================================================
setlocal

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=debug"

set "EXE=%~dp0bin\x64\%CONFIG%\net8.0-windows10.0.19041.0\win-x64\CoKVM.exe"

if not exist "%EXE%" (
  echo [run] Build not found at:
  echo   %EXE%
  echo [run] Building %CONFIG% first...
  call "%~dp0build.bat" %CONFIG%
  if errorlevel 1 exit /b 1
)

echo [run] Launching: %EXE%
start "" "%EXE%"
endlocal
