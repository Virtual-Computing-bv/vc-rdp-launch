@echo off
REM PowerShell-vrije build met de in-box .NET Framework C#-compiler.
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "SRC=%~dp0..\src"
set "OUT=%~dp0"
if not exist "%CSC%" ( echo csc niet gevonden: %CSC% & exit /b 1 )

set "ICO="
if exist "%SRC%\red.ico" set "ICO=/win32icon:%SRC%\red.ico"

REM 1) launcher
"%CSC%" /nologo /target:winexe /platform:x64 "/out:%OUT%vc-rdp-launch.exe" %ICO% /r:System.Windows.Forms.dll "%SRC%\VcRdpLaunch.cs"
if errorlevel 1 exit /b 1

REM 2) setup met launcher embedded + admin-manifest
"%CSC%" /nologo /target:winexe /platform:x64 "/out:%OUT%vc-rdp-setup.exe" "/win32manifest:%SRC%\setup.manifest" "/resource:%OUT%vc-rdp-launch.exe,vc-rdp-launch.exe" %ICO% /r:System.Windows.Forms.dll "%SRC%\VcRdpSetup.cs"
if errorlevel 1 exit /b 1

echo OK: vc-rdp-launch.exe + vc-rdp-setup.exe
endlocal
