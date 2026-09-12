@echo off
rem Start the DeepSeek Harness launcher without installing a desktop shortcut.
setlocal
set "HERE=%~dp0"
if not exist "%HERE%dist\DeepSeekHarnessLauncher.exe" (
  echo Launcher executable not found. Building it first...
  powershell -NoProfile -ExecutionPolicy Bypass -File "%HERE%build.ps1"
  if errorlevel 1 (
    echo Build failed.
    pause
    exit /b 1
  )
)
start "" "%HERE%dist\DeepSeekHarnessLauncher.exe"
endlocal
