using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace XenusDt1Decompiler
{
    internal static class Program
    {
        private const string AppName = "xenus-dt1-decompiler";

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        private const int ATTACH_PARENT_PROCESS = -1;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return 0;
            }

            // We are strictly a GUI app (WinExe), so Windows detached us from the console.
            // Re-attach to the parent console so text output can be seen.
            AttachConsole(ATTACH_PARENT_PROCESS);
            try { Console.OutputEncoding = Encoding.UTF8; } catch { /* redirected/non-console stdout */ }
            Console.WriteLine(); // Add a new line to separate from the command prompt

            if (args.Length < 1 || args[0] == "/?" || args[0] == "--help" || args[0] == "-h")
            {
                PrintUsage();
                return 2;
            }

            var inputPath = Path.GetFullPath(args[0]);
            string outputRoot;
            if (args.Length >= 2)
            {
                outputRoot = Path.GetFullPath(args[1]);
            }
            else
            {
                var inputDir = Path.GetDirectoryName(inputPath);
                if (string.IsNullOrEmpty(inputDir))
                {
                    Console.Error.WriteLine($"Cannot derive output directory from input path: {inputPath}");
                    return 6;
                }
                outputRoot = inputDir;
            }

            bool veloaderAutoResolved = args.Length < 3;
            var veloaderPath = veloaderAutoResolved
                ? DecompilerCore.ResolveDefaultVELoader()
                : Path.GetFullPath(args[2]);
            var userExt = args.Length >= 4 ? args[3] : null;
            var materialsPath = args.Length >= 5 && !string.IsNullOrWhiteSpace(args[4]) ? Path.GetFullPath(args[4]) : null;

            if (!File.Exists(veloaderPath))
            {
                Console.Error.WriteLine(veloaderAutoResolved
                    ? $"VELoader.dll auto-resolved to \"{veloaderPath}\" but the file does not exist. Pass an explicit path as the 3rd argument."
                    : $"VELoader.dll not found: {veloaderPath}");
                return 3;
            }

            if (File.Exists(inputPath))
            {
                Directory.CreateDirectory(outputRoot);
                string? texconvPath = string.IsNullOrEmpty(userExt) ? null : DecompilerCore.ResolveTexconv();
                bool ok = DecompilerCore.DecodeOneFile(inputPath, outputRoot, veloaderPath, null, userExt, texconvPath,
                    msg => Console.WriteLine(msg),
                    err => Console.Error.WriteLine(err));
                return ok ? 0 : 1;
            }

            if (Directory.Exists(inputPath))
            {
                var res = DecompilerCore.DecodeDirectory(inputPath, outputRoot, veloaderPath, userExt, materialsPath,
                    msg => Console.WriteLine(msg),
                    err => Console.Error.WriteLine(err));
                
                if (res.Ok == 0 && res.Fail == 0)
                {
                    return 4;
                }

                return res.Fail == 0 ? 0 : 1;
            }

            Console.Error.WriteLine($"Input path not found: {inputPath}");
            return 5;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine($"  {AppName} <input_file.dt1|dt2> [output_dir] [veloader.dll] [format] [materials_dir]");
            Console.WriteLine($"  {AppName} <input_dir>           [output_dir] [veloader.dll] [format] [materials_dir]");
            Console.WriteLine();
            Console.WriteLine("  materials_dir  Path to the game MATERIALS/ folder (for normal map detection).");
            Console.WriteLine("                 Auto-detected if omitted. Pass \"\" to skip auto-detection.");
            Console.WriteLine();
            Console.WriteLine("Run without arguments to start the Graphical User Interface (GUI).");
        }
    }
}
