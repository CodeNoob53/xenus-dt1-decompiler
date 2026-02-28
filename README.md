# Xenus DT1 Decompiler

[![Platform](https://img.shields.io/badge/platform-Windows-blue.svg)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Build](https://img.shields.io/badge/.NET-8.0-blueviolet.svg)](https://dotnet.microsoft.com/download)
[![Version](https://img.shields.io/badge/version-2.0.0-orange.svg)](CHANGELOG.md)

**[Українська версія (README_UA.md)](README_UA.md)**

A tool for batch-decoding the compressed texture cache of **Xenus 2: White Gold** (`*.DT1` / `*.DT2` files) into standard image formats (`.dds`, `.tga`, `.png`, etc.) while preserving the original directory structure.

---

## Why this approach

The game engine (**Vital Engine 3**) uses a modified zlib with proprietary preset dictionaries. Reimplementing the decompressor from scratch is impractical because the dictionary is embedded deep inside the engine. Instead, the tool loads the original `VELoader.dll` from the game and calls its exported API via Windows Native Interop:

| Export | Purpose |
|---|---|
| `GetCLVersion` | Version/compatibility check |
| `GetUnloadSize` | Query exact uncompressed size |
| `Unload` | Decompress the payload into a buffer |

This way the decompression is bit-perfect and requires zero knowledge of the dictionary internals.

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
0       3     Uncompressed size, little-endian 24-bit
3       1     Flags (0x50 in all observed files)
4       3     Compressed payload size, little-endian 24-bit
7       1     Flags (0x08 in all observed files)
8       N     Compressed payload (VE3 zlib, starts with 0x16 0x30)
```

> **Note:** This tool handles `.DT1` / `.DT2` files only. `CE#$` archives (e.g. `CACHE/*.DAT`) have a different structure — the format is fully documented and `CE#$` support may be integrated into a future version of this tool once finalized.

---

## Requirements

- **OS:** Windows (x86 or x64 host)
- **Runtime:** [.NET SDK 8.0+](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Architecture:** must publish as **win-x86** — `VELoader.dll` is a 32-bit DLL
- **VELoader.dll:** `VELoader.dll` often fails to initialize with pirated copies of the game (error `LoadLibraryEx 1114`). Use the `VELoader.dll` from the **Steam release**, **Vital Engine SDK**, or the **GrpUnpacker** utility.

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

The executable ends up in `bin\Release\net8.0-windows\win-x86\publish\`.

---

## Automatic release (GitHub Actions)

The workflow at `.github/workflows/release.yml` triggers on a version tag push. It builds the project, downloads the latest `texconv.exe` from the [DirectXTex releases](https://github.com/microsoft/DirectXTex/releases), and creates a GitHub Release with the `win-x86.zip` archive (executable + `texconv.exe` + `run_decompiler.bat`):

```powershell
git tag v2.0.0
git push origin v2.0.0
```

---

## Usage

The utility supports two modes: **Graphical User Interface (GUI)** and **Command-Line Interface (CLI)**.

### GUI Mode

Run `xenus-dt1-decompiler.exe` by double-clicking it (without arguments). A window will appear allowing you to:

1. Select the input directory containing `.DT1` / `.DT2` files.
2. Select the output directory.
3. Optionally select `VELoader.dll` (auto-detected if not specified).
4. Choose the output format (`Auto`, `dds`, `tga`, `bmp`, `png`, `jpg`).
5. Click **START DECOMPILE** and watch the progress in the log window.

**Auto format** — detects the real format from magic bytes and saves with the correct extension.
**Explicit format** — converts the decompressed DDS to the chosen format via [texconv](https://github.com/microsoft/DirectXTex/wiki/Texconv) (bundled in the release archive).

### CLI Mode

For automation or batch-script usage:

```text
xenus-dt1-decompiler.exe <input> [output_dir] [veloader_path] [format]
```

| Argument | Default | Description |
|---|---|---|
| `input` | — | Path to a `.DT1`/`.DT2` file **or** a directory to scan recursively |
| `output_dir` | same as input | Directory to write decoded files into |
| `veloader_path` | auto-search | Explicit path to `VELoader.dll` |
| `format` | auto (from magic bytes) | Force output format: `dds`, `tga`, `bmp`, `png`, `jpg` |

If `veloader_path` is omitted, the tool searches for `VELoader.dll` in: current directory → `.\GrpUnpacker\` → `..\GrpUnpacker\` → `..\`.

#### Examples (CLI)

**Decode a single texture file (auto format):**
```powershell
.\xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES\GRASS_TGA.DT1" ".\out_tex"
```

**Batch-decode all textures, auto-detect VELoader:**
```powershell
.\xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES" ".\out_tex"
```

**Batch-decode and convert to TGA:**
```powershell
.\xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES" ".\out_tex" "C:\Games\Xenus 2\VELoader.dll" tga
```

The directory structure relative to `input` is reproduced in `output_dir`.

---

## Exit codes

| Code | Meaning |
|---|---|
| 0 | All files decoded successfully |
| 1 | One or more files failed |
| 2 | Invalid arguments (or help display) |
| 3 | `VELoader.dll` not found |
| 4 | No DT1/DT2 files found in the input directory |
| 5 | Input path does not exist |

---

## Changelog

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

## Disclaimer

This repository exists for research and interoperability purposes only. The user is solely responsible for compliance with applicable laws and the End User License Agreement of Xenus 2: White Gold when working with game files.
