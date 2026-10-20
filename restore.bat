@echo off
rem Restore the official PCL binary from the automatic backup.
chcp 65001 >nul
setlocal
cd /d "%~dp0"

if not exist "PclDownloadRestore.exe" (
    echo [ERROR] PclDownloadRestore.exe not found next to this script.
    pause
    exit /b 1
)

echo Restoring official PCL binary from backup...
echo.
"PclDownloadRestore.exe" --restore %*
echo.
pause
exit /b %errorlevel%
