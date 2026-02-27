# Contributing

## Scope

This repository contains only the decoder tool source and docs.

- Do not commit game binaries/assets (`VELoader.dll`, `*.DT1`, `*.DT2`, `*.dds`, `*.png`).
- Keep changes focused and reproducible.

## Development

Build:

```powershell
dotnet build .\src\XenusDt1Decompiler\XenusDt1Decompiler.csproj -c Release
```

Run:

```powershell
dotnet run --project .\src\XenusDt1Decompiler\XenusDt1Decompiler.csproj -- <input> [output_dir] [veloader_path]
```

## Pull requests

- Describe what changed and why.
- Include exact commands used for validation.
- Keep README usage examples up to date if CLI behavior changes.
