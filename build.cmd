@echo off
setlocal
cd %~dp0
msbuild /m /v:m /p:Configuration=Release /target:Restore
msbuild /m /v:m /p:Configuration=Release /target:Publish
