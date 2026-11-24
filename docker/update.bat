@echo off
REM Pull the pinned published images (compose.yaml) and start or recreate the stack. Non-destructive: named volumes
REM are preserved. For a fresh start use factory\reset.bat; to build from source use dev.bat.
REM
REM   update.bat                                   docker\.env
REM   update.bat <env file>                        an extra env file (path from your shell)
set "ENV_ARGS=--env-file .env"
if not "%~1"=="" set "ENV_ARGS=%ENV_ARGS% --env-file "%~f1""
set "ENV_ARGS=%ENV_ARGS% --env-file .env.local"
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File env-local.ps1 || exit /b 1
docker compose %ENV_ARGS% pull || exit /b 1
docker compose %ENV_ARGS% up -d --remove-orphans
docker ps -a
