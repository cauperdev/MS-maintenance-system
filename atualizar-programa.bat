@echo off
rem ==========================================================================
rem  Recompila a pasta "programa" depois de alterar o codigo-fonte.
rem  Precisa do .NET 10 SDK instalado (https://dotnet.microsoft.com/download).
rem  Feche o sistema (janela do iniciar.bat) antes de rodar.
rem ==========================================================================
title MS - Atualizar programa
cd /d "%~dp0"
echo Recompilando o programa... (pode levar alguns minutos)
dotnet publish MS.csproj -c Release -r win-x64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -o programa
if errorlevel 1 (
    echo.
    echo   Deu erro ao recompilar. Confira se o .NET 10 SDK esta instalado e se o sistema esta fechado.
) else (
    echo.
    echo   Pronto! Use o iniciar.bat para ligar o sistema com as alteracoes.
)
pause
