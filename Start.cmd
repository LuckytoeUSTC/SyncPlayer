@echo off
cd /d "%~dp0"
for /f "delims=" %%D in ('dir /b /ad /o-d "artifacts\SyncPlayer-*-win-x64" 2^>nul') do (
  if exist "artifacts\%%D\SyncPlayer.exe" (
    start "" "artifacts\%%D\SyncPlayer.exe"
    exit /b
  )
)
start "" "bin\Release\net10.0-windows\SyncPlayer.exe"
