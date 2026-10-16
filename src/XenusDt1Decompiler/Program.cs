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

            // Older versions took VELoader.dll as the 3rd argument; it is no longer needed, skip it if present.
            int optArg = args.Length >= 3 && args[2].EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
            var userExt = args.Length > optArg && !string.IsNullOrWhiteSpace(args[optArg]) ? args[optArg] : null;
            // Older versions also took a MATERIALS folder after the format; normal maps are now
            // detected from pixel data, so any further arguments are ignored.

            if (File.Exists(inputPath))
            {
                Directory.CreateDirectory(outputRoot);
                string? texconvPath = string.IsNullOrEmpty(userExt) ? null : DecompilerCore.ResolveTexconv();
                bool ok = DecompilerCore.DecodeOneFile(inputPath, outputRoot, null, userExt, texconvPath,
                    msg => Console.WriteLine(msg),
                    err => Console.Error.WriteLine(err));
                return ok ? 0 : 1;
            }

            if (Directory.Exists(inputPath))
            {
                var res = DecompilerCore.DecodeDirectory(inputPath, outputRoot, userExt,
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
            Console.WriteLine($"  {AppName} <input_file.dt1> [output_dir] [format]");
            Console.WriteLine($"  {AppName} <input_dir>      [output_dir] [format]");
            Console.WriteLine();
            Console.WriteLine("Run without arguments to start the Graphical User Interface (GUI).");
        }
    }
}
