@echo off
rem ==========================================================================
rem  Liga o MS - Gestao de Manutencao SO PARA ESTE COMPUTADOR (http://localhost:5090).
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
echo   Endereco: http://localhost:5090   (so neste computador)
echo   Para abrir no celular, use o "iniciar-na-rede.bat".
echo.
echo   Deixe esta janela aberta. Para desligar o sistema, feche-a.
echo   ---------------------------------------------------------------
echo.
rem abre o navegador depois de alguns segundos, quando o sistema ja ligou
start "" cmd /c "timeout /t 6 /nobreak >nul & start http://localhost:5090"
if exist "programa\MS.exe" (
    "programa\MS.exe" --contentRoot "%~dp0." --urls http://localhost:5090
) else (
    dotnet run --urls http://localhost:5090
)
echo.
echo   O sistema parou. Se apareceu erro acima, veja o LEIA-ME.txt.
pause
