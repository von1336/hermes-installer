@echo off
chcp 65001 >nul
cd /d "%~dp0"

set "EXPECTED="
if exist "%~dp0version.txt" set /p EXPECTED=<"%~dp0version.txt"

echo ========================================
echo  Hermes installer (NATIVE Windows)
if defined EXPECTED echo  Expected version: %EXPECTED%
echo ========================================
echo.

if not exist "%~dp0install-hermes.ps1" (
  echo ERROR: install-hermes.ps1 missing in this folder.
  pause
  exit /b 1
)

if defined EXPECTED (
  findstr /C:"%EXPECTED%" "%~dp0install-hermes.ps1" >nul
  if errorlevel 1 (
    echo ERROR: install-hermes.ps1 version marker does not match version.txt (%EXPECTED%^).
    echo Re-download the full installer bundle (HermesWorkspaceSetup.exe recommended^).
    pause
    exit /b 1
  )
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
