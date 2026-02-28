# Xenus DT1 Decompiler

[![Platform](https://img.shields.io/badge/platform-Windows-blue.svg)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Build](https://img.shields.io/badge/.NET-8.0-blueviolet.svg)](https://dotnet.microsoft.com/download)

**[Українська версія (README_UA.md)](README_UA.md)**

A tool for batch-decoding the compressed texture cache of **Xenus 2: White Gold** (`*.DT1` / `*.DT2` files) into standard image formats (`.dds`, `.bmp`, `.tga`, etc.) while preserving the original directory structure.

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

## DT1 / DT2 file format

Each `.DT1` or `.DT2` file is a single compressed asset. The 8-byte header is followed immediately by the compressed payload:

```
Offset  Size  Description
0       3     Uncompressed size, little-endian 24-bit
3       1     Flags (0x50 in all observed files)
4       3     Compressed payload size, little-endian 24-bit
7       1     Flags (0x08 in all observed files)
8       N     Compressed payload (VE3 zlib, starts with 0x16 0x30)
```

The output format is inferred from the original filename. Names follow the convention `<base>_<EXT>.DT1`, where `EXT` is the real format (e.g. `GRASS_TGA.DT1` → `GRASS.tga`, `GROUP_4_0_BMP.DT1` → `GROUP_4_0.bmp`). You can override this with the optional `[format]` argument.

> **Note:** This tool handles `.DT1` / `.DT2` files only. `CE#$` archives (e.g. `CACHE/*.DAT`) have a different structure — the format is fully documented and `CE#$` support may be integrated into a future version of this tool once finalized.

---

## Requirements

- **OS:** Windows (x86 or x64 host)
- **Runtime:** [.NET SDK 8.0+](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Architecture:** must publish as **win-x86** — `VELoader.dll` is a 32-bit DLL
- **VELoader.dll:** from the game's root folder or extracted from a `.grp` archive

---

## Project structure

```
xenus-dt1-decompiler/
├── src/
│   └── XenusDt1Decompiler/
│       ├── XenusDt1Decompiler.csproj
│       └── Program.cs
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

The executable ends up in `bin\Release\net8.0\win-x86\publish\`.

---

## Automatic release (GitHub Actions)

The workflow at `.github/workflows/release.yml` triggers on a version tag push. It builds the project and creates a GitHub Release with the `win-x86.zip` archive (executable + `run_decompiler.bat`):

```powershell
git tag v1.0.0
git push origin v1.0.0
```

---

## Usage

```
xenus-dt1-decompiler.exe <input> [output_dir] [veloader_path] [format]
```

| Argument | Default | Description |
|---|---|---|
| `input` | — | Path to a `.DT1`/`.DT2` file **or** a directory to scan recursively |
| `output_dir` | same as input | Directory to write decoded files into |
| `veloader_path` | auto-search | Explicit path to `VELoader.dll` |
| `format` | from filename | Force output extension, e.g. `dds`, `bmp`, `tga` |

If `veloader_path` is omitted, the tool looks for `VELoader.dll` in the current directory, `.\GrpUnpacker\`, and `..\`.

### Examples

**Decode a single texture file:**

```powershell
.\xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES\GRASS_TGA.DT1" ".\out_tex"
```

**Batch-decode all textures, auto-detect VELoader:**

```powershell
.\xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES" ".\out_tex"
```

**Batch-decode and force DDS output:**

```powershell
.\xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES" ".\out_tex" "C:\Games\Xenus 2\VELoader.dll" dds
```

The directory structure relative to `input` is reproduced in `output_dir`.

### Console output

```
[OK]   path\to\file.DT1 -> out\file.bmp  (49208 bytes, sig='BM..', ver=0x200, hdrUnc=49200, apiUnc=49200)
[FAIL] path\to\broken.DT1 : Unload returned 0 (...)
Done. OK=308, FAIL=2
```

---

## Exit codes

| Code | Meaning |
|---|---|
| 0 | All files decoded successfully |
| 1 | One or more files failed |
| 2 | No arguments provided |
| 3 | `VELoader.dll` not found |
| 4 | No DT1/DT2 files found in the input directory |
| 5 | Input path does not exist |

---

## For contributors

- Do **not** commit game assets (`.DT1`, `.DT2`, `.dds`, `.bmp`, `.png`, `VELoader.dll`).
- Only source code, documentation, and build scripts belong in the repository.
- See [CONTRIBUTING.md](CONTRIBUTING.md) for commit and PR guidelines.
- License: **MIT** — see [LICENSE](LICENSE).

---

## Disclaimer

This repository exists for research and interoperability purposes only. The user is solely responsible for compliance with applicable laws and the End User License Agreement of Xenus 2: White Gold when working with game files.
