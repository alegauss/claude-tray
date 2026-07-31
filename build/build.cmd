@echo off
REM ==========================================================================
REM build.cmd - Compila e publica o ClaudeTray como .exe self-contained
REM Saida: bin\Release\net10.0-windows\win-x64\publish\ClaudeTray.exe
REM ==========================================================================
setlocal

REM Este script vive em build\; o projeto (ClaudeTray.csproj) esta na pasta acima.
cd /d "%~dp0.."

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
