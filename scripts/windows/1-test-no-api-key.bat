@echo off
REM Step 1: proves capture, hotkeys and the overlay work, WITHOUT needing an API key
REM or a network connection. Translations will be obvious placeholder Arabic - that is
REM correct. If this step works, every Windows-specific problem is ruled out.
cd /d "%~dp0"
start "" "GamingTranslatorGlassHUD.exe" --stub
