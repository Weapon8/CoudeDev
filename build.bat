@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

REM --- Trouve automatiquement le .csproj du dossier (marche pour n'importe quel module) ---
set "PROJ="
for %%F in (*.csproj) do if not defined PROJ set "PROJ=%%F"
if not defined PROJ (
    echo Aucun .csproj trouve dans ce dossier.
    pause
    exit /b 1
)

echo ============================================================
echo   Publication : %PROJ%   ^>   exe_final\  (un seul .exe)
echo ============================================================
echo.

if exist "exe_final" rmdir /s /q "exe_final"

dotnet publish "%PROJ%" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:SatelliteResourceLanguages=fr ^
  -p:DebugType=none -p:DebugSymbols=false ^
  -o exe_final

if errorlevel 1 (
    echo.
    echo *** ECHEC de la publication ***
    pause
    exit /b 1
)

REM --- Ne garder QUE le .exe (on vire pdb, json residuels, sous-dossiers de langue) ---
pushd "exe_final"
for %%F in (*) do if /I not "%%~xF"==".exe" del /q "%%F"
for /d %%D in (*) do rmdir /s /q "%%D"
popd

echo.
echo ============================================================
echo   OK : exe autonome pret dans exe_final\
echo ------------------------------------------------------------
dir /b "exe_final"
echo ------------------------------------------------------------
echo   Copie ce .exe dans le dossier modules\ de la suite.
echo ============================================================
echo.
start "" explorer "exe_final"
pause
