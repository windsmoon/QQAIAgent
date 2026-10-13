@echo off
setlocal

pushd "%~dp0" || exit /b 1

if exist ".\Bin" rmdir /s /q ".\Bin"
if exist ".\Bin" (
    echo Failed to delete .\Bin - remove it manually and retry.
    popd
    pause
    exit /b 1
)

dotnet publish ".\QQAIAgent\QQAIAgent.Host\QQAIAgent.Host.csproj" -c Debug -r win-x64 --self-contained true -o ".\Bin"
set "PublishExitCode=%ERRORLEVEL%"
popd

pause
exit /b %PublishExitCode%
