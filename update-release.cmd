@echo off
REM ==========================================================================
REM update-release.cmd - Sobe o numero de versao (release) do ClaudeTray e
REM gera o release completo em um passo.
REM   1) Atualiza <Version> em ClaudeTray.csproj e os campos de versao nos
REM      manifestos winget\ (PackageVersion, InstallerUrl, ReleaseNotesUrl,
REM      DisplayVersion).
REM   2) Chama build-installer.cmd (build + instalador + update-winget.ps1,
REM      que grava InstallerSha256 e ReleaseDate).
REM   3) [opcional] Se o 2o parametro for "upload": commita o bump, cria e
REM      envia a tag vX.Y.Z e cria a release no GitHub com o instalador anexado.
REM
REM Uso:  update-release 1.4.1
REM       update-release 1.4.2 upload
REM ==========================================================================
setlocal

cd /d "%~dp0"

if "%~1"=="" (
    echo *** ERRO: informe a nova versao. ***
    echo Uso: update-release 1.4.1 [upload]
    exit /b 1
)

REM --- 1) Sobe a versao no csproj e nos manifestos winget ------------------
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-release.ps1" -Version "%~1"
if errorlevel 1 (
    echo.
    echo *** ERRO: falha ao atualizar a versao. ***
    exit /b 1
)

REM --- 2) Gera o release: build + instalador + manifestos (SHA256/data) ----
call "%~dp0build-installer.cmd"
if errorlevel 1 (
    echo.
    echo *** ERRO: falha ao gerar o instalador. ***
    exit /b 1
)

REM --- 3) Upload (opcional): commit + tag + release no GitHub --------------
if /i not "%~2"=="upload" goto :fim

echo.
echo === Publicando release v%~1 no GitHub ===
echo.

REM Commita o bump de versao (csproj + manifestos winget com SHA256/data).
call run-commit.cmd -m "chore: release v%~1"
if errorlevel 1 (
    echo *** ERRO: falha ao commitar o bump de versao. ***
    exit /b 1
)

REM Publica o commit no branch atual.
git push
if errorlevel 1 (
    echo *** ERRO: falha no git push. ***
    exit /b 1
)

REM Cria a tag vX.Y.Z e a envia para o remoto.
git tag "v%~1"
if errorlevel 1 (
    echo *** ERRO: falha ao criar a tag v%~1 - talvez ja exista. ***
    exit /b 1
)
git push origin "v%~1"
if errorlevel 1 (
    echo *** ERRO: falha ao enviar a tag v%~1. ***
    exit /b 1
)

REM Cria a release no GitHub com o instalador anexado (notas auto-geradas).
gh release create "v%~1" "%~dp0dist\ClaudeTray-Setup.exe" --title "v%~1" --generate-notes
if errorlevel 1 (
    echo *** ERRO: falha ao criar a release no GitHub. ***
    exit /b 1
)

echo.
echo === Release v%~1 publicada com sucesso ===
echo.

:fim
endlocal
