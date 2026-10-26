@echo off
rem ==========================================================================
rem  Liga o MS - Gestao de Manutencao PARA A REDE: abre neste computador e tambem no
rem  celular / outros computadores da mesma rede Wi-Fi (http://IP-DESTE-COMPUTADOR:5100).
rem  Na primeira vez o Firewall do Windows pergunta se pode liberar a rede: isso pede
rem  permissao de administrador UMA vez. Sem essa permissao, use o "iniciar.bat"
rem  (funciona so neste computador, sem pedir nada).
rem ==========================================================================
title MS - Gestao de Manutencao (rede)
cd /d "%~dp0"
echo.
echo   MS - Gestao de Manutencao  (aberto para a rede)
echo   ---------------------------------------------------------------
echo   Neste computador: http://localhost:5100
echo   No celular (mesma rede Wi-Fi): https://IP-DESTE-COMPUTADOR:5463
echo   (com "Https" ligado no appsettings.json; o http da rede leva sozinho para o https)
echo   Enderecos IPv4 deste computador:
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /c:"IPv4"') do for /f "tokens=*" %%b in ("%%a") do echo      https://%%b:5463
echo.
echo   Deixe esta janela aberta. Para desligar o sistema, feche-a.
echo   ---------------------------------------------------------------
echo.
start "" cmd /c "timeout /t 6 /nobreak >nul & start http://localhost:5100"
rem Religa sozinho quando o programa para (ex.: ao publicar uma versao nova: a pasta "programa-novo" troca de lugar
rem com a "programa" e o sistema volta em poucos segundos; basta atualizar a pagina). Fechar a janela desliga de vez.
:ligar
if not exist "programa\MS.exe" if exist "programa-novo\MS.exe" (timeout /t 2 /nobreak >nul & goto ligar)
if exist "programa\MS.exe" (
    "programa\MS.exe" --contentRoot "%~dp0." --urls http://0.0.0.0:5100
) else (
    rem Compila em silencio: os avisos do compilador vao para um log, mostrado so se der erro.
    echo   Compilando o sistema - na primeira vez demora um pouco...
    dotnet build -nologo -v q > "%TEMP%\ms-maintenance-system-compilacao.log" 2>&1
    if errorlevel 1 (
        type "%TEMP%\ms-maintenance-system-compilacao.log"
        echo.
        echo   Deu erro ao compilar. Confira se o .NET 10 SDK esta instalado.
    ) else (
        dotnet run --no-build --no-launch-profile --urls http://0.0.0.0:5100
    )
)
echo.
echo   O sistema parou. Ligando de novo em 3 segundos... (para desligar de vez, feche esta janela)
echo   Se aparecer erro acima toda vez, veja o LEIA-ME.txt.
timeout /t 3 /nobreak >nul
goto ligar
