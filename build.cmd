@echo off
setlocal
cd /d "%~dp0"
echo ================================================
echo   Build Hinh nen ^& Quote (M1) cho Windows
echo ================================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [!] Chua cai .NET 8 SDK.
  echo     Cach cai nhanh: mo PowerShell va chay lenh:
  echo         winget install Microsoft.DotNet.SDK.8
  echo     Hoac tai tai: https://dotnet.microsoft.com/download/dotnet/8.0
  echo     Cai xong, dong cua so nay va bam dup build.cmd lan nua.
  echo.
  pause
  exit /b 1
)

dotnet publish "src\WallpaperQuote\WallpaperQuote.csproj" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ^
  -o dist

if errorlevel 1 (
  echo.
  echo [!] BUILD LOI. Hay chup man hinh hoac copy phan loi o tren gui cho Claude de sua.
  echo.
  pause
  exit /b 1
)

echo.
echo [OK] Xong. File chay: %~dp0dist\WallpaperQuote.exe
echo      Bam dup file do de chay app. Icon se hien duoi khay he thong.
echo.
explorer "%~dp0dist"
pause
