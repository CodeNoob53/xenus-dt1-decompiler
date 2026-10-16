# Xenus DT1 Decompiler

[![Platform](https://img.shields.io/badge/platform-Windows-blue.svg)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Build](https://img.shields.io/badge/.NET-8.0-blueviolet.svg)](https://dotnet.microsoft.com/download)
[![Version](https://img.shields.io/badge/version-3.0.0-orange.svg)](CHANGELOG.md)

**[Українська версія (README_UA.md)](README_UA.md)**

A tool for batch-decoding the compressed texture cache of **Xenus 2: White Gold**, **Boiling Point: Road to Hell**, and **The Precursors** (`*.DT1` / `*.DT2` files) into standard image formats (`.dds`, `.tga`, `.png`, etc.) while preserving the original directory structure.

---

## How it works

Starting from v3.0.0 the tool **no longer needs `VELoader.dll`**. The `Unload` routine of the engine's `VELoader.dll` was reverse-engineered and reimplemented in managed code: the "proprietary" compression is a standard **zlib 1.2.2** stream whose first 16 bytes are XOR-ed with `0x6E`. Decompression uses .NET's built-in `ZLibStream`.

The output was verified byte-for-byte against the original `VELoader.dll` on 3857 files, and all 21 921 DT1/DT2 files of the game decode with matching size and zlib Adler-32 checksum.

---

## Important: actual file formats

The game engine caches textures internally as **DDS** (DirectDraw Surface), regardless of the filename suffix. For example, `GRASS_TGA.DT1` decompresses to a DDS file — the `_TGA` suffix is just the engine's internal metadata tag, not the actual format.

Starting from v2.0.0, the tool detects the real format from the file's magic bytes and:
- **Without a format argument / Auto mode** — saves with the correct extension (`.dds`, `.png`, etc.), ignoring the filename hint.
- **With a format argument** (e.g. `tga`) — converts the DDS to the requested format using [texconv](https://github.com/microsoft/DirectXTex/wiki/Texconv) (part of [DirectXTex](https://github.com/microsoft/DirectXTex) by Microsoft, MIT license), correctly decoding DXT1/DXT5 compression including the alpha channel. `texconv.exe` is bundled automatically in the release archive.

---

## DT1 / DT2 file format

Each `.DT1` or `.DT2` file is a single compressed asset. The 8-byte header is followed immediately by the compressed payload:

```text
Offset  Size  Description
0       4     bits 0..27  = uncompressed size
              bits 28..31 = compression method
4       4     bits 0..26  = packed size, including this 8-byte header
              bits 27..31 = key index (method 7 only)
8       N     Payload
```

| Method | Payload |
|---|---|
| 0 | Stored, no compression |
| 1..3 | Another codec, not used by any game file (unsupported) |
| 4..6 | First `min(16, N)` bytes XOR `0x6E`, then a standard zlib stream (`0x16 0x30` → `0x78 0x5E`) |
| 7 | Same as 4..6; if the payload is ≥ 256 bytes it is additionally encrypted with a key table (unsupported, only `resource.DT1` uses it) |

All texture cache files use method 5.

A texture can have up to three cache files:

| File | Contents |
|---|---|
| `.DT1` | Full resolution (DXT or uncompressed 32-bit). This is what the tool exports. |
| `.DT2` | 64×64 low-detail DXT copy |
| `.DT3` | Full-resolution DXT5 copy, present only for normal maps |

`.DT2` and `.DT3` are lower-quality duplicates of `.DT1`, so they are skipped.

> **Note:** This tool handles `.DT1` / `.DT2` files only. `CE#$` archives (e.g. `CACHE/*.DAT`) have a different structure — the format is fully documented and `CE#$` support may be integrated into a future version of this tool once finalized.

---

## SPECIAL Look-Up Tables

`TEXTURES/SPECIAL` contains two lookup tables that are not images:

| Texture | DDS format | Size | Contents |
|---|---|---|---|
| `DXT1CTRL` | `D3DFMT_G16R16F` (FourCC `112`) | 256×256, 2 channels | Both channels hold the 16-bit code `x + 256·y` |
| `R5G6R5TABLE` | `D3DFMT_A16B16G16R16F` (FourCC `113`) | 32×64×32 **volume**, 4 channels | 16-bit codes built from the texel's x/y/z (R5G6B5-style bit fields) |

Although the formats are half-float, the values are **16-bit integer bit patterns**. Read as floats they are mostly denormals and NaNs, so any float conversion (e.g. `texconv`) destroys them.

- **Auto / `dds`:** the table is saved as the original DDS, unchanged. This is the file to use for repacking.
- **`tga` / `png` / `bmp`:** the table is packed losslessly into 8-bit RGBA, and `texconv` is not used:
  - each pair of 16-bit channels `(c0, c1)` becomes one pixel: `R = lo(c0)`, `G = hi(c0)`, `B = lo(c1)`, `A = hi(c1)`;
  - with 4 channels, the `(c2, c3)` pair is placed in a second block to the right;
  - volume slices are stacked top to bottom.

  `DXT1CTRL` becomes a 256×256 image. `R5G6R5TABLE` becomes 64×2048 (2 channel pairs × 32 slices). The top-left block (slice 0, channels 0–1) looks the same as in Noesis. `jpg` is lossy and should not be used for these tables.

---

## Normal map channel correction

The engine stores normal map textures with a non-standard channel layout. The tool remaps pixel bytes to standard RGBA so normal maps render correctly in all viewers.

In the cache, an uncompressed normal map is stored as `A8R8G8B8` with the vector swizzled across the bytes: `B=Y`, `G=Z`, `R=0`, `A=X`. The tool remaps it to `R=X`, `G=Y`, `B=Z`, `A=255`.

Normal maps are identified from the pixel data itself, with no file names or `.MAT` files involved: the R byte is `0` in every pixel and Z averages ≥ 128. In the Xenus 2 cache this matches all 818 normal maps, which are exactly the textures that also have a `.DT3`, and none of the other uncompressed textures (sky, menus, diffuse).

---

## Requirements

- **OS:** Windows (x86 or x64)
- **Runtime:** [.NET SDK 8.0+](https://dotnet.microsoft.com/download/dotnet/8.0)
- No game DLLs are required.

---

## Project structure

```text
xenus-dt1-decompiler/
├── src/
│   └── XenusDt1Decompiler/
│       ├── XenusDt1Decompiler.csproj
│       ├── Program.cs
│       ├── DecompilerCore.cs
│       └── MainForm.cs
├── dist/                       ← release assets (bundled into the zip)
│   ├── app.ico
│   └── README.txt
├── .github/
│   └── workflows/
│       └── release.yml
├── run_decompiler.bat
├── .gitignore
├── README.md
└── README_UA.md
```

---

## Build

Standard debug/release build:

```powershell
dotnet build .\src\XenusDt1Decompiler\XenusDt1Decompiler.csproj -c Release
```

Self-contained single-exe publish (recommended for distribution):

```powershell
dotnet publish .\src\XenusDt1Decompiler\XenusDt1Decompiler.csproj -c Release -r win-x86 --self-contained true
```

The executable ends up in `bin\Release\net8.0-windows\win-x86\publish\`. Any other runtime (e.g. `win-x64`) works as well.

---

## Automatic release (GitHub Actions)

Releases are published by `.github/workflows/release.yml`. To release a new version:

1. Bump `<Version>` in `src/XenusDt1Decompiler/XenusDt1Decompiler.csproj` (for example `3.0.1`).
2. Add a `### v3.0.1` section to the changelog in `README.md`. It becomes the release notes.
3. Push to `main`.

On every push to `main`, the workflow checks whether the tag `v<Version>` already exists. If it does not, the workflow builds the project, downloads the latest `texconv.exe` from the [DirectXTex releases](https://github.com/microsoft/DirectXTex/releases), creates the tag and a GitHub Release with a `win-x86.zip` archive, and mirrors the release to [xenus-dt1-decompiler-release-repo](https://github.com/CodeNoob53/xenus-dt1-decompiler-release-repo). Pushes that do not change the version publish nothing.

Pushing a `v*` tag manually, or running the workflow by hand from the Actions tab, also publishes a release.

Mirroring needs a repository secret `RELEASE_REPO_TOKEN`: a fine-grained personal access token limited to the release repo, with **Contents: Read and write** permission. Without it, the release is created only in this repository.

The zip extracts into a single named folder:

```text
xenus-dt1-decompiler-vX.X.X/
├── xenus-dt1-decompiler.exe
├── texconv.exe
├── app.ico
├── run_decompiler.bat
└── README.txt
```

---

## Usage

The utility supports two modes: **Graphical User Interface (GUI)** and **Command-Line Interface (CLI)**.

### GUI Mode

Run `xenus-dt1-decompiler.exe` by double-clicking it (without arguments). A window will appear allowing you to:

1. Select the input directory containing `.DT1` / `.DT2` files.
2. Select the output directory.
3. Choose the output format (`Auto`, `dds`, `tga`, `bmp`, `png`, `jpg`).
4. Click **START DECOMPILE** and watch the progress in the log window.

**Auto format** — detects the real format from magic bytes and saves with the correct extension.
**Explicit format** — converts the decompressed DDS to the chosen format via [texconv](https://github.com/microsoft/DirectXTex/wiki/Texconv) (bundled in the release archive).

### CLI Mode

For automation or batch-script usage:

```text
xenus-dt1-decompiler.exe <input> [output_dir] [format]
```

| Argument | Default | Description |
|---|---|---|
| `input` | — | Path to a `.DT1`/`.DT2` file **or** a directory to scan recursively |
| `output_dir` | same as input | Directory to write decoded files into |
| `format` | auto (from magic bytes) | Force output format: `dds`, `tga`, `bmp`, `png`, `jpg` |
For backward compatibility, a `*.dll` path in the third position (the old `veloader_path` argument) and a trailing `materials_dir` argument are accepted and ignored.

#### Examples (CLI)

**Decode a single texture file (auto format):**
```powershell
.\xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES\GRASS_TGA.DT1" ".\out_tex"
```

**Batch-decode all textures:**
```powershell
.\xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES" ".\out_tex"
```

**Batch-decode and convert to TGA:**
```powershell
.\xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES" ".\out_tex" tga
```

The directory structure relative to `input` is reproduced in `output_dir`.

---

## Exit codes

| Code | Meaning |
|---|---|
| 0 | All files decoded successfully |
| 1 | One or more files failed |
| 2 | Invalid arguments (or help display) |
| 4 | No DT1/DT2 files found in the input directory |
| 5 | Input path does not exist |
| 6 | Cannot derive output directory from input path |

---

## Changelog

### v3.0.0
- **No more `VELoader.dll`:** The DT1/DT2 decompressor was reverse-engineered and reimplemented in managed code (zlib with the first 16 bytes XOR `0x6E`). The tool no longer loads any game DLL, works with any copy of the game, and is no longer limited to 32-bit (x86) builds. Output is byte-identical to the VELoader-based versions.
- **Normal maps (completes [#1](https://github.com/CodeNoob53/xenus-dt1-decompiler-release-repo/issues/1)):** Normal maps are now detected from pixel data instead of `.MAT` files. The MATERIALS-based detection missed 32 of the 818 normal maps in the Xenus 2 cache (for example `SAND_2_N`, `CAT_KORNI_N`, `HOB_GALKA_N`, `BUMPMAP/WATER`), and those were still exported with swapped channels.
- **SPECIAL lookup tables:** `DXT1CTRL` and `R5G6R5TABLE` are now exported losslessly to `tga`/`png`/`bmp`. The v2.2.0 decoder kept only the first channel of each table and only slice 0 of the 32-slice `R5G6R5TABLE` volume (about 3% of its data). All channels and slices are now packed into an atlas, and the old image is its top-left block. `texconv` is no longer used for these tables.
- **GUI:** The `VELoader.dll` and `Materials` fields were removed.
- **CLI:** New argument order `<input> [output_dir] [format]`. Old command lines (a `.dll` path in the third position, or a trailing `materials_dir`) still work; those arguments are ignored. Exit code 3 is no longer used.

### v2.2.1
- **Stability:** Safer texconv subprocess launch — failure to start now throws a descriptive error instead of a null-reference crash.
- **UX:** GUI now disables the **Materials** textbox and browse button during processing (previously clickable mid-run).
- **UX:** Clearer log line stating which normal-map detection mode is active (MATERIALS database vs `_N` suffix heuristic).
- **CLI:** Auto-resolved `VELoader.dll` path that does not exist now reports an actionable error asking for an explicit 3rd argument.
- **CLI:** Guarded edge case where an input path at a drive root produced a crash deriving the output directory (now exits with code `6`).
- **CLI:** Console output forced to UTF-8 so Cyrillic filenames no longer corrupt in the log.
- **DDS decoder:** `A16B16G16R16F` decoder now validates payload size upfront instead of silently truncating.
- **Diagnostics:** Temporary `.dds` cleanup failures are logged (previously swallowed silently).
- **Build:** `Version` / `AssemblyVersion` / `FileVersion` now defined in `.csproj` for local builds (CI still overrides from the tag).

### v2.2.0
- **Architecture:** Extracted a reusable `DecompilerCore` layer shared by CLI and GUI — normal map detection, VELoader initialization, and format conversion are now in one place.
- **Materials folder:** GUI adds an explicit **Materials** field; CLI accepts a 5th `materials_dir` argument. Pass `""` to skip MATERIALS auto-detection.
- **Docs:** Added dedicated "Normal map channel correction" section explaining MATERIALS/ auto-discovery.
- **Native Support for SPECIAL LUT Textures:** Added dedicated fallback bitwise decoders for the legacy DirectX 9 FourCC `112` and `113` mathematical index tables used by the Vital Engine (`DXT1CTRL` and `R5G6R5TABLE`). The tool intercepts these files to bypass `texconv` failures, preserving the coordinate indexes losslessly into standard `.tga` files by perfectly mimicking the Noesis viewer extraction logic (bit-shifting to RGBA and masking the Y dimension into the Alpha channel). Reported by [@evgeniy-mapper](https://github.com/evgeniy-mapper).

### v2.1.2
- **Normal map channel fix (regression fix):** v2.1.1 incorrectly applied channel remapping to ALL uncompressed DDS textures, breaking diffuse, HUD, SKY, and other textures. The channel swap is now applied only to textures identified as normal maps. Reported by [@evgeniy-mapper](https://github.com/evgeniy-mapper).
- **MAT-based normal map detection:** The tool now reads `.MAT` material files from the game's `MATERIALS/` directory (and `GrpUnpacker/MATERIALS/` if present) to build an accurate normal map database from `Texture1_BUMP:` entries. Both directories are merged automatically. Falls back to `_N`/`_N_` filename suffix heuristic if no `MATERIALS/` directory is found.

### v2.1.1
- **Normal map pixel swap:** In addition to fixing the DDS pixel format masks, the actual pixel bytes are now reordered so that normal maps render correctly in all viewers (not just tools that respect DDS channel masks).

### v2.1.0
- **Channel order fix:** Normal map DDS textures are now exported with correct RGBA channel order. VELoader outputs them with swapped channels; the decompiler automatically fixes the DDS pixel format masks before saving.
- **Version in title bar:** The application window now shows the version number from the assembly.
- **Supported games:** Confirmed support for Boiling Point: Road to Hell and The Precursors in addition to Xenus 2: White Gold.

### v2.0.0
- **GUI:** Added graphical user interface — run without arguments to open the window.
- **Format detection:** The tool now reads magic bytes from the decompressed data and saves files with the correct extension, ignoring the misleading `_TGA` / `_BMP` filename suffix used by the engine.
- **Format conversion:** When an explicit output format is requested, the decompressed DDS is converted via **[texconv](https://github.com/microsoft/DirectXTex/wiki/Texconv)** (Microsoft DirectXTex, MIT), correctly decoding DXT1/DXT5 compression including the alpha channel. `texconv.exe` is included in the release archive.
- **Error output:** The `LoadLibraryEx 1114` error message is now split across multiple lines for readability.
- **Architecture:** Refactored into `DecompilerCore` (shared logic), `Program` (CLI entry point), and `MainForm` (GUI).

### v1.0.0
- Initial release: CLI-only batch decoder for DT1/DT2 files using VELoader.dll via Windows Native Interop.

---

## For contributors

- Do **not** commit game assets (`.DT1`, `.DT2`, `.dds`, `.bmp`, `.png`, `VELoader.dll`).
- Only source code, documentation, and build scripts belong in the repository.
- See [CONTRIBUTING.md](CONTRIBUTING.md) for commit and PR guidelines.
- License: **MIT** — see [LICENSE](LICENSE).

---

## Credits

- [@evgeniy-mapper](https://github.com/evgeniy-mapper) — reported the normal map channel swap issue (v2.1.0).

---

## Disclaimer

This repository exists for research and interoperability purposes only. The user is solely responsible for compliance with applicable laws and the End User License Agreement of Xenus 2: White Gold when working with game files.
