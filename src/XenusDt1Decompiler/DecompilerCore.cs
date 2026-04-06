using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace XenusDt1Decompiler
{
    public static class DecompilerCore
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint GetCLVersionFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetUnloadSizeFn(IntPtr packed);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int UnloadFn(IntPtr packed, IntPtr unpacked, int unpackedSize);

        private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr LoadLibraryExA(string lpLibFileName, IntPtr hFile, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll")]
        private static extern bool FreeLibrary(IntPtr hModule);

        public static bool IsDt1OrDt2(string path)
        {
            return path.EndsWith(".DT1", StringComparison.OrdinalIgnoreCase);
        }

        public static string ResolveDefaultVELoader()
        {
            string cwd = Directory.GetCurrentDirectory();
            string[] candidates =
            {
                // First, check if the user placed it next to our exe (highest priority)
                Path.Combine(cwd, "VELoader.dll"),

                // Then check GrpUnpacker folder (since we know this version works)
                Path.Combine(cwd, "GrpUnpacker", "VELoader.dll"),
                Path.Combine(cwd, "..", "GrpUnpacker", "VELoader.dll"),

                // Last resort
                Path.Combine(cwd, "..", "VELoader.dll"),
            };

            foreach (var c in candidates)
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(full))
                {
                    return full;
                }
            }

            return Path.Combine(cwd, "VELoader.dll");
        }

        // Builds a set of normal map texture base names (case-insensitive, no extension, no path)
        // by scanning all .MAT files in the given MATERIALS directories for "Texture1_BUMP:" lines.
        // Merges results from all provided directories (game patch overrides + base GRP).
        // Returns null if none of the directories exist.
        public static HashSet<string>? BuildNormalMapSet(IEnumerable<string> materialsDirs, Action<string>? logInfo = null)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scanned = new List<string>();

            foreach (var dir in materialsDirs)
            {
                if (!Directory.Exists(dir)) continue;
                scanned.Add(dir);
                foreach (var matFile in Directory.EnumerateFiles(dir, "*.MAT", SearchOption.AllDirectories))
                {
                    foreach (var line in File.ReadLines(matFile))
                    {
                        var trimmed = line.AsSpan().TrimStart();
                        if (!trimmed.StartsWith("Texture1_BUMP:", StringComparison.OrdinalIgnoreCase))
                            continue;
                        int colon = line.IndexOf(':');
                        if (colon < 0) continue;
                        var value = line.AsSpan(colon + 1).Trim();
                        if (value.IsEmpty) continue;
                        // value is like "Animals\Cos_Krab_n.tga" — take filename stem only
                        var stem = Path.GetFileNameWithoutExtension(value.ToString());
                        if (!string.IsNullOrEmpty(stem))
                            set.Add(stem);
                    }
                }
            }

            if (scanned.Count == 0) return null;
            var labels = scanned.Select(d => Path.GetFileName(Path.GetDirectoryName(d)) + "/" + Path.GetFileName(d));
            logInfo?.Invoke($"[INFO] Normal map database: {set.Count} textures from {string.Join(" + ", labels)}");
            return set;
        }

        // Finds all MATERIALS directories relative to veloaderPath or inputPath.
        // Collects both the base GRP data (GrpUnpacker/MATERIALS) and game patch overrides (MATERIALS/).
        // Results are ordered: patch/game-root first (higher priority), GrpUnpacker second.
        public static List<string> ResolveMaterialsDirs(string veloaderPath, string inputPath)
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddRoot(string? r) { if (r is not null) roots.Add(r); }

            AddRoot(Path.GetDirectoryName(veloaderPath));
            AddRoot(Path.GetDirectoryName(inputPath));
            // Walk up: CACHE/TEXTURES → CACHE → game root
            foreach (var r in roots.ToList())
            {
                AddRoot(Path.GetDirectoryName(r));
                AddRoot(Path.GetDirectoryName(Path.GetDirectoryName(r) ?? ""));
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            void TryAdd(string path) { if (Directory.Exists(path) && seen.Add(path)) result.Add(path); }

            foreach (var root in roots)
            {
                // Direct MATERIALS/ in root (game patches, higher priority)
                TryAdd(Path.Combine(root, "MATERIALS"));
                // GrpUnpacker/MATERIALS (base GRP data)
                TryAdd(Path.Combine(root, "GrpUnpacker", "MATERIALS"));
            }
            return result;
        }

        // Overload without explicit materialsPath — auto-discovers from veloaderPath/inputPath.
        public static (int Ok, int Fail) DecodeDirectory(
            string inputPath,
            string outputRoot,
            string veloaderPath,
            string? userExt,
            Action<string> logInfo,
            Action<string> logError) =>
            DecodeDirectory(inputPath, outputRoot, veloaderPath, userExt, null, logInfo, logError);

        public static (int Ok, int Fail) DecodeDirectory(
            string inputPath,
            string outputRoot,
            string veloaderPath,
            string? userExt,
            string? explicitMaterialsPath,
            Action<string> logInfo,
            Action<string> logError)
        {
            var files = Directory.EnumerateFiles(inputPath, "*.DT*", SearchOption.AllDirectories)
                .Where(IsDt1OrDt2)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                logError($"No DT1/DT2 files found in: {inputPath}");
                return (0, 0);
            }

            string? texconvPath = ResolveTexconv();
            if (texconvPath is null)
                logError("[WARN] texconv.exe not found — format conversion unavailable, files will be saved as .dds.");

            // Build normal map database: explicit path takes priority, then auto-discover, then _N suffix.
            HashSet<string>? normalMapSet;
            if (!string.IsNullOrWhiteSpace(explicitMaterialsPath) && Directory.Exists(explicitMaterialsPath))
            {
                normalMapSet = BuildNormalMapSet(new[] { explicitMaterialsPath }, logInfo);
            }
            else
            {
                var materialsDirs = ResolveMaterialsDirs(veloaderPath, inputPath);
                normalMapSet = BuildNormalMapSet(materialsDirs, logInfo);
            }
            if (normalMapSet is null)
                logInfo("[INFO] MATERIALS directory not found — using _N suffix heuristic for normal map detection.");

            int ok = 0;
            int fail = 0;
            foreach (var file in files)
            {
                try
                {
                    if (DecodeOneFile(file, outputRoot, veloaderPath, inputPath, userExt, texconvPath, normalMapSet, logInfo, logError))
                    {
                        ok++;
                    }
                    else
                    {
                        fail++;
                    }
                }
                catch (InvalidOperationException)
                {
                    // This is thrown when VELoader initialization critically fails (e.g. error 1114)
                    // We stop processing further files to avoid a cascade of identical errors
                    break;
                }
            }

            logInfo($"Done. OK={ok}, FAIL={fail}");
            return (ok, fail);
        }

        public static bool DecodeOneFile(
            string filePath,
            string outputRoot,
            string veloaderPath,
            string? inputRoot,
            string? userExt,
            string? texconvPath,
            Action<string> logInfo,
            Action<string> logError) =>
            DecodeOneFile(filePath, outputRoot, veloaderPath, inputRoot, userExt, texconvPath, null, logInfo, logError);

        public static bool DecodeOneFile(
            string filePath,
            string outputRoot,
            string veloaderPath,
            string? inputRoot,
            string? userExt,
            string? texconvPath,
            HashSet<string>? normalMapSet,
            Action<string> logInfo,
            Action<string> logError)
        {
            byte[] packed;
            try
            {
                packed = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                logError($"[FAIL] {filePath} : read error: {ex.Message}");
                return false;
            }

            if (packed.Length < 8)
            {
                logError($"[FAIL] {filePath} : too small ({packed.Length})");
                return false;
            }

            int headerUnc = packed[0] | (packed[1] << 8) | (packed[2] << 16);
            int headerComp = packed[4] | (packed[5] << 8) | (packed[6] << 16);
            int flags = packed[7];

            IntPtr mod = LoadLibraryExA(veloaderPath, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
            if (mod == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == 1114) // ERROR_DLL_INIT_FAILED
                {
                    logError($"[FAIL] {filePath} : LoadLibraryEx failed (1114).");
                    logError("VELoader.dll often fails to initialize with pirated copies.");
                    logError("Please use the VELoader.dll from the Steam release, Vital Engine SDK, or GrpUnpacker.");
                    throw new InvalidOperationException("VELoader.dll initialization failed (1114). Processing stopped.");
                }
                else
                {
                    logError($"[FAIL] {filePath} : LoadLibraryEx failed ({err}) for {veloaderPath}");
                }
                return false;
            }

            try
            {
                var pGetVer = GetProcAddress(mod, "GetCLVersion");
                var pGetUnc = GetProcAddress(mod, "GetUnloadSize");
                var pUnload = GetProcAddress(mod, "Unload");

                if (pGetVer == IntPtr.Zero || pGetUnc == IntPtr.Zero || pUnload == IntPtr.Zero)
                {
                    logError($"[FAIL] {filePath} : required exports not found");
                    return false;
                }

                var getVer = Marshal.GetDelegateForFunctionPointer<GetCLVersionFn>(pGetVer);
                var getUnloadSize = Marshal.GetDelegateForFunctionPointer<GetUnloadSizeFn>(pGetUnc);
                var unload = Marshal.GetDelegateForFunctionPointer<UnloadFn>(pUnload);

                uint ver;
                try
                {
                    ver = getVer();
                }
                catch (Exception ex)
                {
                    logError($"[FAIL] {filePath} : GetCLVersion call failed: {ex.Message}");
                    return false;
                }

                int outSize;
                int apiUnc;
                using (var pinPacked = new Pinned(packed))
                {
                    try
                    {
                        apiUnc = getUnloadSize(pinPacked.Ptr);
                    }
                    catch
                    {
                        apiUnc = 0;
                    }

                    outSize = ChooseOutputSize(headerUnc, apiUnc);
                    var unpacked = new byte[outSize];
                    using var pinOut = new Pinned(unpacked);

                    int status;
                    try
                    {
                        status = unload(pinPacked.Ptr, pinOut.Ptr, unpacked.Length);
                    }
                    catch (Exception ex)
                    {
                        logError($"[FAIL] {filePath} : Unload call failed: {ex.Message}");
                        return false;
                    }

                    if (status <= 0)
                    {
                        logError($"[FAIL] {filePath} : Unload returned {status} (ver=0x{ver:x}, hdrUnc={headerUnc}, apiUnc={apiUnc}, hdrComp24={headerComp}, flags=0x{flags:x2})");
                        return false;
                    }

                    int writeLen = apiUnc > 0 && apiUnc <= unpacked.Length ? apiUnc : unpacked.Length;
                    var outRelDir = inputRoot is null
                        ? ""
                        : Path.GetDirectoryName(Path.GetRelativePath(inputRoot, filePath)) ?? "";

                    var rawData = unpacked.AsSpan(0, writeLen).ToArray();
                    var nameInfo = ParseFileNameAndExtension(filePath, userExt);
                    var realExt = DetectRealExtension(rawData);

                    // Determine final extension:
                    // - user forced a format → use it
                    // - auto mode → use the format hint from the filename suffix (_TGA, _BMP, etc.)
                    //              if no recognisable suffix → fall back to the real detected format
                    string finalExt;
                    if (!string.IsNullOrEmpty(userExt))
                    {
                        finalExt = "." + userExt.TrimStart('.');
                    }
                    else
                    {
                        var hintExt = nameInfo.ext.ToLowerInvariant();
                        finalExt = hintExt is ".tga" or ".bmp" or ".png" or ".jpg" or ".jpeg" or ".dds"
                            ? hintExt
                            : realExt;
                    }

                    var newFileName = nameInfo.basePath + finalExt;
                    var outPath = Path.Combine(outputRoot, outRelDir, newFileName);
                    var outDir = Path.GetDirectoryName(outPath)!;
                    Directory.CreateDirectory(outDir);

                    bool isNormalMap = normalMapSet is not null
                        ? IsNormalMapBySet(filePath, normalMapSet)
                        : IsNormalMapBySuffix(filePath);
                    bool converted = false;
                    if (texconvPath is not null
                        && !finalExt.Equals(realExt, StringComparison.OrdinalIgnoreCase))
                    {
                        // texconv needs a DDS file on disk; write a temp one then convert
                        var tmpDds = Path.ChangeExtension(outPath, ".dds");
                        var ddsData = (realExt == ".dds" && isNormalMap) ? FixNormalMapChannels(rawData) : rawData;
                        File.WriteAllBytes(tmpDds, ddsData);
                        converted = TryConvertWithTexconv(texconvPath, tmpDds, outPath, logError);
                        if (converted && !tmpDds.Equals(outPath, StringComparison.OrdinalIgnoreCase))
                            File.Delete(tmpDds);
                    }

                    if (!converted)
                    {
                        // Save with real extension (no conversion needed or conversion failed)
                        var actualPath = Path.ChangeExtension(outPath, realExt);
                        if (realExt == ".dds" && isNormalMap)
                            rawData = FixNormalMapChannels(rawData);
                        File.WriteAllBytes(actualPath, rawData);
                        outPath = actualPath;
                    }

                    logInfo(
                        $"{filePath}\n" +
                        $"-> {outPath}\n" +
                        $"({writeLen} bytes, real={realExt}, ver=0x{ver:x}, hdrUnc={headerUnc}, apiUnc={apiUnc}) [OK]\n"
                    );
                    return true;
                }
            }
            finally
            {
                FreeLibrary(mod);
            }
        }

        private static int ChooseOutputSize(int headerUnc, int apiUnc)
        {
            const int MaxSafe = 256 * 1024 * 1024;

            if (apiUnc > 0 && apiUnc <= MaxSafe)
            {
                if (headerUnc > 0 && headerUnc <= MaxSafe)
                {
                    return Math.Max(apiUnc, headerUnc);
                }
                return apiUnc;
            }

            if (headerUnc > 0 && headerUnc <= MaxSafe)
            {
                return headerUnc;
            }

            return 8 * 1024 * 1024;
        }

        private static string GetSig(byte[] data)
        {
            int n = Math.Min(4, data.Length);
            var sb = new StringBuilder(4);
            for (int i = 0; i < n; i++)
            {
                var b = data[i];
                sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            return sb.ToString();
        }

        // Accurate normal map detection using the MATERIALS database.
        // Checks whether the DT1 base name (without extension and format suffix like _TGA)
        // matches any texture listed as Texture1_BUMP in the .MAT files.
        private static bool IsNormalMapBySet(string filePath, HashSet<string> normalMapSet)
        {
            // DT1 filename: e.g. "COS_KRAB_DARK_N_TGA.DT1" → base = "COS_KRAB_DARK_N"
            var nameInfo = ParseFileNameAndExtension(filePath, null);
            // nameInfo.basePath is the stem without the _TGA/_DXT etc. suffix
            var stem = Path.GetFileName(nameInfo.basePath);
            return normalMapSet.Contains(stem);
        }

        // Fallback heuristic: filename contains "_N" as a standalone suffix.
        // e.g. ROCK_N.DT1, ROCK_N_DXT.DT1, ROAD_N_01.DT1 — all match.
        // Used only when the MATERIALS directory is not available.
        private static bool IsNormalMapBySuffix(string filePath)
        {
            var name = Path.GetFileNameWithoutExtension(filePath).ToUpperInvariant();
            if (name.EndsWith("_N")) return true;
            if (name.Contains("_N_")) return true;
            return false;
        }

        // VELoader outputs uncompressed 32-bit DDS with BGRA pixel layout but masks that
        // say R=0xFF0000 (byte2). Standard tools respect the masks, so diffuse/HUD textures
        // look correct without any fix. Normal maps however have channels packed as:
        //   VELoader byte0=B(X), byte1=G(Y), byte2=R(unused/0), byte3=A(Z)
        // and they need to be remapped to standard RGBA so that:
        //   new byte0=R = old A (Z stored in alpha)
        //   new byte1=G = old B (X stored in blue)
        //   new byte2=B = old G (Y stored in green)
        //   new byte3=A = 255   (old R/byte2 is always 0, not useful)
        // Only call this for normal maps (_N suffix). For all other textures, return as-is.
        private static byte[] FixNormalMapChannels(byte[] data)
        {
            if (data.Length < 128) return data;
            if (data[0] != 'D' || data[1] != 'D' || data[2] != 'S' || data[3] != ' ') return data;
            uint fourcc = BitConverter.ToUInt32(data, 84);
            if (fourcc != 0) return data;
            int bpp = (int)BitConverter.ToUInt32(data, 88);
            if (bpp != 32) return data;

            var result = (byte[])data.Clone();

            // Update masks to standard RGBA to match the remapped pixel layout
            BitConverter.GetBytes(0x000000FFu).CopyTo(result, 92);  // R mask
            BitConverter.GetBytes(0x0000FF00u).CopyTo(result, 96);  // G mask
            BitConverter.GetBytes(0x00FF0000u).CopyTo(result, 100); // B mask
            BitConverter.GetBytes(0xFF000000u).CopyTo(result, 104); // A mask

            // Remap pixel bytes for normal map channel layout
            for (int i = 128; i + 3 < result.Length; i += 4)
            {
                byte oldB = data[i];      // X component (stored in B)
                byte oldG = data[i + 1];  // Y component (stored in G)
                // byte2 = R is always 0, unused
                byte oldA = data[i + 3];  // Z component (stored in A)
                result[i]     = oldA;  // new R = Z
                result[i + 1] = oldB;  // new G = X
                result[i + 2] = oldG;  // new B = Y
                result[i + 3] = 255;   // new A = fully opaque
            }

            return result;
        }

        private static string DetectRealExtension(byte[] data)
        {
            if (data.Length >= 4 && data[0] == 'D' && data[1] == 'D' && data[2] == 'S' && data[3] == ' ')
                return ".dds";
            if (data.Length >= 4 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G')
                return ".png";
            if (data.Length >= 2 && data[0] == 'B' && data[1] == 'M')
                return ".bmp";
            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                return ".jpg";
            // TGA has no reliable magic; treat unknown as .dds (most common in this game)
            return ".dds";
        }

        // Resolves texconv.exe: next to our exe, then in PATH.
        public static string? ResolveTexconv()
        {
            string next2exe = Path.Combine(AppContext.BaseDirectory, "texconv.exe");
            if (File.Exists(next2exe)) return next2exe;

            // Search PATH
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                var candidate = Path.Combine(dir.Trim(), "texconv.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        // Converts a DDS file to the requested format using texconv.exe.
        // ddsPath    — temporary DDS file written by the caller
        // finalPath  — desired output path (e.g. foo.tga)
        // Returns true on success; on failure leaves ddsPath untouched.
        private static bool TryConvertWithTexconv(
            string texconvPath,
            string ddsPath,
            string finalPath,
            Action<string> logError)
        {
            string outDir  = Path.GetDirectoryName(finalPath)!;
            string outExt  = Path.GetExtension(finalPath).TrimStart('.');
            string outName = Path.GetFileNameWithoutExtension(finalPath);

            // texconv writes <name>.<ext> into the output directory.
            // -y  = overwrite without prompt
            // -ft = output format
            // -o  = output directory
            // -fl = feature level (needed for some DX formats, safe to omit for TGA/PNG/BMP)
            var args = $"-y -ft {outExt} -o \"{outDir}\" \"{ddsPath}\"";
            try
            {
                var psi = new ProcessStartInfo(texconvPath, args)
                {
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };
                using var proc = Process.Start(psi)!;
                proc.WaitForExit(30_000);

                // texconv writes output as <stem>.<ext> — rename if stem differs from desired
                string texconvOut = Path.Combine(outDir,
                    Path.GetFileNameWithoutExtension(ddsPath) + "." + outExt);

                if (File.Exists(texconvOut))
                {
                    if (!texconvOut.Equals(finalPath, StringComparison.OrdinalIgnoreCase))
                        File.Move(texconvOut, finalPath, overwrite: true);
                    return true;
                }

                logError($"[WARN] texconv produced no output for {Path.GetFileName(ddsPath)} — saved as .dds");
                return false;
            }
            catch (Exception ex)
            {
                logError($"[WARN] texconv failed for {Path.GetFileName(ddsPath)}: {ex.Message} — saved as .dds");
                return false;
            }
        }

        private static (string basePath, string ext) ParseFileNameAndExtension(string filePath, string? userExt)
        {
            var nameWithoutDt = Path.GetFileNameWithoutExtension(filePath);
            int lastUnder = nameWithoutDt.LastIndexOf('_');
            string ext = "dds";
            string baseName = nameWithoutDt;

            if (lastUnder > 0 && lastUnder < nameWithoutDt.Length - 1)
            {
                var potentialExt = nameWithoutDt.Substring(lastUnder + 1);
                if (potentialExt.Length >= 2 && potentialExt.Length <= 4 && potentialExt.All(char.IsLetterOrDigit))
                {
                    ext = potentialExt.ToLowerInvariant();
                    baseName = nameWithoutDt.Substring(0, lastUnder);
                }
            }

            if (!string.IsNullOrWhiteSpace(userExt))
            {
                ext = userExt.TrimStart('.').ToLowerInvariant();
            }

            return (baseName, "." + ext);
        }

        private sealed class Pinned : IDisposable
        {
            private GCHandle _handle;

            public IntPtr Ptr => _handle.AddrOfPinnedObject();

            public Pinned(byte[] data)
            {
                _handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            }

            public void Dispose()
            {
                if (_handle.IsAllocated)
                {
                    _handle.Free();
                }
            }
        }
    }
}
