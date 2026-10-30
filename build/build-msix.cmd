@echo off
REM ==========================================================================
REM build-msix.cmd - Gera o pacote da Microsoft Store do ClaudeTray (T409)
REM Faz o build (dotnet publish) e monta o pacote com build-msix.ps1: o manifesto
REM build\msix\AppxManifest.xml com a versao do csproj, os logos desenhados pelo
REM proprio exe (--msix-assets) e o makeappx do Windows SDK.
REM Saida: dist\ClaudeTray.msix (sem assinatura: a Store assina no envio)
REM
REM   build-msix.cmd            gera o pacote
REM   build-msix.cmd -Register  tambem instala a pasta do pacote nesta maquina para
REM                             testar (precisa do Modo de desenvolvedor; feche antes
REM                             a bandeja do winget, que divide o mutex de instancia)
REM ==========================================================================
setlocal

REM Este script vive em build\; tudo o que ele gera (bin\, dist\) fica na pasta acima.
cd /d "%~dp0.."

call "%~dp0build.cmd"
if errorlevel 1 (
    echo *** ERRO: build falhou, pacote nao foi gerado. ***
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-msix.ps1" %*
if errorlevel 1 (
    echo.
    echo *** ERRO: falha ao montar o pacote MSIX. ***
    exit /b 1
)

endlocal
