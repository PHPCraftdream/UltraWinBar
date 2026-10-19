@echo off
rem Rebuilds UltraWinBar and reinstalls it for the current user; see Reinstall-UltraWinBar.ps1 for options.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Reinstall-UltraWinBar.ps1" %*
exit /b %ERRORLEVEL%
