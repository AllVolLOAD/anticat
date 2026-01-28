@echo off
setlocal
set "SRC=%~dp0"
set "DEST=%ProgramFiles%\AntiCatLock"
if not exist "%DEST%" mkdir "%DEST%" 2>nul
if not exist "%DEST%" (
  set "DEST=%LOCALAPPDATA%\AntiCatLock"
  if not exist "%DEST%" mkdir "%DEST%"
)

xcopy /E /I /Y "%SRC%*" "%DEST%\" >nul

powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%APPDATA%\\Microsoft\\Windows\\Start Menu\\Programs\\AntiCatLock.lnk'); $s.TargetPath='%DEST%\\AntiCatLock.exe'; $s.WorkingDirectory='%DEST%'; $s.Save();"

echo Installed to %DEST%
endlocal
