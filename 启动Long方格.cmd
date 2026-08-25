@echo off
setlocal

set "LONGGRID_ROOT=%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%LONGGRID_ROOT%eng\Start-LongGrid.ps1" %*
set "LONGGRID_EXIT_CODE=%ERRORLEVEL%"

if not "%LONGGRID_EXIT_CODE%"=="0" (
    echo.
    echo Long Grid failed to start. Exit code: %LONGGRID_EXIT_CODE%
    echo Review the error above, then press any key to close this window.
    pause >nul
)

exit /b %LONGGRID_EXIT_CODE%
