@echo off
setlocal
cd %~dp0

if defined VisualStudioVersion (
    if not defined __VSVersion echo Detected Visual Studio %VisualStudioVersion% developer command ^prompt environment
    goto skip_setup
)
echo Searching ^for Visual Studio 2019 or later installation
set _VSWHERE="%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist %_VSWHERE% (
    for /f "usebackq tokens=*" %%i in (`%_VSWHERE% -latest -version [16 -property installationPath`) do set _VSCOMNTOOLS=%%i\Common7\Tools
    goto call_vs
)
echo Visual Studio 2019 or later not found
:call_vs
if not exist "%_VSCOMNTOOLS%" (
    echo Error: Visual Studio 2019 required.
    exit /b 1
)
set VSCMD_START_DIR="%~dp0"
echo "%_VSCOMNTOOLS%\VsDevCmd.bat"
call "%_VSCOMNTOOLS%\VsDevCmd.bat"
:skip_setup

msbuild /m /v:m /p:Configuration=Release /target:Publish /restore
set _MSBUILD_ERRORLEVEL=%ERRORLEVEL%
if not %_MSBUILD_ERRORLEVEL% == 0 (echo BUILD FAILED && exit /b 1)
echo Build Succeeded
exit /b 0
