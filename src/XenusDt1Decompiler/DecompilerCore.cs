using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

namespace XenusDt1Decompiler
{
    public static class DecompilerCore
    {
        public static bool IsDt1OrDt2(string path)
        {
            return path.EndsWith(".DT1", StringComparison.OrdinalIgnoreCase);
        }

        // Managed reimplementation of VELoader.dll!Unload (reversed from the unprotected DLL build).
        //
        // Header (8 bytes, little-endian):
        //   dword0: bits 28..31 = method, bits 0..27 = unpacked size
        //   dword1: bits 27..31 = key index (method 7 only), bits 0..26 = packed size incl. header
        // Payload (data[8 .. packedSize]):
        //   method 0    -> stored as-is
        //   method 1..3 -> UCL NRV2E stream; methods 2 and 3 XOR the first min(16, len) bytes with 0x6E first
        //                  (Boiling Point uses method 2)
        //   method 4..7 -> first min(16, len) bytes XOR 0x6E, then a plain zlib stream (zlib 1.2.2)
        //   method 7 with payload >= 0x100 bytes is additionally encrypted with a key table (unsupported)
        public static byte[] UnpackDt(byte[] packed)
        {
            if (packed.Length < 8)
                throw new InvalidDataException($"too small ({packed.Length} bytes)");

            uint h0 = BitConverter.ToUInt32(packed, 0);
            uint h1 = BitConverter.ToUInt32(packed, 4);
            int method = (int)(h0 >> 28);
            int unpackedSize = (int)(h0 & 0x0FFFFFFF);
            int packedSize = (int)(h1 & 0x07FFFFFF);

            if (method == 7 && (uint)(packedSize - 8) >= 0x100) // unsigned, as in VELoader
                throw new NotSupportedException($"method 7 (encrypted, key {h1 >> 27}) is not supported");
            if (method > 7)
                throw new InvalidDataException($"unknown compression method {method}");
            if (packedSize < 8 || packedSize > packed.Length)
                throw new InvalidDataException($"bad packed size {packedSize} (file is {packed.Length} bytes)");

            int payloadLen = packedSize - 8;

            if (method == 0)
            {
                if (unpackedSize > payloadLen)
                    throw new InvalidDataException($"stored block truncated ({payloadLen} < {unpackedSize})");
                return packed.AsSpan(8, unpackedSize).ToArray();
            }

            var payload = packed.AsSpan(8, payloadLen).ToArray();
            if (method != 1)
            {
                for (int i = 0; i < Math.Min(16, payload.Length); i++)
                    payload[i] ^= 0x6E;
            }

            if (method <= 3)
                return DecompressNrv2e(payload, unpackedSize);

            var result = new byte[unpackedSize];
            using (var zs = new ZLibStream(new MemoryStream(payload), CompressionMode.Decompress))
            {
                int read = 0;
                while (read < result.Length)
                {
                    int n = zs.Read(result, read, result.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                if (read != unpackedSize)
                    throw new InvalidDataException($"zlib stream ended early ({read} of {unpackedSize} bytes)");
            }
            return result;
        }

        // UCL NRV2E decompressor with an 8-bit bit buffer (ucl_nrv2e_decompress_safe_8),
        // as used by VELoader.dll for methods 1..3.
        internal static byte[] DecompressNrv2e(byte[] src, int outLen)
        {
            var dst = new byte[outLen];
            int ilen = 0, olen = 0;
            uint bb = 0;
            uint lastOff = 1;

            int GetBit()
            {
                if ((bb & 0x7F) != 0)
                {
                    bb *= 2;
                }
                else
                {
                    if (ilen >= src.Length)
                        throw new InvalidDataException("NRV2E input overrun");
                    bb = (uint)src[ilen++] * 2 + 1;
                }
                return (int)((bb >> 8) & 1);
            }

            byte NextByte()
            {
                if (ilen >= src.Length)
                    throw new InvalidDataException("NRV2E input overrun");
                return src[ilen++];
            }

            while (true)
            {
                while (GetBit() == 1)
                {
                    if (olen >= outLen)
                        throw new InvalidDataException("NRV2E output overrun");
                    dst[olen++] = NextByte();
                }

                uint off = 1;
                while (true)
                {
                    off = off * 2 + (uint)GetBit();
                    if (GetBit() == 1) break;
                    off = (off - 1) * 2 + (uint)GetBit();
                }

                uint len;
                if (off == 2)
                {
                    off = lastOff;
                    len = (uint)GetBit();
                }
                else
                {
                    off = (off - 3) * 256 + NextByte();
                    if (off == 0xFFFFFFFF)
                        break; // end of stream
                    len = (off ^ 0xFFFFFFFF) & 1;
                    off = (off >> 1) + 1;
                    lastOff = off;
                }

                if (len != 0)
                {
                    len = 1 + (uint)GetBit();
                }
                else if (GetBit() == 1)
                {
                    len = 3 + (uint)GetBit();
                }
                else
                {
                    len = 1;
                    do
                    {
                        len = len * 2 + (uint)GetBit();
                    } while (GetBit() == 0);
                    len += 3;
                }
                if (off > 0x500)
                    len++;

                if (off > (uint)olen)
                    throw new InvalidDataException("NRV2E lookbehind overrun");
                if (olen + len + 1 > outLen)
                    throw new InvalidDataException("NRV2E output overrun");
                int from = olen - (int)off;
                for (uint i = 0; i <= len; i++)
                    dst[olen++] = dst[from++];
            }

            if (olen != outLen)
                throw new InvalidDataException($"NRV2E stream ended early ({olen} of {outLen} bytes)");
            if (ilen != src.Length)
                throw new InvalidDataException($"NRV2E stream has {src.Length - ilen} trailing bytes");
            return dst;
        }

        public static (int Ok, int Fail) DecodeDirectory(
            string inputPath,
            string outputRoot,
            string? userExt,
            Action<string> logInfo,
            Action<string> logError,
            Action<int, int>? onProgress = null,
            CancellationToken cancellationToken = default)
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

            var keepSuffix = FindOutputNameClashes(files, inputPath, userExt);
            if (keepSuffix.Count > 0)
                logInfo($"[INFO] {keepSuffix.Count} textures would share an output file name; they keep their format suffix: " +
                        string.Join(", ", keepSuffix.Select(Path.GetFileNameWithoutExtension)));

            // Files are independent and every one gets a unique output path, so they are decoded
            // and converted in parallel; texconv runs as one process per file.
            int ok = 0;
            int fail = 0;
            int done = 0;
            var failed = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var stopwatch = Stopwatch.StartNew();
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken,
            };
            try
            {
                Parallel.ForEach(files, options, file =>
                {
                    bool success;
                    try
                    {
                        success = DecodeOneFile(file, outputRoot, inputPath, userExt, texconvPath, logInfo, logError,
                            keepSuffix.Contains(file));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logError($"[FAIL] {Shown(file, inputPath)} : {ex.Message}");
                        success = false;
                    }

                    if (success)
                    {
                        Interlocked.Increment(ref ok);
                    }
                    else
                    {
                        Interlocked.Increment(ref fail);
                        failed.Enqueue(Shown(file, inputPath));
                    }
                    // Increment outside ?.Invoke: without a progress callback the argument would not be evaluated.
                    int doneNow = Interlocked.Increment(ref done);
                    onProgress?.Invoke(doneNow, files.Count);
                });
            }
            catch (OperationCanceledException)
            {
                logError($"[WARN] Cancelled after {done} of {files.Count} files.");
            }

            logInfo("");
            int skipped = files.Count - done;
            logInfo($"Done in {stopwatch.Elapsed.TotalSeconds:0.0} s: {ok} OK, {fail} failed" +
                    (skipped > 0 ? $", {skipped} not processed." : "."));
            if (!failed.IsEmpty)
            {
                const int MaxListed = 50;
                var list = failed.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                logError($"[FAIL] Failed files ({list.Count}):");
                foreach (var f in list.Take(MaxListed))
                    logError($"[FAIL]   {f}");
                if (list.Count > MaxListed)
                    logError($"[FAIL]   ... and {list.Count - MaxListed} more");
            }
            return (ok, fail);
        }

