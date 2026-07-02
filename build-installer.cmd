@echo off
REM ==========================================================================
REM build-installer.cmd - Gera o instalador do ClaudeTray com Inno Setup
REM Faz o build (dotnet publish), compila installer.iss e por fim regenera os
REM manifestos winget (update-winget.ps1: versao, SHA256 e ReleaseDate).
REM Saida: dist\ClaudeTray-Setup.exe
REM ==========================================================================
setlocal

cd /d "%~dp0"

REM --- 1) Build / publish -------------------------------------------------
call "%~dp0build.cmd"
if errorlevel 1 (
    echo *** ERRO: build falhou, instalador nao foi gerado. ***
    exit /b 1
)

REM --- 2) Localiza o compilador do Inno Setup (ISCC.exe) -------------------
REM Procura nas instalacoes machine-wide e por-usuario (%LocalAppData%), e por fim no PATH.
set "ISCC="
set "P1=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
set "P2=%ProgramFiles%\Inno Setup 6\ISCC.exe"
set "P3=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if exist "%P1%" set "ISCC=%P1%"
if not defined ISCC if exist "%P2%" set "ISCC=%P2%"
if not defined ISCC if exist "%P3%" set "ISCC=%P3%"
if not defined ISCC for /f "delims=" %%I in ('where ISCC.exe 2^>nul') do if not defined ISCC set "ISCC=%%I"

if not defined ISCC (
    echo.
    echo *** ERRO: ISCC.exe nao encontrado. ***
    echo Instale o Inno Setup 6: https://jrsoftware.org/isdl.php
    exit /b 1
)

REM --- 3) Compila o instalador --------------------------------------------
echo.
echo === Gerando instalador com Inno Setup ===
echo.

"%ISCC%" "%~dp0installer.iss"
if errorlevel 1 (
    echo.
    echo *** ERRO: falha ao compilar o instalador. ***
    exit /b 1
)

echo.
echo === Instalador gerado com sucesso ===
echo Arquivo: dist\ClaudeTray-Setup.exe
echo.

REM --- 4) Regenera os manifestos winget (versao, SHA256, ReleaseDate) ------
echo === Atualizando manifestos winget ===
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-winget.ps1"
if errorlevel 1 (
    echo.
    echo *** ERRO: falha ao atualizar os manifestos winget. ***
    exit /b 1
)

endlocal
