@echo off
rem PCL 2.99.0.0 download restore - one click fix
rem Pure ASCII on purpose: cmd.exe parses .bat as GBK, Chinese text here breaks it.
chcp 65001 >nul
setlocal
cd /d "%~dp0"

if not exist "PclDownloadRestore.exe" (
    echo [ERROR] PclDownloadRestore.exe not found next to this script.
    echo         Extract the whole zip archive first, then run this file.
    echo.
    pause
    exit /b 1
)

echo ============================================
echo  PCL 2.99.0.0  -  restore MC download page
echo ============================================
echo.

"PclDownloadRestore.exe" %*

set "RC=%errorlevel%"
echo.
if "%RC%"=="0"  echo Done. You can start PCL now and open Download page.
if "%RC%"=="10" echo Already fixed. Nothing to do.
if "%RC%"=="1"  echo Failed. See message above.
if "%RC%"=="2"  echo PCL not found. Drag PCL exe onto this bat, or pass its path.
if "%RC%"=="3"  echo Unsupported PCL version or file unreadable.
if "%RC%"=="5"  echo Patch verification failed. Original file was NOT modified.
echo.
pause
exit /b %RC%
