@echo off
REM Build the server and website from this engine's source for a property folder (compose.yaml plus compose.dev.yaml)
REM and recreate the stack. Non-destructive: named volumes are preserved.
REM
REM   dev.bat                                                the neutral sample property (docker\.env)
REM   dev.bat <property folder>\deploy\local.env             a property's env file (path from your shell)
set "ENV_ARGS=--env-file .env"
if not "%~1"=="" set "ENV_ARGS=%ENV_ARGS% --env-file "%~f1""
set "ENV_ARGS=%ENV_ARGS% --env-file .env.local"
set "FILES=-f compose.yaml -f compose.dev.yaml"
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File env-local.ps1 || exit /b 1
docker compose %FILES% %ENV_ARGS% pull --ignore-buildable
docker compose %FILES% %ENV_ARGS% down --remove-orphans
docker compose %FILES% %ENV_ARGS% up -d --build
docker ps -a
