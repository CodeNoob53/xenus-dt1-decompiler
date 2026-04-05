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

        public static (int Ok, int Fail) DecodeDirectory(
            string inputPath,
            string outputRoot,
            string veloaderPath,
            string? userExt,
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

            int ok = 0;
            int fail = 0;
            foreach (var file in files)
            {
                try
                {
                    if (DecodeOneFile(file, outputRoot, veloaderPath, inputPath, userExt, texconvPath, logInfo, logError))
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

                    bool converted = false;
                    if (texconvPath is not null
                        && !finalExt.Equals(realExt, StringComparison.OrdinalIgnoreCase))
                    {
                        // texconv needs a DDS file on disk; write a temp one then convert
                        var tmpDds = Path.ChangeExtension(outPath, ".dds");
                        var ddsData = realExt == ".dds" ? FixDdsBgraMasks(rawData) : rawData;
                        File.WriteAllBytes(tmpDds, ddsData);
                        converted = TryConvertWithTexconv(texconvPath, tmpDds, outPath, logError);
                        if (converted && !tmpDds.Equals(outPath, StringComparison.OrdinalIgnoreCase))
                            File.Delete(tmpDds);
                    }

                    if (!converted)
                    {
                        // Save with real extension (no conversion needed or conversion failed)
                        var actualPath = Path.ChangeExtension(outPath, realExt);
                        if (realExt == ".dds")
                            rawData = FixDdsBgraMasks(rawData);
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

        // VELoader stores uncompressed 32-bit DDS pixels with channel layout:
        //   byte0=B(mask 0x0000FF), byte1=G(mask 0x00FF00), byte2=R(mask 0xFF0000), byte3=A
        // i.e. BGRA in memory, but the masks describe it as "R at 0xFF0000" = byte2.
        // The correct output order for standard tools is RGBA in memory (R=byte0).
        // Remap: new[R,G,B,A] = old[A, B, G, R] = old[byte3, byte0, byte1, byte2]
        // Also update masks to standard RGBA.
        // Only applies to uncompressed 32-bit (non-FourCC) DDS.
        private static byte[] FixDdsBgraMasks(byte[] data)
        {
            if (data.Length < 128) return data;
            if (data[0] != 'D' || data[1] != 'D' || data[2] != 'S' || data[3] != ' ') return data;
            uint fourcc = BitConverter.ToUInt32(data, 84);
            if (fourcc != 0) return data;
            int bpp = (int)BitConverter.ToUInt32(data, 88);
            if (bpp != 32) return data;

            var result = (byte[])data.Clone();

            // Fix header masks to standard RGBA
            BitConverter.GetBytes(0x000000FFu).CopyTo(result, 92);  // R
            BitConverter.GetBytes(0x0000FF00u).CopyTo(result, 96);  // G
            BitConverter.GetBytes(0x00FF0000u).CopyTo(result, 100); // B
            BitConverter.GetBytes(0xFF000000u).CopyTo(result, 104); // A

            // Remap pixel bytes: new[0,1,2,3] = old[3,0,1,2]
            // old: byte0=B, byte1=G, byte2=R, byte3=A
            // new: byte0=R=old[3]? No: correct_R=old_A=old[byte3], correct_G=old_B=old[byte0], correct_B=old_G=old[byte1], correct_A=old_R=old[byte2]
            for (int i = 128; i + 3 < result.Length; i += 4)
            {
                byte oldB  = data[i];      // old byte0 = B channel
                byte oldG  = data[i + 1];  // old byte1 = G channel
                byte oldR  = data[i + 2];  // old byte2 = R channel
                byte oldA  = data[i + 3];  // old byte3 = A channel
                result[i]     = oldA;  // new R = old A
                result[i + 1] = oldB;  // new G = old B
                result[i + 2] = oldG;  // new B = old G
                result[i + 3] = 255;   // new A = fully opaque (old R is always 0)
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
