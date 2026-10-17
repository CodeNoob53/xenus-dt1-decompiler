using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XenusDt1Decompiler
{
    public class MainForm : Form
    {
        // (shown text, format argument passed to DecompilerCore; "" = Auto)
        private static readonly (string Text, string Ext)[] Formats =
        {
            ("Auto: original format from the file name (recommended)", ""),
            ("DDS: exactly as stored in the cache", "dds"),
            ("TGA", "tga"),
            ("PNG", "png"),
            ("BMP", "bmp"),
            ("JPG (lossy)", "jpg"),
        };

        private static readonly string DefaultOutput = Path.Combine(AppContext.BaseDirectory, "out_tex");

        private readonly Theme theme = Theme.Current();
        private readonly AppSettings settings = AppSettings.Load();

        private TextBox txtInput = null!;
        private TextBox txtOutput = null!;
        private ComboBox cmbFormat = null!;
        private Button btnBrowseInput = null!;
        private Button btnBrowseOutput = null!;
        private Button btnOpenOutput = null!;
        private Button btnStart = null!;
        private ProgressBar progress = null!;
        private Label lblStatus = null!;
        private RichTextBox rtbLog = null!;
        private ToolTip toolTip = null!;
        private System.Windows.Forms.Timer logTimer = null!;

        private CancellationTokenSource? cts;

        // Log messages are queued here (from worker threads) and flushed to the RichTextBox by a UI timer.
        private readonly ConcurrentQueue<string> logQueue = new();

        public MainForm()
        {
            InitializeComponent();

            txtInput.Text = settings.InputPath ?? "";
            txtOutput.Text = string.IsNullOrWhiteSpace(settings.OutputPath) ? DefaultOutput : settings.OutputPath;
            int formatIndex = Array.FindIndex(Formats, f => f.Ext == (settings.Format ?? ""));
            cmbFormat.SelectedIndex = formatIndex >= 0 ? formatIndex : 0;
            UpdateOpenOutputButton();
        }

        private void InitializeComponent()
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string verStr = ver is null ? "" : $" v{ver.Major}.{ver.Minor}.{ver.Build}";
            Text = $"Xenus DT1 Decompiler{verStr}";
            Size = new Size(760, 560);
            MinimumSize = new Size(560, 420);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            // The icon is embedded in the exe (ApplicationIcon); single-file builds have no app.ico next to it.
            if (Environment.ProcessPath is { } exePath)
                Icon = Icon.ExtractAssociatedIcon(exePath);

            toolTip = new ToolTip { AutoPopDelay = 15000 };

            // Top: input, output, format
            var panelTop = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 3,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 10, 10, 0),
            };
            panelTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panelTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panelTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            txtInput = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3, 5, 6, 3), PlaceholderText = "Folder with .DT1 files, or a single .DT1 file (you can drop it here)" };
            btnBrowseInput = new Button { Text = "Browse…", AutoSize = true, Margin = new Padding(0, 3, 0, 3) };
            btnBrowseInput.Click += BtnBrowseInput_Click;
            AddRow(panelTop, 0, "Input:", txtInput, btnBrowseInput);

            txtOutput = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3, 5, 6, 3) };
            btnBrowseOutput = new Button { Text = "Browse…", AutoSize = true, Margin = new Padding(0, 3, 0, 3) };
            btnBrowseOutput.Click += BtnBrowseOutput_Click;
            txtOutput.TextChanged += (_, _) => UpdateOpenOutputButton();
            AddRow(panelTop, 1, "Output:", txtOutput, btnBrowseOutput);

            cmbFormat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(3, 5, 6, 3) };
            foreach (var f in Formats)
                cmbFormat.Items.Add(f.Text);
            toolTip.SetToolTip(cmbFormat,
                "Auto: the format the texture had before the engine cached it (KURSOR_1_TGA.DT1 → KURSOR_1.tga).\n" +
                "This is the format to use when putting edited textures back into the game.\n\n" +
                "DDS: the texture exactly as stored in the cache.\n" +
                "TGA / PNG / BMP / JPG: converted with texconv. JPG is lossy.");
            AddRow(panelTop, 2, "Format:", cmbFormat, null);

            // Bottom: progress bar, status, buttons
            var panelBottom = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 2,
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10, 0, 10, 10),
            };
            panelBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panelBottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panelBottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            progress = new ProgressBar { Dock = DockStyle.Fill, Height = 8, Margin = new Padding(3, 0, 3, 6), Style = ProgressBarStyle.Continuous };
            panelBottom.Controls.Add(progress, 0, 0);
            panelBottom.SetColumnSpan(progress, 3);

            lblStatus = new Label { Text = "Ready", AutoSize = true, Anchor = AnchorStyles.Left, Tag = "muted" };
            panelBottom.Controls.Add(lblStatus, 0, 1);

            btnOpenOutput = new Button { Text = "Open output folder", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
            btnOpenOutput.Click += BtnOpenOutput_Click;
            panelBottom.Controls.Add(btnOpenOutput, 1, 1);

            btnStart = new Button { Text = "Start", AutoSize = true, MinimumSize = new Size(110, 0), Padding = new Padding(8, 2, 8, 2), Font = new Font(Font, FontStyle.Bold) };
            btnStart.Click += BtnStart_Click;
            panelBottom.Controls.Add(btnStart, 2, 1);
            AcceptButton = btnStart;

            // Middle: log
            rtbLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9F),
                WordWrap = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
            };
            var panelLog = new Panel { Dock = DockStyle.Fill, Padding = new Padding(13, 8, 13, 8) };
            panelLog.Controls.Add(rtbLog);

            Controls.Add(panelLog);
            Controls.Add(panelBottom);
            Controls.Add(panelTop);

            // Drag & drop a folder or a single .DT1 file anywhere onto the window.
            AllowDrop = true;
            DragEnter += Drop_DragEnter;
            DragDrop += Drop_DragDrop;
            foreach (var c in new Control[] { panelTop, txtInput, rtbLog, panelLog })
            {
                c.AllowDrop = true;
                c.DragEnter += Drop_DragEnter;
                c.DragDrop += Drop_DragDrop;
            }

            logTimer = new System.Windows.Forms.Timer { Interval = 100 };
            logTimer.Tick += (_, _) => FlushLog();
            logTimer.Start();
        }

        private void AddRow(TableLayoutPanel panel, int row, string label, Control field, Control? button)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 6, 3) }, 0, row);
            panel.Controls.Add(field, 1, row);
            if (button is not null)
                panel.Controls.Add(button, 2, row);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            theme.Apply(this);
            progress.ForeColor = theme.Success;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            cts?.Cancel();
            SaveSettings();
            base.OnFormClosing(e);
        }

        private void SaveSettings()
        {
            settings.InputPath = txtInput.Text.Trim();
            settings.OutputPath = txtOutput.Text.Trim();
            settings.Format = Formats[Math.Max(0, cmbFormat.SelectedIndex)].Ext;
            settings.Save();
        }

        // ---- Log -------------------------------------------------------------------------------

        private void Log(string message) => logQueue.Enqueue(message);

        private Color ColorFor(string line) =>
            line.StartsWith("[FAIL]") ? theme.Error :
            line.StartsWith("[WARN]") ? theme.Warning :
            line.StartsWith("[INFO]") ? theme.Info :
            line.StartsWith("Done in") ? theme.Success :
            theme.LogText;

        // Drains the queue and appends messages in batches of the same colour.
        private void FlushLog()
        {
            if (logQueue.IsEmpty) return;

            var sb = new StringBuilder();
            Color current = theme.LogText;

            void Append()
            {
                if (sb.Length == 0) return;
                rtbLog.SelectionStart = rtbLog.TextLength;
                rtbLog.SelectionLength = 0;
                rtbLog.SelectionColor = current;
                rtbLog.AppendText(sb.ToString());
                sb.Clear();
            }

            while (logQueue.TryDequeue(out var line))
            {
                var color = ColorFor(line);
                if (color != current)
                {
                    Append();
                    current = color;
                }
                sb.AppendLine(line);
            }
            Append();
            rtbLog.ScrollToCaret();
        }

        // ---- Input / output --------------------------------------------------------------------

        private void Drop_DragEnter(object? sender, DragEventArgs e)
        {
            if (cts is null && e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        }

        private void Drop_DragDrop(object? sender, DragEventArgs e)
        {
            if (cts is not null) return; // ignore drops while working
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
                return;

            var path = paths[0];
            if (Directory.Exists(path) || (File.Exists(path) && DecompilerCore.IsDt1OrDt2(path)))
                txtInput.Text = path; // a dropped file is processed on its own, not its whole folder
        }

        private static string? ExistingFolder(string path)
        {
            path = path.Trim();
            if (Directory.Exists(path)) return path;
            if (File.Exists(path)) return Path.GetDirectoryName(path);
            return null;
        }

        private void BtnBrowseInput_Click(object? sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog { Description = "Select the folder with .DT1 files (e.g. CACHE\\TEXTURES)", UseDescriptionForTitle = true };
            if (ExistingFolder(txtInput.Text) is { } start)
                dlg.InitialDirectory = start;
            if (dlg.ShowDialog(this) == DialogResult.OK)
                txtInput.Text = dlg.SelectedPath;
        }

        private void BtnBrowseOutput_Click(object? sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog { Description = "Select the output folder", UseDescriptionForTitle = true };
            dlg.InitialDirectory = ExistingFolder(txtOutput.Text) ?? AppContext.BaseDirectory;
            if (dlg.ShowDialog(this) == DialogResult.OK)
                txtOutput.Text = dlg.SelectedPath;
        }

        private void UpdateOpenOutputButton() =>
            btnOpenOutput.Enabled = cts is null && Directory.Exists(txtOutput.Text.Trim());

        private void BtnOpenOutput_Click(object? sender, EventArgs e)
        {
            var dir = txtOutput.Text.Trim();
            if (Directory.Exists(dir))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }

        // ---- Run -------------------------------------------------------------------------------

        private async void BtnStart_Click(object? sender, EventArgs e)
        {
            if (cts is not null)
            {
                cts.Cancel();
                btnStart.Enabled = false;
                btnStart.Text = "Cancelling…";
                return;
            }

            string input = txtInput.Text.Trim();
            string output = txtOutput.Text.Trim();
            string ext = Formats[Math.Max(0, cmbFormat.SelectedIndex)].Ext;
            bool singleFile = File.Exists(input) && DecompilerCore.IsDt1OrDt2(input);

            if (!singleFile && !Directory.Exists(input))
            {
                MessageBox.Show(this, "Select a folder with .DT1 files or a single .DT1 file.", "Input not found",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrEmpty(output))
            {
                output = DefaultOutput;
                txtOutput.Text = output;
            }

            SaveSettings();
            cts = new CancellationTokenSource();
            SetRunning(true);
            rtbLog.Clear();
            while (logQueue.TryDequeue(out _)) { } // drop leftovers from a previous run
            Log($"Input:  {input}");
            Log($"Output: {output}");
            Log($"Format: {Formats[Math.Max(0, cmbFormat.SelectedIndex)].Text}");
            Log("");

            var token = cts.Token;
            (int Ok, int Fail) res;
            try
            {
                res = await Task.Run(() =>
                {
                    if (singleFile)
                    {
                        Directory.CreateDirectory(output);
                        string? texconv = string.IsNullOrEmpty(ext) || ext != "dds" ? DecompilerCore.ResolveTexconv() : null;
                        bool ok = DecompilerCore.DecodeOneFile(input, output, null, ext, texconv, Log, Log);
                        ReportProgress(1, 1);
                        return ok ? (1, 0) : (0, 1);
                    }
                    return DecompilerCore.DecodeDirectory(input, output, ext, Log, Log, ReportProgress, token);
                });
            }
            catch (Exception ex)
            {
                Log($"[FAIL] {ex.Message}");
                res = (0, 1);
            }

            bool cancelled = token.IsCancellationRequested;
            cts.Dispose();
            cts = null;
            FlushLog();
            SetRunning(false);

            lblStatus.Text = cancelled
                ? $"Cancelled: {res.Ok} OK, {res.Fail} failed"
                : $"Done: {res.Ok} OK, {res.Fail} failed";
            lblStatus.ForeColor = res.Fail > 0 ? theme.Error : cancelled ? theme.Warning : theme.Success;
        }

        private int lastProgressTick;

        // Called from worker threads; throttled to ~10 UI updates per second, the last one always shown.
        private void ReportProgress(int done, int total)
        {
            int now = Environment.TickCount;
            if (done != total && now - Volatile.Read(ref lastProgressTick) < 100) return;
            Volatile.Write(ref lastProgressTick, now);
            BeginInvoke(() =>
            {
                progress.Maximum = Math.Max(1, total);
                progress.Value = Math.Min(Math.Max(progress.Value, done), progress.Maximum);
                lblStatus.Text = $"Processing… {progress.Value} / {total}";
            });
        }

        private void SetRunning(bool running)
        {
            txtInput.ReadOnly = running;
            txtOutput.ReadOnly = running;
            cmbFormat.Enabled = !running;
            btnBrowseInput.Enabled = !running;
            btnBrowseOutput.Enabled = !running;
            btnStart.Enabled = true;
            btnStart.Text = running ? "Cancel" : "Start";
            UpdateOpenOutputButton();

            if (running)
            {
                progress.Value = 0;
                lblStatus.Text = "Processing…";
                lblStatus.ForeColor = theme.Muted;
            }
        }
    }
}