        // Path as shown in the log: relative to the batch root when there is one.
        private static string Shown(string path, string? root) =>
            root is null ? path : Path.GetRelativePath(root, path);

        // Returns the input files whose output name would collide with another input's
        // (e.g. SKY00_BMP.DT1 and SKY00_TGA.DT1 are different textures, but in dds or a forced
        // format both would become SKY00.dds). Those files keep their format suffix in the output name.
        internal static HashSet<string> FindOutputNameClashes(IEnumerable<string> files, string inputRoot, string? userExt)
        {
            static string PlannedExt(string hintExt, string? userExt)
            {
                if (!string.IsNullOrWhiteSpace(userExt))
                    return "." + userExt.TrimStart('.').ToLowerInvariant();
                return hintExt is ".tga" or ".bmp" or ".png" or ".jpg" or ".jpeg" or ".dds" ? hintExt : ".dds";
            }

            return files
                .GroupBy(f =>
                {
                    var (basePath, hintExt) = ParseFileNameAndExtension(f, null);
                    var relDir = Path.GetDirectoryName(Path.GetRelativePath(inputRoot, f)) ?? "";
                    return Path.Combine(relDir, basePath) + PlannedExt(hintExt, userExt);
                }, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .SelectMany(g => g)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public static bool DecodeOneFile(
            string filePath,
            string outputRoot,
            string? inputRoot,
            string? userExt,
            string? texconvPath,
            Action<string> logInfo,
            Action<string> logError,
            bool keepFormatSuffix = false)
        {
            byte[] packed;
            try
            {
                packed = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                logError($"[FAIL] {Shown(filePath, inputRoot)} : read error: {ex.Message}");
                return false;
            }

            byte[] rawData;
            try
            {
                rawData = UnpackDt(packed);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                logError($"[FAIL] {Shown(filePath, inputRoot)} : {ex.Message}");
                return false;
            }

            var outRelDir = inputRoot is null
                ? ""
                : Path.GetDirectoryName(Path.GetRelativePath(inputRoot, filePath)) ?? "";

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

            var outStem = keepFormatSuffix ? Path.GetFileNameWithoutExtension(filePath) : nameInfo.basePath;
            var newFileName = outStem + finalExt;
            var outPath = Path.Combine(outputRoot, outRelDir, newFileName);
            var outDir = Path.GetDirectoryName(outPath)!;
            Directory.CreateDirectory(outDir);

            if (IsSwizzledNormalMap(rawData))
                rawData = FixNormalMapChannels(rawData);

            bool converted = false;
            bool wantsConversion = !finalExt.Equals(realExt, StringComparison.OrdinalIgnoreCase);
            bool canSoftwareConvert = wantsConversion && realExt == ".dds";

            // texconv would treat the integer lookup tables as floats and clamp them; use the built-in packer.
            if (texconvPath is not null && wantsConversion && realExt == ".dds" && !IsRaw16Lut(rawData))
            {
                // texconv needs a DDS file on disk. Use a private temp folder so that parallel
                // conversions never touch each other's files or a real <name>.dds in the output folder.
                var workDir = Path.Combine(Path.GetTempPath(), "xenus-dt1", Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(workDir);
                    var tmpDds = Path.Combine(workDir, Path.GetFileNameWithoutExtension(outPath) + ".dds");
                    File.WriteAllBytes(tmpDds, rawData);
                    converted = TryConvertWithTexconv(texconvPath, tmpDds, outPath, logError);
                }
                finally
                {
                    try
                    {
                        Directory.Delete(workDir, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        logError($"[WARN] Failed to delete temp folder \"{workDir}\": {ex.Message}");
                    }
                }
            }

            if (!converted && canSoftwareConvert)
                converted = TryConvertDdsWithoutTexconv(rawData, outPath, logError);

            if (!converted)
            {
                // Save with real extension (no conversion needed or conversion failed)
                var actualPath = Path.ChangeExtension(outPath, realExt);
                File.WriteAllBytes(actualPath, rawData);

                if (wantsConversion)
                    logError($"[WARN] Unable to convert {Shown(filePath, inputRoot)} to {finalExt}; saved as {realExt}.");

                outPath = actualPath;
            }
            else if (IsRaw16Lut(rawData))
            {
                // DXT1CTRL / R5G6R5TABLE: engine lookup tables, not pictures (see TryDecodeRaw16Lut).
                var name = Path.GetFileName(outPath);
                if (finalExt is ".jpg" or ".jpeg")
                    logError($"[WARN] {name} is an engine lookup table; JPG is lossy, so this preview cannot be turned back into the table. Use Auto or dds to keep the original .dds.");
                else
                    logInfo($"[INFO] {name} is an engine lookup table, saved as a lossless preview atlas for viewing only. Use the .dds from Auto or dds mode to repack it.");
            }

            logInfo($"OK    {(inputRoot is null ? outPath : Path.GetRelativePath(outputRoot, outPath))}");
            return true;
        }

        // The cache stores uncompressed 32-bit DDS as A8R8G8B8 (masks R=0xFF0000, byte order B,G,R,A).
        // Diffuse/HUD/sky textures are stored normally and need no fix. Normal maps, however,
        // are stored swizzled for the engine's shaders:
        //   byte0 (B slot) = Y,  byte1 (G slot) = Z,  byte2 (R slot) = 0,  byte3 (A slot) = X
        // Normal maps are recognised by their pixels rather than by name or .MAT files:
        // the R slot is 0 in every pixel and Z (byte1) averages >= 128, because the normals
        // point out of the surface. In the Xenus 2 cache this matches all 818 normal maps
        // (exactly the textures that also have a .DT3) and none of the other 40 uncompressed textures.
        internal static bool IsSwizzledNormalMap(byte[] data)
        {
            if (!IsUncompressedArgb32Dds(data)) return false;

            long zSum = 0;
            int count = 0;
            for (int i = 128; i + 3 < data.Length; i += 4)
            {
                if (data[i + 2] != 0) return false;
                zSum += data[i + 1];
                count++;
            }
            return count > 0 && zSum >= 128L * count;
        }

        private static bool IsUncompressedArgb32Dds(byte[] data)
        {
            if (data.Length <= 128) return false;
            if (data[0] != 'D' || data[1] != 'D' || data[2] != 'S' || data[3] != ' ') return false;
            return BitConverter.ToUInt32(data, 84) == 0            // no FourCC
                && BitConverter.ToUInt32(data, 88) == 32           // 32 bpp
                && BitConverter.ToUInt32(data, 92) == 0x00FF0000u; // R mask = byte2
        }

        // Remaps a swizzled normal map (see IsSwizzledNormalMap) to standard RGBA:
        //   R = X (old byte3), G = Y (old byte0), B = Z (old byte1), A = 255
        internal static byte[] FixNormalMapChannels(byte[] data)
        {
            var result = (byte[])data.Clone();

            // Update masks to standard RGBA to match the remapped pixel layout
            BitConverter.GetBytes(0x000000FFu).CopyTo(result, 92);  // R mask
            BitConverter.GetBytes(0x0000FF00u).CopyTo(result, 96);  // G mask
            BitConverter.GetBytes(0x00FF0000u).CopyTo(result, 100); // B mask
            BitConverter.GetBytes(0xFF000000u).CopyTo(result, 104); // A mask

            // Remap pixel bytes for normal map channel layout
            for (int i = 128; i + 3 < result.Length; i += 4)
            {
                byte y = data[i];
                byte z = data[i + 1];
                byte x = data[i + 3];
                result[i]     = x;
                result[i + 1] = y;
                result[i + 2] = z;
                result[i + 3] = 255;
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
            // texconv writes <stem>.<ext> next to the input (the input is in its own temp folder),
            // and the result is then moved to finalPath.
            string workDir = Path.GetDirectoryName(ddsPath)!;
            string outExt  = Path.GetExtension(finalPath).TrimStart('.');

            // -y  = overwrite without prompt
            // -ft = output format
            // -o  = output directory
            var args = $"-y -ft {outExt} -o \"{workDir}\" \"{ddsPath}\"";
            try
            {
                var psi = new ProcessStartInfo(texconvPath, args)
                {
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };
                using var proc = Process.Start(psi)
                    ?? throw new InvalidOperationException($"Failed to start texconv: {texconvPath}");
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(30_000);

                string texconvOut = Path.Combine(workDir,
                    Path.GetFileNameWithoutExtension(ddsPath) + "." + outExt);

                if (File.Exists(texconvOut))
                {
                    File.Move(texconvOut, finalPath, overwrite: true);
                    return true;
                }

                string detail = BuildTexconvFailureDetail(proc.ExitCode, stderr, stdout);
                logError($"[WARN] texconv produced no output for {Path.GetFileName(ddsPath)}{detail}");
                return false;
            }
            catch (Exception ex)
            {
                logError($"[WARN] texconv failed for {Path.GetFileName(ddsPath)}: {ex.Message}");
                return false;
            }
        }

        private static bool TryConvertDdsWithoutTexconv(
            byte[] ddsData,
            string finalPath,
            Action<string> logError)
        {
            if (!TryDecodeDdsToBgra32(ddsData, out int width, out int height, out byte[] pixels, out string reason))
            {
                logError($"[WARN] Built-in DDS fallback could not decode {Path.GetFileName(finalPath)}: {reason}");
                return false;
            }

            try
            {
                string ext = Path.GetExtension(finalPath).ToLowerInvariant();
                switch (ext)
                {
                    case ".tga":
                        WriteTga(finalPath, width, height, pixels);
                        return true;
                    case ".bmp":
                    case ".png":
                    case ".jpg":
                    case ".jpeg":
                        SaveBitmap(finalPath, width, height, pixels, ext);
                        return true;
                    default:
                        logError($"[WARN] Built-in DDS fallback does not support {ext} output.");
                        return false;
                }
            }
            catch (Exception ex)
            {
                logError($"[WARN] Built-in DDS fallback failed for {Path.GetFileName(finalPath)}: {ex.Message}");
                return false;
            }
        }

        private static bool TryDecodeDdsToBgra32(
            byte[] data,
            out int width,
            out int height,
            out byte[] pixels,
            out string reason)
        {
            width = 0;
            height = 0;
            pixels = Array.Empty<byte>();
            reason = string.Empty;

            if (data.Length < 128 || data[0] != 'D' || data[1] != 'D' || data[2] != 'S' || data[3] != ' ')
            {
                reason = "input is not a DDS file";
                return false;
            }

            height = BitConverter.ToInt32(data, 12);
            width = BitConverter.ToInt32(data, 16);
            if (width <= 0 || height <= 0)
            {
                reason = "invalid DDS dimensions";
                return false;
            }

            uint pfFlags = BitConverter.ToUInt32(data, 80);
            uint fourCc = BitConverter.ToUInt32(data, 84);
            int bitsPerPixel = (int)BitConverter.ToUInt32(data, 88);
            uint rMask = BitConverter.ToUInt32(data, 92);
            uint gMask = BitConverter.ToUInt32(data, 96);
            uint bMask = BitConverter.ToUInt32(data, 100);
            uint aMask = BitConverter.ToUInt32(data, 104);

            const uint DdpfFourCc = 0x4;
            if ((pfFlags & DdpfFourCc) != 0)
            {
                if (fourCc == MakeFourCc("DXT1"))
                    return TryDecodeDxt1(data, width, height, out pixels, out reason);
                if (fourCc == D3dFmtG16R16F)
                    return TryDecodeRaw16Lut(data, 2, ref width, ref height, out pixels, out reason);
                if (fourCc == D3dFmtA16B16G16R16F)
                    return TryDecodeRaw16Lut(data, 4, ref width, ref height, out pixels, out reason);

                reason = $"unsupported DDS FourCC {FourCcToString(fourCc)}";
                return false;
            }

            return TryDecodeUncompressedDds(
                data, width, height, bitsPerPixel, rMask, gMask, bMask, aMask,
                out pixels, out reason);
        }

        private static bool TryDecodeDxt1(
            byte[] data,
            int width,
            int height,
            out byte[] pixels,
            out string reason)
        {
            pixels = new byte[width * height * 4];
            reason = string.Empty;

            int blockCountX = (width + 3) / 4;
            int blockCountY = (height + 3) / 4;
            int requiredBytes = blockCountX * blockCountY * 8;
            if (data.Length < 128 + requiredBytes)
            {
                reason = "DDS payload is truncated for DXT1";
                pixels = Array.Empty<byte>();
                return false;
            }

            int offset = 128;
            Span<byte> palette = stackalloc byte[16];

            for (int by = 0; by < blockCountY; by++)
            {
                for (int bx = 0; bx < blockCountX; bx++)
                {
                    ushort color0 = BitConverter.ToUInt16(data, offset);
                    ushort color1 = BitConverter.ToUInt16(data, offset + 2);
                    uint indices = BitConverter.ToUInt32(data, offset + 4);
                    offset += 8;

                    WriteRgb565Color(color0, palette, 0);
                    WriteRgb565Color(color1, palette, 4);

                    if (color0 > color1)
                    {
                        InterpolateColor(palette, 0, palette, 4, palette, 8, 2, 1);
                        InterpolateColor(palette, 0, palette, 4, palette, 12, 1, 2);
                    }
                    else
                    {
                        AverageColor(palette, 0, palette, 4, palette, 8);
                        palette[12] = 0;
                        palette[13] = 0;
                        palette[14] = 0;
                        palette[15] = 0;
                    }

                    for (int py = 0; py < 4; py++)
                    {
                        int y = by * 4 + py;
                        if (y >= height)
                            break;

                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx * 4 + px;
                            if (x >= width)
                                break;

                            int paletteIndex = (int)((indices >> (2 * (py * 4 + px))) & 0x3);
                            int src = paletteIndex * 4;
                            int dst = (y * width + x) * 4;
                            pixels[dst] = palette[src];
                            pixels[dst + 1] = palette[src + 1];
                            pixels[dst + 2] = palette[src + 2];
                            pixels[dst + 3] = palette[src + 3];
                        }
                    }
                }
            }

            return true;
        }

        // Legacy D3DFORMAT codes used as FourCC by the SPECIAL lookup tables.
        private const uint D3dFmtG16R16F = 112;
        private const uint D3dFmtA16B16G16R16F = 113;

        internal static bool IsRaw16Lut(byte[] data)
        {
            if (data.Length < 128 || data[0] != 'D' || data[1] != 'D' || data[2] != 'S' || data[3] != ' ')
                return false;
            uint fourCc = BitConverter.ToUInt32(data, 84);
            return fourCc is D3dFmtG16R16F or D3dFmtA16B16G16R16F;
        }

        // DXT1CTRL (G16R16F, 256x256) and R5G6R5TABLE (A16B16G16R16F, 32x64x32 volume) are
        // lookup tables of 16-bit integer codes stored in half-float formats; read as floats
        // they are mostly denormals and NaNs. They cannot be represented in an 8-bit image
        // directly, so they are packed losslessly:
        //   - each pair of 16-bit channels (c0,c1) becomes one RGBA8 pixel:
        //       R = lo(c0), G = hi(c0), B = lo(c1), A = hi(c1)
        //   - with 4 channels, the (c2,c3) pair goes to a second block to the right
        //   - volume slices are stacked top to bottom (slice z at rows z*height .. z*height+height-1)
        // Output size: (width * channels/2) x (height * depth).
        // For DXT1CTRL, and for slice 0 / (c0,c1) of R5G6R5TABLE, this is the same image as
        // Noesis shows (c0 == c1 in both tables).
        internal static bool TryDecodeRaw16Lut(
            byte[] data,
            int channels,
            ref int width,
            ref int height,
            out byte[] pixels,
            out string reason)
        {
            pixels = Array.Empty<byte>();
            reason = string.Empty;

            const uint DdsdDepth = 0x800000;
            uint flags = BitConverter.ToUInt32(data, 8);
            int depth = (flags & DdsdDepth) != 0 ? Math.Max(1, BitConverter.ToInt32(data, 24)) : 1;

            int srcWidth = width;
            int srcHeight = height;
            long requiredBytes = (long)srcWidth * srcHeight * depth * channels * 2;
            if (data.Length < 128 + requiredBytes)
            {
                reason = $"DDS payload is truncated for a {channels}x16-bit table";
                return false;
            }

            int pairs = channels / 2;
            int outWidth = srcWidth * pairs;
            int outHeight = srcHeight * depth;
            pixels = new byte[outWidth * outHeight * 4];

            int src = 128;
            for (int z = 0; z < depth; z++)
            {
                for (int y = 0; y < srcHeight; y++)
                {
                    for (int x = 0; x < srcWidth; x++)
                    {
                        for (int p = 0; p < pairs; p++)
                        {
                            ushort c0 = BitConverter.ToUInt16(data, src);
                            ushort c1 = BitConverter.ToUInt16(data, src + 2);
                            src += 4;

                            int dst = (((z * srcHeight) + y) * outWidth + p * srcWidth + x) * 4; // BGRA
                            pixels[dst]     = (byte)c1;        // B = lo(c1)
                            pixels[dst + 1] = (byte)(c0 >> 8); // G = hi(c0)
                            pixels[dst + 2] = (byte)c0;        // R = lo(c0)
                            pixels[dst + 3] = (byte)(c1 >> 8); // A = hi(c1)
                        }
                    }
                }
            }

            width = outWidth;
            height = outHeight;
            return true;
        }


        private static bool TryDecodeUncompressedDds(
            byte[] data,
            int width,
            int height,
            int bitsPerPixel,
            uint rMask,
            uint gMask,
            uint bMask,
            uint aMask,
            out byte[] pixels,
            out string reason)
        {
            pixels = new byte[width * height * 4];
            reason = string.Empty;

            int bytesPerPixel = bitsPerPixel switch
            {
                16 => 2,
                24 => 3,
                32 => 4,
                _ => 0,
            };

            if (bytesPerPixel == 0)
            {
                reason = $"unsupported uncompressed DDS bpp={bitsPerPixel}";
                pixels = Array.Empty<byte>();
                return false;
            }

            int requiredBytes = width * height * bytesPerPixel;
            if (data.Length < 128 + requiredBytes)
            {
                reason = "DDS payload is truncated for uncompressed data";
                pixels = Array.Empty<byte>();
                return false;
            }

            int src = 128;
            for (int i = 0; i < width * height; i++)
            {
                uint value = bytesPerPixel switch
                {
                    2 => BitConverter.ToUInt16(data, src),
                    3 => (uint)(data[src] | (data[src + 1] << 8) | (data[src + 2] << 16)),
                    _ => BitConverter.ToUInt32(data, src),
                };
                src += bytesPerPixel;

                int dst = i * 4;
                pixels[dst] = ExtractChannel(value, bMask);
                pixels[dst + 1] = ExtractChannel(value, gMask);
                pixels[dst + 2] = ExtractChannel(value, rMask);
                pixels[dst + 3] = aMask == 0 ? (byte)255 : ExtractChannel(value, aMask);
            }

            return true;
        }

        private static void SaveBitmap(string finalPath, int width, int height, byte[] pixels, string ext)
        {
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            var bmpData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }

            var imageFormat = ext switch
            {
                ".bmp" => ImageFormat.Bmp,
                ".png" => ImageFormat.Png,
                ".jpg" => ImageFormat.Jpeg,
                ".jpeg" => ImageFormat.Jpeg,
                _ => throw new NotSupportedException($"Unsupported bitmap output format: {ext}"),
            };

            bitmap.Save(finalPath, imageFormat);
        }

        private static void WriteTga(string finalPath, int width, int height, byte[] pixels)
        {
            var header = new byte[18];
            header[2] = 2;
            header[12] = (byte)(width & 0xFF);
            header[13] = (byte)((width >> 8) & 0xFF);
            header[14] = (byte)(height & 0xFF);
            header[15] = (byte)((height >> 8) & 0xFF);
            header[16] = 32;
            header[17] = 8 | 0x20;

            using var fs = File.Create(finalPath);
            fs.Write(header, 0, header.Length);
            fs.Write(pixels, 0, pixels.Length);
        }

        private static byte ExtractChannel(uint value, uint mask)
        {
            if (mask == 0)
                return 0;

            int shift = BitOperations.TrailingZeroCount(mask);
            uint maxValue = mask >> shift;
            uint channel = (value & mask) >> shift;
            return (byte)((channel * 255 + (maxValue / 2)) / maxValue);
        }

        private static void WriteRgb565Color(ushort color, Span<byte> dest, int index)
        {
            byte r = (byte)((((color >> 11) & 0x1F) * 255 + 15) / 31);
            byte g = (byte)((((color >> 5) & 0x3F) * 255 + 31) / 63);
            byte b = (byte)(((color & 0x1F) * 255 + 15) / 31);

            dest[index] = b;
            dest[index + 1] = g;
            dest[index + 2] = r;
            dest[index + 3] = 255;
        }

        private static void InterpolateColor(
            Span<byte> c0,
            int c0Index,
            Span<byte> c1,
            int c1Index,
            Span<byte> dest,
            int destIndex,
            int w0,
            int w1)
        {
            int denom = w0 + w1;
            dest[destIndex] = (byte)((c0[c0Index] * w0 + c1[c1Index] * w1) / denom);
            dest[destIndex + 1] = (byte)((c0[c0Index + 1] * w0 + c1[c1Index + 1] * w1) / denom);
            dest[destIndex + 2] = (byte)((c0[c0Index + 2] * w0 + c1[c1Index + 2] * w1) / denom);
            dest[destIndex + 3] = 255;
        }

        private static void AverageColor(
            Span<byte> c0,
            int c0Index,
            Span<byte> c1,
            int c1Index,
            Span<byte> dest,
            int destIndex)
        {
            dest[destIndex] = (byte)((c0[c0Index] + c1[c1Index]) / 2);
            dest[destIndex + 1] = (byte)((c0[c0Index + 1] + c1[c1Index + 1]) / 2);
            dest[destIndex + 2] = (byte)((c0[c0Index + 2] + c1[c1Index + 2]) / 2);
            dest[destIndex + 3] = 255;
        }

        private static uint MakeFourCc(string value)
        {
            if (value.Length != 4)
                throw new ArgumentException("FourCC must be exactly 4 characters.", nameof(value));

            return (uint)value[0]
                | ((uint)value[1] << 8)
                | ((uint)value[2] << 16)
                | ((uint)value[3] << 24);
        }

        private static string FourCcToString(uint value)
        {
            Span<char> chars = stackalloc char[4];
            chars[0] = (char)(value & 0xFF);
            chars[1] = (char)((value >> 8) & 0xFF);
            chars[2] = (char)((value >> 16) & 0xFF);
            chars[3] = (char)((value >> 24) & 0xFF);
            return new string(chars);
        }

        private static string BuildTexconvFailureDetail(int exitCode, string stderr, string stdout)
        {
            string message = stderr;
            if (string.IsNullOrWhiteSpace(message))
                message = stdout;

            message = message.Trim();
            if (message.Length > 220)
                message = message.Substring(0, 220).TrimEnd() + "...";

            return string.IsNullOrEmpty(message)
                ? $" (exit={exitCode})"
                : $" (exit={exitCode}: {message})";
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
    }
}
