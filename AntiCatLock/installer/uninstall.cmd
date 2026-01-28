@echo off
setlocal
set "DEST=%ProgramFiles%\AntiCatLock"
if not exist "%DEST%" set "DEST=%LOCALAPPDATA%\AntiCatLock"

taskkill /F /IM AntiCatLock.exe >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -Command "Remove-Item -Force '%APPDATA%\\Microsoft\\Windows\\Start Menu\\Programs\\AntiCatLock.lnk' -ErrorAction SilentlyContinue"
if exist "%DEST%" rmdir /S /Q "%DEST%"

echo Uninstalled
endlocal
