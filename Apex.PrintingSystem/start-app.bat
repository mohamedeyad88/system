@echo off
chcp 65001 >nul
title Apex Printing System
cd /d "%~dp0Apex.UI"
echo Building and starting application...
dotnet run
pause

