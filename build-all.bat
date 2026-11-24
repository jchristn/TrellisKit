@ECHO OFF
REM Build and push both TrellisKit images (server and website) with the same tags.
REM   build-all.bat            latest
REM   build-all.bat v1.0.0     v1.0.0 and latest
CD /D "%~dp0"
CALL build-server.bat %1 || EXIT /B 1
CALL build-www.bat %1
