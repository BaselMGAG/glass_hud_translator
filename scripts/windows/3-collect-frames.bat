@echo off
REM Saves every captured region into captured-frames\ as you play. Run this for twenty
REM minutes of normal story play and the folder fills with exactly the frames the OCR
REM has to handle. Uses the stub translator, so it costs no API quota.
cd /d "%~dp0"
start "" "GlassHudTranslator.exe" --stub --save-frames "captured-frames"
