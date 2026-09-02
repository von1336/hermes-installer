@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo ========================================
echo  Hermes installer (NATIVE Windows)
echo  VERSION banner must show:
echo    2026-08-30-pro-v9
echo  Folder: %~dp0
echo ========================================
echo.

if not exist "%~dp0install-hermes.ps1" (
  echo ERROR: install-hermes.ps1 missing in this folder.
  pause
  exit /b 1
)

findstr /C:"2026-08-30-pro-v9" "%~dp0install-hermes.ps1" >nul
if errorlevel 1 (
  echo ERROR: This is an OLD install-hermes.ps1
  echo Delete Telegram copies and use D:\apk\installer\ or HermesWorkspaceSetup.exe
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-hermes.ps1"
set ERR=%ERRORLEVEL%
echo.
if not "%ERR%"=="0" (
  echo Installer failed with code %ERR%.
  echo Log: %LOCALAPPDATA%\hermes\install.log
) else (
  echo Done.
)
pause
exit /b %ERR%
