@echo off
setlocal
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\submit-localization.ps1" %*
set "submit_result=%ERRORLEVEL%"
if not "%submit_result%"=="0" echo Submission failed. See the message above.
pause
exit /b %submit_result%
