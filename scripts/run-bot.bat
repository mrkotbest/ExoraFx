@echo off
REM ============================================================
REM  Exora FX bot launcher
REM
REM  Runs the bot in Release straight from sources.
REM  All output goes to chat-logs/bot-YYYY-MM-DD.log via the app's logger.
REM  To stop: close this console window or run `taskkill /im dotnet.exe`.
REM ============================================================

cd /d "%~dp0\.."

REM Wait for DNS resolution (up to ~3 min). The bot has its own retry loop;
REM skipping noisy first-failures keeps the log readable on cold boot.
set /a TRIES=0
:wait_net
ping -n 1 api.telegram.org >nul 2>&1
if %ERRORLEVEL% EQU 0 goto :net_ready
set /a TRIES+=1
if %TRIES% GEQ 36 goto :net_ready
timeout /t 5 /nobreak >nul
goto :wait_net
:net_ready

REM Kill any stale instance before building. Two pollers against the same bot token
REM cause "Conflict: terminated by other getUpdates request" and updates split across
REM instances. Match by CommandLine because `dotnet run` keeps the bot inside the
REM parent dotnet.exe (no separate apphost), so plain taskkill on the apphost does nothing.
powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | Where-Object { $_.CommandLine -like '*ExoraFx.Api.csproj*' -or $_.CommandLine -like '*ExoraFx.Api.dll*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>&1
taskkill /F /IM ExoraFx.Api.exe /T >nul 2>&1

REM Build once. No-op if already up to date.
dotnet build ExoraFx.Api/ExoraFx.Api.csproj -c Release --nologo

set ASPNETCORE_ENVIRONMENT=Production

dotnet run --project ExoraFx.Api/ExoraFx.Api.csproj ^
    -c Release --no-build --no-launch-profile
