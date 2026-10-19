@echo off
chcp 65001 >nul
rem Keo tha thu muc project Unity vao file nay (hoac bam dup roi dan duong dan).
set "SCRIPT=%~dp0unity_scan.py"
set "PROJ=%~1"
if "%PROJ%"=="" set /p "PROJ=Duong dan project Unity: "
where py >nul 2>nul
if %errorlevel%==0 (
  py -3 "%SCRIPT%" "%PROJ%" --open
) else (
  python "%SCRIPT%" "%PROJ%" --open
)
if errorlevel 1 echo. & echo Loi: can cai Python 3 tu https://www.python.org/downloads/ (tick "Add python.exe to PATH").
pause
