@echo off
REM ============================================================================
REM  CoKVM - clean build artifacts
REM  Usage: clean.bat
REM ============================================================================
setlocal
rmdir /s /q "%~dp0bin" 2>nul
rmdir /s /q "%~dp0obj" 2>nul
echo [clean] Removed bin\ and obj\
endlocal
