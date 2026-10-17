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
- 64-bit Windows. Nothing to install: .NET is bundled into the exe,
  and no game DLLs are needed.


INSTALLATION
------------
1. Extract this archive to any folder.
2. Run xenus-dt1-decompiler.exe.


USAGE - GUI
-----------
Double-click xenus-dt1-decompiler.exe (no arguments).

1. Select the input folder containing .DT1 files.
2. Select the output folder.
3. Choose output format:
     Auto        - detects real format from magic bytes (recommended)
     dds/tga/png/bmp/jpg - converts via texconv.exe (included)
5. Click START DECOMPILE.


USAGE - Command Line
--------------------
xenus-dt1-decompiler.exe <input> [output_dir] [format]

  input         Path to a .DT1 file or a folder to scan recursively
  output_dir    Output folder (default: same as input)
  format        Output format: dds, tga, bmp, png, jpg (default: auto)

Old command lines with VELoader.dll as the 3rd argument or a MATERIALS
folder at the end still work; those arguments are simply ignored.

Examples:
  xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES" ".\out"
  xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES" ".\out" tga


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
- Normal map textures are automatically exported with corrected channel order.
  The engine stores them with a non-standard layout; the decompiler remaps
  the pixel bytes and fixes the DDS format masks. All other textures are
  saved as-is. Normal maps are recognised from the pixel data itself,
  so no MATERIALS folder or file naming convention is needed.
- Only .DT1 files are exported. .DT2 (64x64) and .DT3 (DXT5, normal maps
  only) are lower-quality copies of the same textures.
- All textures are internally stored as DDS regardless of filename suffix.
- texconv.exe (Microsoft DirectXTex, MIT license) is included.
  Source: https://github.com/microsoft/DirectXTex


LICENSE
-------
MIT - https://github.com/CodeNoob53/xenus-dt1-decompiler-release-repo/blob/main/LICENSE


SOURCE & RELEASES
-----------------
https://github.com/CodeNoob53/xenus-dt1-decompiler-release-repo
