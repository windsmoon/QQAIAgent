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

dotnet publish ".\QQAIAgent\QQAIAgent.Host\QQAIAgent.Host.csproj" -c Release -r win-x64 --self-contained true -p:AppHostDotNetSearch=AppRelative -p:AppHostRelativeDotNet=.. -o ".\Bin"
set "PublishExitCode=%ERRORLEVEL%"
if not "%PublishExitCode%"=="0" goto :finish

dotnet publish ".\QQAIAgent\QQAIAgent.Cli\QQAIAgent.Cli.csproj" -c Release -r win-x64 --self-contained true -p:AppHostDotNetSearch=AppRelative -p:AppHostRelativeDotNet=.. -o ".\Bin"
set "PublishExitCode=%ERRORLEVEL%"
if not "%PublishExitCode%"=="0" goto :finish

powershell -NoProfile -ExecutionPolicy Bypass -File ".\organize-publish.ps1" -PublishDirectory ".\Bin"
set "PublishExitCode=%ERRORLEVEL%"

:finish
popd

pause
exit /b %PublishExitCode%
