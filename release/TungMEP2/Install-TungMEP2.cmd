@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-TungMEP2.ps1"
if errorlevel 1 (
  echo.
  echo Cai dat that bai. Xem loi ben tren.
) else (
  echo.
  echo Cai dat thanh cong.
)
pause

