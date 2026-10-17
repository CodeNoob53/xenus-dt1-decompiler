# Xenus DT1 Decompiler

[![Platform](https://img.shields.io/badge/platform-Windows-blue.svg)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Build](https://img.shields.io/badge/.NET-8.0-blueviolet.svg)](https://dotnet.microsoft.com/download)
[![Version](https://img.shields.io/badge/version-3.0.0-orange.svg)](CHANGELOG.md)

**[Українська версія (README_UA.md)](README_UA.md)**

A tool for batch-decoding the compressed texture cache of **Xenus 2: White Gold**, **Boiling Point: Road to Hell**, and **The Precursors** (`*.DT1` / `*.DT2` files) into standard image formats (`.dds`, `.tga`, `.png`, etc.) while preserving the original directory structure.

---

## Supported games

| Game | Status |
|---|---|
| Xenus 2: White Gold | ✅ Tested on the whole cache (3829 textures) |
| Boiling Point: Road to Hell | ✅ Tested on the whole cache (2603 textures), since v3.0.2 |
| The Precursors | ⚠️ Not tested since v3.0 ([#4](https://github.com/CodeNoob53/xenus-dt1-decompiler/issues/4)) |

Up to v2.x the decoding was done by the game's own `VELoader.dll`, so the tool worked with every game whose DLL it was given, The Precursors included. Since v3.0 it uses its own decoder, rebuilt from `VELoader.dll` (see below). On Xenus 2 and Boiling Point its output is byte-identical to the DLL's; The Precursors runs on the same engine but hasn't been checked yet.

---

## How it works

Starting from v3.0.0 the tool **no longer needs `VELoader.dll`**. The `Unload` routine of the engine's `VELoader.dll` was reverse-engineered and reimplemented in managed code. The "proprietary" compression turned out to be two standard algorithms with the first 16 bytes of the stream XOR-ed with `0x6E`: **zlib 1.2.2** (Xenus 2, decoded with .NET's `ZLibStream`) and **UCL NRV2E** (Boiling Point, decoded by a built-in decompressor). See [DT1 / DT2 file format](#dt1--dt2-file-format).

The output is byte-identical to the original `VELoader.dll` on 3857 Xenus 2 files and all 2603 Boiling Point files, and all 21 921 DT1/DT2 files of Xenus 2 decode with a matching size and zlib Adler-32 checksum.

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
| 1..3 | UCL NRV2E stream (8-bit variant). Methods 2 and 3 first XOR the first `min(16, N)` bytes with `0x6E`; method 1 doesn't |
| 4..6 | First `min(16, N)` bytes XOR `0x6E`, then a standard zlib stream (`0x16 0x30` → `0x78 0x5E`) |
| 7 | Same as 4..6; if the payload is ≥ 256 bytes it is additionally encrypted with a key table (unsupported, only `resource.DT1` uses it) |

Xenus 2 uses method 5; Boiling Point uses method 2.

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

  `DXT1CTRL` becomes a 256×256 image. `R5G6R5TABLE` becomes 64×2048 (2 channel pairs × 32 slices). The top-left block (slice 0, channels 0–1) looks the same as in Noesis. This atlas is **for viewing only**: the alpha channel holds data (row 0 of `DXT1CTRL` is fully transparent), so image editors can damage it, and `jpg` is lossy. To put a table back into the game, use the `.dds` from Auto or `dds` mode. The log reminds you of this when a table is exported to an 8-bit format.

---

## Normal map channel correction

The engine stores normal map textures with a non-standard channel layout. The tool remaps pixel bytes to standard RGBA so normal maps render correctly in all viewers.

In the cache, an uncompressed normal map is stored as `A8R8G8B8` with the vector swizzled across the bytes: `B=Y`, `G=Z`, `R=0`, `A=X`. The tool remaps it to `R=X`, `G=Y`, `B=Z`, `A=255`.

Normal maps are identified from the pixel data itself, with no file names or `.MAT` files involved: the R byte is `0` in every pixel and Z averages ≥ 128. In the Xenus 2 cache this matches all 818 normal maps, which are exactly the textures that also have a `.DT3`, and none of the other uncompressed textures (sky, menus, diffuse).

---

## Requirements

- **To run a release:** 64-bit Windows. Nothing else: the release exe is self-contained (the .NET runtime is bundled), and no game DLLs are needed.
- **To build:** [.NET SDK 8.0+](https://dotnet.microsoft.com/download/dotnet/8.0)

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
├── tests/
│   └── XenusDt1Decompiler.Tests/   ← xUnit tests, synthetic fixtures only
├── dist/                       ← release assets (bundled into the zip)
│   ├── app.ico
│   └── README.txt
├── .github/
│   └── workflows/
│       ├── ci.yml              ← build + tests on pull requests
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

Release build: one self-contained, compressed exe (~64 MB) that runs without an installed .NET runtime. The csproj enables this whenever a runtime identifier is given:

```powershell
dotnet publish .\src\XenusDt1Decompiler\XenusDt1Decompiler.csproj -c Release -r win-x64 -o .\publish
```

## Tests

```powershell
dotnet test .\tests\XenusDt1Decompiler.Tests
```

The tests cover unpacking (all compression methods, including an NRV2E encoder for synthetic method 1–3 streams), error handling, normal map detection and remapping, and the lossless lookup-table atlas. They build their inputs in code, so no game files are needed. The CI workflow runs them on every pull request.

---

## Automatic release (GitHub Actions)

Releases are published by `.github/workflows/release.yml`. To release a new version:

1. Bump `<Version>` in `src/XenusDt1Decompiler/XenusDt1Decompiler.csproj` (for example `3.0.1`).
2. Add a `### v3.0.1` section to the changelog in `README.md`. It becomes the release notes.
3. Merge it into `main` through a pull request (direct pushes to `main` are blocked by a ruleset).

On every push to `main` (including merged pull requests), the workflow checks whether the tag `v<Version>` already exists. If it does not, the workflow builds the project, downloads the latest `texconv.exe` from the [DirectXTex releases](https://github.com/microsoft/DirectXTex/releases), creates the tag and a GitHub Release with a `win-x64.zip` archive, and mirrors the release to [xenus-dt1-decompiler-release-repo](https://github.com/CodeNoob53/xenus-dt1-decompiler-release-repo). Pushes that do not change the version publish nothing.

Pushing a `v*` tag manually, or running the workflow by hand from the Actions tab, also publishes a release.

Mirroring needs a repository secret `RELEASE_REPO_TOKEN`: a fine-grained personal access token limited to the release repo, with **Contents: Read and write** permission. Without it, the release is created only in this repository.

The zip extracts into a single named folder:

```text
xenus-dt1-decompiler-vX.X.X/
├── xenus-dt1-decompiler.exe
├── texconv.exe
├── run_decompiler.bat
└── README.txt
```

---

## Usage

The utility supports two modes: **Graphical User Interface (GUI)** and **Command-Line Interface (CLI)**.

### GUI Mode

Run `xenus-dt1-decompiler.exe` without arguments.

1. **Input**: a folder with `.DT1` files (subfolders are included) or a single `.DT1` file. You can also drop a folder or a file onto the window; a dropped file is processed on its own.
2. **Output**: where to save the textures. Defaults to `out_tex` next to the exe.
3. **Format**: **Auto** saves each texture in the format it had before the engine cached it (`_TGA` → `.tga`), which is what the game expects when you put a texture back. **DDS** keeps the texture exactly as stored in the cache. The other formats are converted with [texconv](https://github.com/microsoft/DirectXTex/wiki/Texconv) (bundled in the release archive). Hover over an option for details; the line under the switch describes the selected one.
4. Click **Start**. The progress bar and the log show each file. **Cancel** stops the run, and **Open output folder** opens the result.

The log shows one line per file, warnings in yellow and failures in red, and ends with a summary that lists the failed files. The input, output and format are remembered between runs (in `%AppData%\xenus-dt1-decompiler\settings.json`). There are two themes, **Mono** (monochrome) and **White Gold** (warm stone with a gold accent); pick one with the round swatches at the right of the Format row. Both follow the Windows light or dark mode.

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

### v3.1.0
- **New GUI:**
  - a progress bar, a **Cancel** button and an **Open output folder** button;
  - a dropped `.DT1` file is processed on its own instead of its whole folder;
  - the output defaults to `out_tex` next to the exe, and the paths and format are remembered between runs;
  - **Browse…** opens at the current path;
  - the format dropdown is replaced by a switch (Auto · DDS · TGA · PNG · BMP · JPG) with a tooltip on each option and a description of the selected one;
  - two themes, Mono and White Gold, picked with round swatches next to the format switch; both follow the Windows light or dark mode and use the app's own buttons, fields and progress bar, so text stays readable in every state (stock disabled buttons drew near-black text on the dark background);
  - restrained motion: the switch's selection slides to the chosen format, the counter and the progress line move smoothly, and the line shows a running segment while files are collected; all of it is off when Windows animation effects are off.
- **Quieter log** (GUI and CLI): one line per file instead of three, colour-coded warnings and failures in the GUI, and a final summary with the elapsed time and the list of failed files.
- **Lookup tables:** exporting `DXT1CTRL` / `R5G6R5TABLE` to an 8-bit format logs that the atlas is for viewing only (a warning for `jpg`).
- **Faster batch processing:** files are decoded and converted in parallel on all CPU cores. On the full Xenus 2 cache (3829 files, 12 threads), Auto mode drops from ~199 s to ~60 s and `dds` mode from ~25 s to ~6 s; Boiling Point in `dds` mode from ~6.5 s to ~2 s. Output is byte-identical to v3.0.x, except for the fix below.
- **No more lost textures when names clash:** `SKY00_BMP.DT1` and `SKY00_TGA.DT1` are different textures, but in `dds` or a forced format both used to be written to `SKY00.dds`, so one of them was silently overwritten (10 sky textures in Xenus 2). Textures whose output names would clash now keep their format suffix: `SKY00_BMP.dds`, `SKY00_TGA.dds`.
- **Safer temporary files:** texconv now works in its own temporary folder, so a temporary `NAME.dds` can no longer overwrite or delete a real `NAME.dds` in the output folder.

### v3.0.2
- **Boiling Point works again:** Its whole texture cache uses compression method 2, which v3.0.0 and v3.0.1 did not support. All 2603 Boiling Point textures failed. Methods 1–3 are now decoded: a UCL NRV2E decompressor reversed from `VELoader.dll`. The output is byte-identical to the VELoader-based v2.2 build on all 2603 files. Xenus 2 output is unchanged.

### v3.0.1
- **No .NET installation needed:** The release is now a self-contained 64-bit exe. v3.0.0 was a framework-dependent 32-bit build and did not start without the .NET 8 Desktop Runtime **x86**, which most PCs don't have. The zip is now `...-win-x64.zip` (~64 MB). The x86 target has no use any more: it was only needed for the 32-bit `VELoader.dll`, and the bundled `texconv.exe` is 64-bit.
- The window icon is loaded from the exe itself.

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
