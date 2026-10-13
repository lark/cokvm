@echo off
REM ============================================================================
REM  CoKVM - build script (command line, unpackaged self-contained)
REM  Builds an .exe you can run directly without MSIX/certificate.
REM
REM  Usage:
REM    build.bat            Debug build (default)
REM    build.bat release    Release build
REM
REM  Requires Visual Studio 2022 with the "Windows application development"
REM  workload (provides MSBuild + Windows App SDK targets).
REM ============================================================================
setlocal enabledelayedexpansion

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=debug"

REM Locate MSBuild from the installed Visual Studio 2022.
set "MSBUILD="
for %%E in (Community Professional Enterprise BuildTools) do (
  if exist "%ProgramFiles%\Microsoft Visual Studio\2022\%%E\MSBuild\Current\Bin\MSBuild.exe" (
    set "MSBUILD=%ProgramFiles%\Microsoft Visual Studio\2022\%%E\MSBuild\Current\Bin\MSBuild.exe"
  )
)
if not defined MSBUILD (
  REM Fall back to vswhere (handles non-standard install locations).
  set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
  if exist "!VSWHERE!" (
    for /f "usebackq delims=" %%i in (`"!VSWHERE!" -latest -find "MSBuild\**\Bin\MSBuild.exe"`) do set "MSBUILD=%%i"
  )
)
if not defined MSBUILD (
  echo [build] MSBuild not found.
  echo [build] Install Visual Studio 2022 with the "Windows application development" workload.
  exit /b 1
)

echo [build] MSBuild: %MSBUILD%
echo [build] Configuration: %CONFIG%

echo [build] Restoring NuGet packages...
"%MSBUILD%" "%~dp0CoKVM.csproj" ^
  -t:Restore ^
  -p:Configuration=%CONFIG% ^
  -p:Platform=x64 ^
  -p:WindowsPackageType=None ^
  -p:WindowsAppSDKSelfContained=true ^
  -p:RuntimeIdentifier=win-x64 ^
  -v:minimal ^
  -nologo
if errorlevel 1 (
  echo [build] Restore FAILED.
  exit /b 1
)

echo [build] Building...
"%MSBUILD%" "%~dp0CoKVM.csproj" ^
  -p:Configuration=%CONFIG% ^
  -p:Platform=x64 ^
  -p:WindowsPackageType=None ^
  -p:WindowsAppSDKSelfContained=true ^
  -p:RuntimeIdentifier=win-x64 ^
  -v:minimal ^
  -nologo

if errorlevel 1 (
  echo [build] FAILED.
  exit /b 1
)

echo.
echo [build] OK. Output:
echo   %~dp0bin\x64\%CONFIG%\net8.0-windows10.0.19041.0\win-x64\CoKVM.exe
echo Run it with: run.bat %CONFIG%
endlocal
