Xenus DT1/DT2 Decompiler
========================

Batch decoder for compressed texture cache files (*.DT1 / *.DT2) used in:
  - Xenus 2: White Gold
  - Boiling Point: Road to Hell
  - The Precursors

Extracts textures while preserving the original folder structure.
Output formats: DDS, TGA, PNG, BMP, JPG.


REQUIREMENTS
------------
- Windows x86 or x64
- .NET 8 Runtime: https://dotnet.microsoft.com/download/dotnet/8.0
- VELoader.dll from the Steam release of Xenus 2: White Gold
  (place it next to xenus-dt1-decompiler.exe, or specify path in GUI)


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
  xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES" ".\out" "" tga


TEXTURE REPACKING WORKFLOW
--------------------------
Vital Engine 3 automatically recompresses TGA files into cache format
when launching the game. To replace a texture:

1. Extract the original texture with this tool.
2. Edit the image (keep the same resolution, or see notes below).
3. Place the modified TGA in the game TEXTURES folder
   (same relative path as in the cache).
4. Launch the game — it will recompress automatically.

Note: Maximum supported texture size is 2048x2048. Recommended: 1024x1024.
      HUD textures reference pixel coordinates in XML files — resizing them
      requires updating those values too.


NOTES
-----
- VELoader.dll from pirated copies often fails with error 1114.
  Use the DLL from the Steam release of the game.
- Normal map textures (_N suffix) are automatically exported with corrected
  channel order. VELoader outputs them with a non-standard channel layout;
  the decompiler remaps the pixel bytes and fixes the DDS format masks.
  Only applies to files with _N or _N_ in the name; all other textures
  are saved as-is from VELoader.
  Note: _N suffix detection is a temporary heuristic. Future versions will
  use .MAT material files (CACHE/MATERIALS.DAT) for accurate detection.
- All textures are internally stored as DDS regardless of filename suffix.
- texconv.exe (Microsoft DirectXTex, MIT license) is included.
  Source: https://github.com/microsoft/DirectXTex


LICENSE
-------
MIT - https://github.com/CodeNoob53/xenus-dt1-decompiler-release-repo/blob/main/LICENSE


SOURCE & RELEASES
-----------------
https://github.com/CodeNoob53/xenus-dt1-decompiler-release-repo
