@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-TungMEP2.ps1"
if errorlevel 1 (
  echo.
  echo Go cai dat that bai. Xem loi ben tren.
) else (
  echo.
  echo Go cai dat thanh cong.
)
pause

