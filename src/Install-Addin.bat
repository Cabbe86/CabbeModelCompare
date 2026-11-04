@echo off
chcp 65001 > nul
echo ============================================================
echo   Installation av Cabbe Model Compare (C# Revit Add-in)
echo ============================================================
echo.

set "SCRIPT_DIR=%~dp0"
set "ADDIN_SRC=%SCRIPT_DIR%CabbeModelCompare.addin"
set "DLL_SRC=%SCRIPT_DIR%CabbeModelCompare.dll"

if not exist "%DLL_SRC%" (
    echo Fel: Hittade inte CabbeModelCompare.dll i installationsmappen!
    pause
    exit /b 1
)

for %%V in (2025 2026 2027 2024) do (
    set "DEST_DIR=%APPDATA%\Autodesk\Revit\Addins\%%V"
    if exist "%APPDATA%\Autodesk\Revit\Addins\%%V" (
        echo Installerar för Revit %%V...
        copy /Y "%ADDIN_SRC%" "%DEST_DIR%\" > nul
        copy /Y "%DLL_SRC%" "%DEST_DIR%\" > nul
        echo   - Klart för Revit %%V.
    ) else (
        echo   - Revit %%V Addins-mapp hittades inte (hoppar över).
    )
)

echo.
echo ============================================================
echo   Klart! Starta om Revit.
echo   Fliken "Cabbe" med panelen "Jämförelse" och
echo   knappen "Modelljämförelse" finns nu i Ribbon.
echo ============================================================
echo.
pause
