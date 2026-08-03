@echo off
REM ==========================================================================
REM build.cmd - Compila e publica o ClaudeTray como .exe self-contained
REM Saida: bin\Release\net10.0-windows\win-x64\publish\ClaudeTray.exe
REM ==========================================================================
setlocal

REM Este script vive em build\; o projeto (ClaudeTray.csproj) esta na pasta acima.
cd /d "%~dp0.."

REM --- 0) Nada rodando de dentro da pasta que o publish vai sobrescrever (T266) -------------
REM Sem isto o SDK falha dentro do proprio bundler, com UnauthorizedAccessException e quatro
REM linhas de MSBuild, para um estado que nao e bug: quem instalou compilando roda a bandeja
REM exatamente dali. O guard nomeia o pid e o caminho, e nao encerra nada por conta propria.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0guard-publish.ps1"
if errorlevel 1 exit /b 1

echo.
echo === Publicando ClaudeTray (Release, win-x64, self-contained) ===
echo.

dotnet publish -c Release
set "PUBERR=%errorlevel%"

REM O SDK do WPF gera um projeto temporario "<nome>_<aleatorio>_wpftmp.csproj" na pasta do projeto
REM durante a compilacao do XAML e normalmente o apaga; se um build for interrompido ele fica para
REM tras. Remove qualquer resquicio aqui (esta no .gitignore) para nao poluir a pasta / o git.
del /q "%~dp0..\*_wpftmp.csproj" >nul 2>nul

if not "%PUBERR%"=="0" (
    echo.
    echo *** ERRO: falha no dotnet publish. ***
    exit /b 1
)

echo.
echo === Build concluido com sucesso ===
echo Executavel: bin\Release\net10.0-windows\win-x64\publish\ClaudeTray.exe
echo.

endlocal
