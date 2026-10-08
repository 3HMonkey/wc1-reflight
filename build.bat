@echo off
rem Builds WC1 Reflight as a native, self-contained and trimmed executable (NativeAOT).
rem
rem   build.bat            -> publish\win-x64
rem   build.bat win-arm64  -> publish\win-arm64
rem
rem Needs the .NET 10 SDK and Visual Studio (or the Build Tools) with the
rem "Desktop development with C++" workload for the native linker.
rem The result is wc1.exe, SDL3.dll and THIRD-PARTY-NOTICES.txt; the fonts are compiled in.

setlocal
set "RID=%~1"
if "%RID%"=="" set "RID=win-x64"
set "ROOT=%~dp0"
set "OUT=%ROOT%publish\%RID%"

rem The NativeAOT linker step runs vcvarsall.bat, which needs vswhere.exe on PATH.
set "PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%"

if exist "%OUT%" rmdir /s /q "%OUT%"

dotnet publish "%ROOT%src\WingCommander\WingCommander.csproj" ^
  -c Release -r %RID% --self-contained true -o "%OUT%" ^
  -p:DebugType=none -p:DebugSymbols=false -p:CopyOutputSymbolsToPublishDirectory=false ^
  -p:UseSystemResourceKeys=true -p:GenerateDocumentationFile=false
if errorlevel 1 goto :failed

echo.
echo WC1 Reflight built into %OUT%:
dir /b "%OUT%"
echo.
echo Put config.json (see config.example.json) next to wc1.exe or into a parent folder.
exit /b 0

:failed
echo.
echo Build failed.
exit /b 1
