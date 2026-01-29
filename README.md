# AntiCatLock

Lock keyboard & mouse input while you watch movies — so your cat can sleep on the keyboard without chaos.

**AntiCatLock** is a tiny Windows tray app that blocks keyboard input and mouse clicks (cursor movement still works). Toggle with **Ctrl+Alt+U** or from the tray menu.

## Why it exists
Cats love keyboards. One accidental paw = pause, skip, close, or delete. AntiCatLock keeps your screen safe during movie night.

## Features
- One‑tap lock/unlock: **Ctrl+Alt+U**
- Blocks **all keyboard input**
- Blocks **mouse clicks + wheel**, keeps **cursor movement**
- Runs in the **system tray**
- Watchdog process for reliable unlock
- Single EXE, no installer required

## Screenshots
Add your screenshots here.

## Quick start
1. Run `AntiCatLock.exe`
2. Press **Ctrl+Alt+U** to lock
3. Press **Ctrl+Alt+U** again to unlock

## Tray menu
- **Lock/Unlock**
- **Show window**
- **Exit**

## Build (Windows)
```powershell
dotnet build -c Release
```

## Publish single EXE (Windows)
```powershell
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o .\publish
```

## Keywords
keyboard lock, mouse lock, input blocker, cat proof, movie mode, kids lock, anti cat, tray app, windows utility, productivity, focus, anti‑mistap

## License
MIT (add your license file if you want to keep it explicit)
