@echo off
rem One Big Package - production showcase launcher (desktop-shortcut target).
rem Opens the gamey title screen, then Game Sources, maximised.
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0launch-obp.ps1" %*
