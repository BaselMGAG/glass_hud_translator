@echo off
REM Kills the app if the overlay is ever stuck on screen.
REM
REM The overlay has no Alt-Tab entry, cannot be focused, and clicks pass straight
REM through it, so there is no window to close - the process has to be ended.
REM You should not normally need this; it exists because an early build did not
REM exit when the Settings window was closed.
echo Stopping GamingTranslatorGlassHUD...
taskkill /IM GamingTranslatorGlassHUD.exe /F >nul 2>&1
if %ERRORLEVEL%==0 (echo Stopped.) else (echo It was not running.)
timeout /t 2 >nul
