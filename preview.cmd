@echo off
rem WW480. Look at a window: build, draw it, and say where the picture is.
rem
rem This replaces scripts\Capture-Window.ps1, which copied the pixels on screen inside a window's
rem rectangle. A case asks this application to draw its own visual tree instead — no foreground, no
rem second instance in the frame, and no window standing over the region — and the engine refuses a
rem picture it cannot vouch for rather than writing one somebody would read as evidence.
rem
rem   preview.cmd            every preview case
rem   preview.cmd shell      the shell, on each of its three destinations
rem   preview.cmd panels     every settings panel the sidebar declares
rem   preview.cmd context    the context load page over its own fixture
rem   preview.cmd note       the method note, whose popup no copy of a screen can photograph
rem
rem A word that names no tag is refused with the list there is, so a typo costs a corrected word.
rem Then READ the PNG and judge it: this prints where it went and never what is in it.
setlocal

set ASKED=%1
if "%ASKED%"=="" set ASKED=preview

set CLAUDETRAY_CASES=%ASKED%

dotnet build "%~dp0ClaudeTray.csproj" --configuration Debug --nologo || exit /b 1

dotnet test "%~dp0tests\ClaudeTray.Cases\ClaudeTray.Cases.csproj" ^
  --configuration Debug --nologo ^
  --filter "FullyQualifiedName~CasesRun" || exit /b 1

echo.
echo pictures: %~dp0docs\_preview\
exit /b 0
