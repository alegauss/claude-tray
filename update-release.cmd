@echo off
REM ==========================================================================
REM update-release.cmd - Sobe o numero de versao (release) do ClaudeTray e
REM gera o release completo em um passo.
REM   1) Atualiza <Version> em ClaudeTray.csproj e os campos de versao nos
REM      manifestos winget\ (PackageVersion, InstallerUrl, ReleaseNotesUrl,
REM      DisplayVersion).
REM   2) Chama build-installer.cmd (build + instalador + update-winget.ps1,
REM      que grava InstallerSha256 e ReleaseDate).
REM
REM Uso:  update-release 1.4.1
REM ==========================================================================
setlocal

cd /d "%~dp0"

if "%~1"=="" (
    echo *** ERRO: informe a nova versao. ***
    echo Uso: update-release 1.4.1
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-release.ps1" -Version "%~1"
if errorlevel 1 (
    echo.
    echo *** ERRO: falha ao atualizar a versao. ***
    exit /b 1
)

REM --- Gera o release: build + instalador + manifestos winget (SHA256/data) ---
call "%~dp0build-installer.cmd"
if errorlevel 1 (
    echo.
    echo *** ERRO: falha ao gerar o instalador. ***
    exit /b 1
)

endlocal
