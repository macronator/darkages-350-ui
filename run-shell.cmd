@echo off
rem Build + run the live client shell from source.
rem   run-shell.cmd <DarkAges.dat> [ui-350.json]
rem or set DA_DAT to your DarkAges.dat and just run:  run-shell.cmd
rem (ui-350.json is found automatically under docs\ when omitted)
setlocal
if "%~1"=="" (
  dotnet run --project "%~dp0shell" -c Release -- "" "%~dp0docs\ui-350.json"
) else (
  dotnet run --project "%~dp0shell" -c Release -- %*
)
endlocal
