@echo off
chcp 65001 >nul
:: =================================================================================================
:: Xenus 2 DT1/DT2 Decompiler - Quick Start Script
:: =================================================================================================
:: This file was created to easily run the decompiler without constantly using the console.
:: Edit the paths below in any text editor (for example, Notepad).

:: 1. Specify the path to the file or folder with textures to unpack
:: (The program will automatically process not only the specified folder but also all folders inside it!)
:: For example (folder): "C:\Games\Xenus 2\CACHE\TEXTURES" 
:: For example (single file): "C:\Games\Xenus 2\CACHE\TEXTURES\MAP\GROUP_0_0_BMP.DT1"
set INPUT_PATH="C:\Games\Xenus 2\CACHE\TEXTURES"

:: 2. Specify the folder where the ready textures will be saved
:: The program will automatically create nested folders. By default: ".\out_tex"
set OUTPUT_PATH=".\out_tex"

:: 3. Specify the path to the VELoader.dll file (located in the game's root folder)
:: This is a mandatory game library required for the utility to properly unpack the cache
set VELOADER_PATH="C:\Games\Xenus 2\VELoader.dll"

:: 4. Save format (by default the program determines it automatically from the name, e.g. _TGA -> .tga)
:: If you want to playfully force another one (e.g. tga, dds, png), specify it here (or leave empty)
set OUTPUT_EXT=""

:: =================================================================================================
:: Execution Process (usually nothing below needs to be changed)
:: =================================================================================================

echo [Xenus 2 DT1/DT2 Decompiler]
echo.
echo Input Path  : %INPUT_PATH%
echo Output Path : %OUTPUT_PATH%
echo VELoader.dll: %VELOADER_PATH%
echo Format (ext): %OUTPUT_EXT%
echo.
echo ---------------------------------------------------
echo Starting the process...

xenus-dt1-decompiler.exe %INPUT_PATH% %OUTPUT_PATH% %VELOADER_PATH% %OUTPUT_EXT%

echo ---------------------------------------------------
echo Process completed!
pause
