@echo off
rem WW315. Build this application and run every case in cases\ against it - the one command a person
rem types, here and in a guest.
rem
rem It exists because -Run on winwright's runner is documented as "what a developer there types", and
rem until now nobody could type anything: the adoption had a test project and no command, so the
rem invocation lived in whoever's shell history had it last. That is the thing WW293 already fixed one
rem layer up, and this is the same fix at the layer below it.
rem
rem The build is the application and not the test project. winwright.json names
rem bin\Debug\net10.0-windows\win-x64\ClaudeTray.exe, and a case launches that file: a run that built
rem only the driver would drive whatever exe was last left there, which is a green about an old build.
rem The RID is spelled because ClaudeTray.csproj sets RuntimeIdentifier unconditionally.
rem
rem The cases need a desk. Run this in the guest - `run-tests-vm.cmd` in the winwright tree, with
rem -Tree pointed here - rather than at a machine somebody is using, which is WW157's whole argument
rem and WW227's reason for taking a tree at all.
setlocal

set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Debug

set RESULTS=%2
if "%RESULTS%"=="" set RESULTS=TestResults

dotnet build "%~dp0ClaudeTray.csproj" --configuration %CONFIG% --nologo || exit /b 1

rem A trx and not just the console, because the runner copies a file out of the guest and cannot read
rem what scrolled past in it.
dotnet test "%~dp0tests\ClaudeTray.Cases\ClaudeTray.Cases.csproj" ^
  --configuration %CONFIG% --nologo ^
  --logger "trx;LogFileName=cases.trx" ^
  --results-directory "%~dp0%RESULTS%"

exit /b %ERRORLEVEL%
