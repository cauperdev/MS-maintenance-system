@echo off
rem ==========================================================================
rem  Liga o MS - Gestao de Manutencao SO PARA ESTE COMPUTADOR (http://localhost:5100).
rem  Nao pede permissao de administrador nem do Firewall.
rem  Para abrir tambem no celular, use o "iniciar-na-rede.bat".
rem  - Usa o programa pronto da pasta "programa" (nao precisa instalar nada).
rem  - Sem a pasta "programa", roda pelo codigo-fonte (precisa do .NET 10 SDK).
rem  - Dados: pasta "dados" (ms.db = banco, fotos = fotos das OS).
rem  Deixe esta janela aberta enquanto usa o sistema. Fechar a janela desliga o sistema.
rem ==========================================================================
title MS - Gestao de Manutencao
cd /d "%~dp0"
echo.
echo   MS - Gestao de Manutencao
echo   ---------------------------------------------------------------
echo   Endereco: http://localhost:5100   (so neste computador)
echo   Para abrir no celular, use o "iniciar-na-rede.bat".
echo.
echo   Deixe esta janela aberta. Para desligar o sistema, feche-a.
echo   ---------------------------------------------------------------
echo.
rem abre o navegador depois de alguns segundos, quando o sistema ja ligou
start "" cmd /c "timeout /t 6 /nobreak >nul & start http://localhost:5100"
rem Religa sozinho quando o programa para (ex.: ao publicar uma versao nova: a pasta "programa-novo" troca de lugar
rem com a "programa" e o sistema volta em poucos segundos; basta atualizar a pagina). Fechar a janela desliga de vez.
:ligar
if not exist "programa\MS.exe" if exist "programa-novo\MS.exe" (timeout /t 2 /nobreak >nul & goto ligar)
if exist "programa\MS.exe" (
    "programa\MS.exe" --contentRoot "%~dp0." --urls http://localhost:5100
) else (
    rem Compila em silencio: os avisos do compilador vao para um log, mostrado so se der erro.
    echo   Compilando o sistema - na primeira vez demora um pouco...
    dotnet build -nologo -v q > "%TEMP%\ms-maintenance-system-compilacao.log" 2>&1
    if errorlevel 1 (
        type "%TEMP%\ms-maintenance-system-compilacao.log"
        echo.
        echo   Deu erro ao compilar. Confira se o .NET 10 SDK esta instalado.
    ) else (
        dotnet run --no-build --no-launch-profile --urls http://localhost:5100
    )
)
echo.
echo   O sistema parou. Ligando de novo em 3 segundos... (para desligar de vez, feche esta janela)
echo   Se aparecer erro acima toda vez, veja o LEIA-ME.txt.
timeout /t 3 /nobreak >nul
goto ligar
