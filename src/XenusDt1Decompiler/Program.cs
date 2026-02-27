using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GetCLVersionFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetUnloadSizeFn(IntPtr packed);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UnloadFn(IntPtr packed, IntPtr unpacked, int unpackedSize);

    private const string AppName = "xenus-dt1-decompiler";
    private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr LoadLibraryExA(string lpLibFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr hModule);

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            PrintUsage();
            return 2;
        }

        var inputPath = Path.GetFullPath(args[0]);
        var outputRoot = args.Length >= 2 ? Path.GetFullPath(args[1]) : Path.GetDirectoryName(inputPath)!;
        var veloaderPath = args.Length >= 3
            ? Path.GetFullPath(args[2])
            : ResolveDefaultVELoader();

        if (!File.Exists(veloaderPath))
        {
            Console.Error.WriteLine($"VELoader.dll not found: {veloaderPath}");
            return 3;
        }

        if (File.Exists(inputPath))
        {
            Directory.CreateDirectory(outputRoot);
            return DecodeOneFile(inputPath, outputRoot, veloaderPath) ? 0 : 1;
        }

        if (Directory.Exists(inputPath))
        {
            var files = Directory.EnumerateFiles(inputPath, "*.DT*", SearchOption.AllDirectories)
                .Where(IsDt1OrDt2)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                Console.Error.WriteLine($"No DT1/DT2 files found in: {inputPath}");
                return 4;
            }

            int ok = 0;
            int fail = 0;
            foreach (var file in files)
            {
                if (DecodeOneFile(file, outputRoot, veloaderPath, inputPath))
                {
                    ok++;
                }
                else
                {
                    fail++;
                }
            }

            Console.WriteLine($"Done. OK={ok}, FAIL={fail}");
            return fail == 0 ? 0 : 1;
        }

        Console.Error.WriteLine($"Input path not found: {inputPath}");
        return 5;
    }

    private static bool IsDt1OrDt2(string path)
    {
        return path.EndsWith(".DT1", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".DT2", StringComparison.OrdinalIgnoreCase);
    }

    private static bool DecodeOneFile(string filePath, string outputRoot, string veloaderPath, string? inputRoot = null)
    {
        byte[] packed;
        try
        {
            packed = File.ReadAllBytes(filePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FAIL] {filePath} : read error: {ex.Message}");
            return false;
        }

        if (packed.Length < 8)
        {
            Console.Error.WriteLine($"[FAIL] {filePath} : too small ({packed.Length})");
            return false;
        }

        int headerUnc = packed[0] | (packed[1] << 8) | (packed[2] << 16);
        int headerComp = packed[4] | (packed[5] << 8) | (packed[6] << 16);
        int flags = packed[7];

        IntPtr mod = LoadLibraryExA(veloaderPath, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
        if (mod == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            Console.Error.WriteLine($"[FAIL] {filePath} : LoadLibraryEx failed ({err}) for {veloaderPath}");
            return false;
        }

        try
        {
            var pGetVer = GetProcAddress(mod, "GetCLVersion");
            var pGetUnc = GetProcAddress(mod, "GetUnloadSize");
            var pUnload = GetProcAddress(mod, "Unload");

            if (pGetVer == IntPtr.Zero || pGetUnc == IntPtr.Zero || pUnload == IntPtr.Zero)
            {
                Console.Error.WriteLine($"[FAIL] {filePath} : required exports not found");
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
                Console.Error.WriteLine($"[FAIL] {filePath} : GetCLVersion call failed: {ex.Message}");
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
                    Console.Error.WriteLine($"[FAIL] {filePath} : Unload call failed: {ex.Message}");
                    return false;
                }

                if (status == 0)
                {
                    Console.Error.WriteLine($"[FAIL] {filePath} : Unload returned 0 (ver=0x{ver:x}, hdrUnc={headerUnc}, apiUnc={apiUnc}, hdrComp24={headerComp}, flags=0x{flags:x2})");
                    return false;
                }

                int writeLen = apiUnc > 0 && apiUnc <= unpacked.Length ? apiUnc : unpacked.Length;
                var outRel = inputRoot is null
                    ? Path.GetFileName(filePath)
                    : Path.GetRelativePath(inputRoot, filePath);

                var outPath = Path.Combine(outputRoot, outRel + ".dds");
                var outDir = Path.GetDirectoryName(outPath)!;
                Directory.CreateDirectory(outDir);
                File.WriteAllBytes(outPath, unpacked.AsSpan(0, writeLen).ToArray());

                var sig = GetSig(unpacked);
                Console.WriteLine($"[OK] {filePath} -> {outPath} ({writeLen} bytes, sig='{sig}', ver=0x{ver:x}, hdrUnc={headerUnc}, apiUnc={apiUnc}, hdrComp24={headerComp}, flags=0x{flags:x2})");
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

    private static string ResolveDefaultVELoader()
    {
        string cwd = Directory.GetCurrentDirectory();
        string[] candidates =
        {
            Path.Combine(cwd, "VELoader.dll"),
            Path.Combine(cwd, "GrpUnpacker", "VELoader.dll"),
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

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine($"  {AppName} <input_file.dt1|dt2> [output_dir] [path_to_veloader.dll]");
        Console.WriteLine($"  {AppName} <input_dir> [output_dir] [path_to_veloader.dll]");
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
