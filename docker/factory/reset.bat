@echo off
REM ============================================================================
REM TrellisKit factory reset: restores the Docker deployment to factory defaults.
REM DESTRUCTIVE: stops the stack and deletes every named volume (database, server settings, uploads, traces, logs,
REM metrics, Grafana state), then starts a fresh stack. docker\.env.local (secrets) is kept; delete it first to rotate.
REM
REM   factory\reset.bat                                         published images (compose.yaml)
REM   factory\reset.bat <property folder>\deploy\local.env      build from source for a property (adds compose.dev.yaml)
REM ============================================================================
setlocal
set "ENV_ARGS=--env-file .env"
set "FILES=-f compose.yaml"
set "BUILD="
if not "%~1"=="" (
  set "ENV_ARGS=%ENV_ARGS% --env-file "%~f1""
  set "FILES=-f compose.yaml -f compose.dev.yaml"
  set "BUILD=--build"
)
set "ENV_ARGS=%ENV_ARGS% --env-file .env.local"
cd /d "%~dp0\.."
echo =========================================
echo   TrellisKit Factory Reset
echo =========================================
echo.
echo WARNING: this deletes ALL data in this stack: members, content, uploads, settings, logs, metrics, and traces.
echo.
set /p CONFIRM=Type RESET to confirm: 
if not "%CONFIRM%"=="RESET" (
  echo Reset cancelled.
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File env-local.ps1 || exit /b 1
echo [1/2] Stopping containers and removing volumes...
docker compose %FILES% %ENV_ARGS% down --volumes --remove-orphans
echo [2/2] Starting a fresh stack...
if "%BUILD%"=="" docker compose %FILES% %ENV_ARGS% pull
docker compose %FILES% %ENV_ARGS% up -d %BUILD%
docker ps -a
echo.
echo Factory reset complete. Website: http://127.0.0.1:8080
endlocal
