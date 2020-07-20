@echo off
setlocal
cd %~dp0
midl VdsWrapper.idl
if not %errorlevel%==0 (exit /b 1)
tlbimp VdsWrapper.tlb
if not %errorlevel%==0 (exit /b 1)
exit /b 0