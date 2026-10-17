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
dotnet run --project .\src\XenusDt1Decompiler\XenusDt1Decompiler.csproj -- <input> [output_dir] [format]
```

Test:

```powershell
dotnet test .\tests\XenusDt1Decompiler.Tests
```

Test inputs are generated in code (see `tests/XenusDt1Decompiler.Tests/Fixtures.cs`); don't add game files as fixtures.

## Pull requests

- Describe what changed and why.
- Include exact commands used for validation.
- The CI check (build + tests) must pass.
- Keep README usage examples up to date if CLI behavior changes.
