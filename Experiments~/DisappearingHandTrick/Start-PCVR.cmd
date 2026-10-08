@echo off
cd /d "%~dp0"
if not exist "Builds\Windows\DisappearingHandTrick.exe" (
  echo Build missing. Run Tools\Build.ps1 with Unity 6000.5.9f1 installed.
  pause
  exit /b 1
)
start "" "Builds\Windows\DisappearingHandTrick.exe"
