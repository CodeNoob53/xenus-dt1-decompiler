# 🛸 Xenus DT1 Decompiler

[![Platform](https://img.shields.io/badge/platform-Windows-blue.svg)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Build](https://img.shields.io/badge/.NET-8.0-blueviolet.svg)](https://dotnet.microsoft.com/download)

🇺🇦 **[Українська версія документації (README_UA.md)](README_UA.md)**

A specialized utility for reverse engineering and mass decoding cache textures (`*.DT1` and `*.DT2` formats) of the game **Xenus 2: White Gold**.

## 💡 Why is this approach unique?

Data in `DT1/DT2` formats uses engine-specific **Vital Engine 3** packaging (modified `zlib` with preset dictionaries). Instead of trying to recreate a complex algorithm from scratch, this tool uses **Native Interop** to call the original game component — `VELoader.dll`.

The utility accesses exported functions:
* `GetCLVersion` — to check compatibility.
* `GetUnloadSize` — for precise memory allocation.
* `Unload` — for direct decompression.

## 🛠 Build Requirements

* **OS:** Windows (x86/x64)
* **Runtime:** [.NET SDK 8.0+](https://dotnet.microsoft.com/download/dotnet/8.0)
* **Architecture:** The project must be built for **x86**, as the original DLL is 32-bit.
* **Dependencies:** The `VELoader.dll` file (can be found in the game's root folder or in `GrpUnpacker` archives).

## 📁 Project Structure

```text
xenus-dt1-decompiler/
 ├── src/
 │    └── XenusDt1Decompiler/
 │         ├── XenusDt1Decompiler.csproj
 │         └── Program.cs
 ├── .github/
 │    └── workflows/
 │         └── release.yml
 ├── run_decompiler.bat
 ├── .gitignore
 └── README.md
```

## 🏗 Building

To create the executable, run the following command in the terminal:

```powershell
dotnet build .\src\XenusDt1Decompiler\XenusDt1Decompiler.csproj -c Release
```

To publish the ready binary without extra dependencies:

```powershell
dotnet publish .\src\XenusDt1Decompiler\XenusDt1Decompiler.csproj -c Release -r win-x86 --self-contained false
```

## 🤖 Automatic Release (GitHub Actions)

A pipeline is set up in the repository: `.github/workflows/release.yml`

After creating and pushing a version tag (`git tag v0.1.0`), GitHub Actions will automatically build the project and create a GitHub Release, attaching the `win-x86.zip` archive containing the ready utility and the `run_decompiler.bat` quick start file.

## 🚀 Usage

The program supports both item-by-item processing and mass decoding of entire directories from the command line (including recursive traversal of all subfolders).

### Syntax:

```powershell
.\xenus-dt1-decompiler.exe <path_to_file_or_folder> [output_folder] [path_to_veloader.dll] [format]
```

### Examples:

**1. Decoding a single map tile:**

```powershell
.\xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES\MAP\GROUP_0_0_BMP.DT1" ".\out_tex"
```

**2. Batch processing of all textures (with recursive traversal of subfolders):**

```powershell
.\xenus-dt1-decompiler.exe "C:\Games\Xenus 2\CACHE\TEXTURES" ".\out_tex" "C:\Games\Xenus 2\VELoader.dll"
```

The result will be standard texture files (the format is automatically determined from the original filename, e.g., `.bmp`, `.tga` or `.dds`), which can be opened in image editors. You can force a format as the 4th parameter.

## 📦 For Contributors

* **Game Assets:** It is strictly prohibited to add original game resources (`.DT1`, `.DT2`, `.dds`, `.png`) to the repository.
* **Commits:** Only add source code, documentation, and build scripts.
* **More info:** See the **[CONTRIBUTING.md](CONTRIBUTING.md)**.
* **License:** `MIT`. See the **[LICENSE](LICENSE)** file.

---

## ⚖️ Disclaimer

This repository was created solely for research purposes and compatibility. The user bears full responsibility for complying with local laws and the End User License Agreement (EULA) of the original game when working with game assets.
