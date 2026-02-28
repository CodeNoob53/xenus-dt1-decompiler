using System;
using System.IO;
using System.Runtime.InteropServices;
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
            Console.WriteLine(); // Add a new line to separate from the command prompt

            if (args.Length < 1 || args[0] == "/?" || args[0] == "--help" || args[0] == "-h")
            {
                PrintUsage();
                return 2;
            }

            var inputPath = Path.GetFullPath(args[0]);
            var outputRoot = args.Length >= 2 ? Path.GetFullPath(args[1]) : Path.GetDirectoryName(inputPath)!;
            var veloaderPath = args.Length >= 3
                ? Path.GetFullPath(args[2])
                : DecompilerCore.ResolveDefaultVELoader();
            var userExt = args.Length >= 4 ? args[3] : null;

            if (!File.Exists(veloaderPath))
            {
                Console.Error.WriteLine($"VELoader.dll not found: {veloaderPath}");
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
                var res = DecompilerCore.DecodeDirectory(inputPath, outputRoot, veloaderPath, userExt,
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
            Console.WriteLine($"  {AppName} <input_file.dt1|dt2> [output_dir] [path_to_veloader.dll] [format]");
            Console.WriteLine($"  {AppName} <input_dir> [output_dir] [path_to_veloader.dll] [format]");
            Console.WriteLine();
            Console.WriteLine("Run without arguments to start the Graphical User Interface (GUI).");
        }
    }
}
