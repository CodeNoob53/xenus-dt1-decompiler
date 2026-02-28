Xenus DT1/DT2 Decompiler
========================

Batch decoder for compressed texture cache files (*.DT1) of Xenus 2: White Gold.
Saves decoded textures as .dds, .tga, .png, .bmp, or .jpg while preserving
the original folder structure.


REQUIREMENTS
------------
- Windows x86 or x64
- .NET 8 Runtime: https://dotnet.microsoft.com/download/dotnet/8.0
- VELoader.dll from the Steam release of Xenus 2: White Gold
  (place it next to xenus-dt1-decompiler.exe)


INSTALLATION
------------
1. Extract this archive to any folder.
2. Copy VELoader.dll from your game folder next to xenus-dt1-decompiler.exe.
3. Run xenus-dt1-decompiler.exe.


USAGE - GUI
-----------
Double-click xenus-dt1-decompiler.exe (no arguments).

1. Select the input folder containing .DT1 files.
2. Select the output folder.
3. VELoader.dll is detected automatically if placed next to the exe.
4. Choose output format:
     Auto        - detects real format from magic bytes (recommended)
     dds/tga/png/bmp/jpg - converts via texconv.exe (included)
5. Click START DECOMPILE.


USAGE - Command Line
--------------------
xenus-dt1-decompiler.exe <input> [output_dir] [veloader_path] [format]

  input         Path to a .DT1 file or a folder to scan recursively
  output_dir    Output folder (default: same as input)
  veloader_path Path to VELoader.dll (auto-detected if omitted)
  format        Output format: dds, tga, bmp, png, jpg (default: auto)

Examples:
  xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES" ".\out"
  xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES" ".\out" "C:\Games\Xenus 2\VELoader.dll" tga


NOTES
-----
- VELoader.dll from pirated copies often fails with error 1114.
  Use the DLL from the Steam release of the game.
- texconv.exe (Microsoft DirectXTex, MIT license) is included.
  Source: https://github.com/microsoft/DirectXTex


LICENSE
-------
MIT - https://github.com/CodeNoob53/xenus-dt1-decompiler/blob/main/LICENSE


SOURCE & AUTHOR
---------------
https://github.com/CodeNoob53/xenus-dt1-decompiler
