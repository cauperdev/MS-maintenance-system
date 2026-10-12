@echo off
rem ==========================================================================
rem  Liga o MS - Gestao de Manutencao PARA A REDE: abre neste computador e tambem no
rem  celular / outros computadores da mesma rede Wi-Fi (http://IP-DESTE-COMPUTADOR:5090).
rem  Na primeira vez o Firewall do Windows pergunta se pode liberar a rede: isso pede
rem  permissao de administrador UMA vez. Sem essa permissao, use o "iniciar.bat"
rem  (funciona so neste computador, sem pedir nada).
rem ==========================================================================
title MS - Gestao de Manutencao (rede)
cd /d "%~dp0"
echo.
echo   MS - Gestao de Manutencao  (aberto para a rede)
echo   ---------------------------------------------------------------
echo   Neste computador: http://localhost:5090
echo   No celular (mesma rede Wi-Fi): https://IP-DESTE-COMPUTADOR:5453
echo   (com "Https" ligado no appsettings.json; o http da rede leva sozinho para o https)
echo   Enderecos IPv4 deste computador:
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /c:"IPv4"') do for /f "tokens=*" %%b in ("%%a") do echo      https://%%b:5453
echo.
echo   Deixe esta janela aberta. Para desligar o sistema, feche-a.
echo   ---------------------------------------------------------------
echo.
start "" cmd /c "timeout /t 6 /nobreak >nul & start http://localhost:5090"
if exist "programa\MS.exe" (
    "programa\MS.exe" --contentRoot "%~dp0." --urls http://0.0.0.0:5090
) else (
    dotnet run --urls http://0.0.0.0:5090
)
echo.
echo   O sistema parou. Se apareceu erro acima, veja o LEIA-ME.txt.
pause
