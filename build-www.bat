@ECHO OFF
REM Build the TrellisKit website image (nginx with the website built for the neutral sample property).
REM Builds for linux/amd64 and linux/arm64/v8 on Docker Build Cloud, pushes to Docker Hub, then pulls the pushed tags
REM into the local image store. Tags: latest, plus the tag given as the first argument.
REM
REM   build-www.bat             jchristn77/trelliskit-www:latest
REM   build-www.bat v1.0.0      jchristn77/trelliskit-www:v1.0.0 and jchristn77/trelliskit-www:latest
SETLOCAL
CD /D "%~dp0"
SET "IMAGE_NAME=jchristn77/trelliskit-www"
SET "TAG=%~1"
IF "%TAG%"=="" SET "TAG=latest"
SET "TAGS=--tag %IMAGE_NAME%:latest"
IF NOT "%TAG%"=="latest" SET "TAGS=%TAGS% --tag %IMAGE_NAME%:%TAG%"
ECHO Image:     %IMAGE_NAME%:%TAG%
ECHO Platforms: linux/amd64,linux/arm64/v8
docker buildx use cloud-jchristn77-jchristn77 2>NUL || docker buildx create --driver cloud jchristn77/jchristn77 --use
docker buildx build --builder cloud-jchristn77-jchristn77 --platform linux/amd64,linux/arm64/v8 --file docker/www/Dockerfile  %TAGS% --push .
IF ERRORLEVEL 1 EXIT /B 1
ECHO Pulling the pushed tags into the local image store...
docker pull %IMAGE_NAME%:latest
IF NOT "%TAG%"=="latest" docker pull %IMAGE_NAME%:%TAG%
ECHO Done: %IMAGE_NAME%:%TAG%
ENDLOCAL
