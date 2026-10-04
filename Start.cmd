@echo off
cd /d "%~dp0"
if exist "artifacts\SyncPlayer-1.0.0-win-x64\SyncPlayer.exe" (
  start "" "artifacts\SyncPlayer-1.0.0-win-x64\SyncPlayer.exe"
) else (
  start "" "bin\Release\net10.0-windows\SyncPlayer.exe"
)
