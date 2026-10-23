@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Instalar-CaixaLume.ps1" %*
if errorlevel 1 (
  echo.
  echo A instalacao nao foi concluida. Leia a mensagem acima.
)
pause
