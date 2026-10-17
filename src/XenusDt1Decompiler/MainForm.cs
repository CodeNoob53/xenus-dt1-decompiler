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
        // Output formats; Value is the format argument passed to DecompilerCore ("" = Auto).
        private static readonly SegmentedControl.Option[] Formats =
        {
            new("Auto", "", "Original format from the file name (_TGA → .tga). Use it to put textures back into the game."),
            new("DDS", "dds", "Exactly as stored in the game cache, without conversion."),
            new("TGA", "tga", "Converted to TGA with texconv."),
            new("PNG", "png", "Converted to PNG with texconv."),
            new("BMP", "bmp", "Converted to BMP with texconv."),
            new("JPG", "jpg", "Converted to JPG with texconv. Lossy, so not suitable for putting textures back."),
        };

        private static readonly string DefaultOutput = Path.Combine(AppContext.BaseDirectory, "out_tex");

        private readonly Theme theme = Theme.Load();
        private readonly AppSettings settings = AppSettings.Load();

        private TextBox txtInput = null!;
        private TextBox txtOutput = null!;
        private SegmentedControl formatSwitch = null!;
        private Label lblFormatHint = null!;
        private ThemedButton btnBrowseInput = null!;
        private ThemedButton btnBrowseOutput = null!;
        private ThemedButton btnOpenOutput = null!;
        private ThemedButton btnStart = null!;
        private ThinProgressBar progress = null!;
        private Label lblStatus = null!;
        private RichTextBox rtbLog = null!;
        private System.Windows.Forms.Timer logTimer = null!;

        private CancellationTokenSource? cts;

        // Log messages are queued here (from worker threads) and flushed to the RichTextBox by a UI timer.
        private readonly ConcurrentQueue<string> logQueue = new();

        public MainForm()
        {
            InitializeComponent();

            txtInput.Text = settings.InputPath ?? "";
            txtOutput.Text = string.IsNullOrWhiteSpace(settings.OutputPath) ? DefaultOutput : settings.OutputPath;
            int formatIndex = Array.FindIndex(Formats, f => f.Value == (settings.Format ?? ""));
            formatSwitch.SelectedIndex = Math.Max(0, formatIndex);
            lblFormatHint.Text = formatSwitch.SelectedOption.Description;
            UpdateOpenOutputButton();
            ShowEmptyLogHint();
        }

        private void ShowEmptyLogHint()
        {
            rtbLog.SelectionColor = theme.Muted;
            rtbLog.AppendText(
                "Choose the game's CACHE\\TEXTURES folder (or a single .DT1 file) and press Start.\n" +
                "You can also drop a folder or a file onto this window.");
        }

        private void InitializeComponent()
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            Text = "Xenus DT1 Decompiler" + (ver is null ? "" : $" v{ver.Major}.{ver.Minor}.{ver.Build}");
            Size = new Size(820, 600);
            MinimumSize = new Size(620, 460);
            StartPosition = FormStartPosition.CenterScreen;
            Font = theme.UiFont;
            BackColor = theme.Back;
            ForeColor = theme.Text;
            Padding = new Padding(16, 14, 16, 14);
            // The icon is embedded in the exe (ApplicationIcon); single-file builds have no app.ico next to it.
            if (Environment.ProcessPath is { } exePath)
                Icon = Icon.ExtractAssociatedIcon(exePath);

            // Top: input, output, format
            var top = new TableLayoutPanel
            {
                ColumnCount = 3,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = theme.Back,
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            txtInput = NewTextBox("Folder with .DT1 files, or a single .DT1 file (you can drop it onto the window)");
            btnBrowseInput = new ThemedButton { Text = "Browse…", Margin = new Padding(0, 4, 0, 4) };
            btnBrowseInput.Click += BtnBrowseInput_Click;
            AddRow(top, "Input", Field(txtInput), btnBrowseInput);

            txtOutput = NewTextBox("Where to save the textures");
            txtOutput.TextChanged += (_, _) => UpdateOpenOutputButton();
            btnBrowseOutput = new ThemedButton { Text = "Browse…", Margin = new Padding(0, 4, 0, 4) };
            btnBrowseOutput.Click += BtnBrowseOutput_Click;
            AddRow(top, "Output", Field(txtOutput), btnBrowseOutput);

            formatSwitch = new SegmentedControl { Anchor = AnchorStyles.Left, Margin = new Padding(0, 10, 0, 0), AccessibleName = "Format" };
            formatSwitch.SetOptions(Formats);
            formatSwitch.SelectedIndexChanged += (_, _) => lblFormatHint.Text = formatSwitch.SelectedOption.Description;
            AddRow(top, "Format", formatSwitch, null, span: 2);

            lblFormatHint = new Label { AutoSize = true, ForeColor = theme.Muted, Margin = new Padding(2, 6, 0, 2) };
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            top.Controls.Add(lblFormatHint, 1, top.RowCount++);
            top.SetColumnSpan(lblFormatHint, 2);

            // Bottom: progress line, status, actions
            var bottom = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 2,
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = theme.Back,
            };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            progress = new ThinProgressBar { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
            bottom.Controls.Add(progress, 0, 0);
            bottom.SetColumnSpan(progress, 3);

            lblStatus = new Label { Text = "Ready", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = theme.Muted, Margin = new Padding(0) };
            bottom.Controls.Add(lblStatus, 0, 1);

            btnOpenOutput = new ThemedButton { Text = "Open output folder", Margin = new Padding(8, 0, 0, 0) };
            btnOpenOutput.Click += BtnOpenOutput_Click;
            bottom.Controls.Add(btnOpenOutput, 1, 1);

            btnStart = new ThemedButton { Text = "Start", Primary = true, MinimumSize = new Size(120, 32), Font = theme.UiFontStrong, Margin = new Padding(8, 0, 0, 0) };
            btnStart.Click += BtnStart_Click;
            bottom.Controls.Add(btnStart, 2, 1);
            AcceptButton = btnStart;

            // Middle: log
            rtbLog = new RichTextBox
            {
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                Font = theme.MonoFont,
                ForeColor = theme.LogText,
                WordWrap = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                TabStop = false,
            };
            var logFrame = new FieldFrame(rtbLog, new Padding(12, 10, 4, 10), theme.LogBack) { Dock = DockStyle.Fill };
            var middle = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 14, 0, 14), BackColor = theme.Back };
            middle.Controls.Add(logFrame);

            Controls.Add(middle);
            Controls.Add(bottom);
            Controls.Add(top);

            // Drag & drop a folder or a single .DT1 file anywhere onto the window.
            foreach (var c in new Control[] { this, top, middle, logFrame, rtbLog, txtInput })
            {
                c.AllowDrop = true;
                c.DragEnter += Drop_DragEnter;
                c.DragDrop += Drop_DragDrop;
            }

            logTimer = new System.Windows.Forms.Timer { Interval = 100 };
            logTimer.Tick += (_, _) => FlushLog();
            logTimer.Start();
        }

        private TextBox NewTextBox(string placeholder) => new()
        {
            BorderStyle = BorderStyle.None,
            PlaceholderText = placeholder,
            ForeColor = theme.Text,
            Font = theme.UiFont,
        };

        private FieldFrame Field(TextBox box) =>
            new(box, new Padding(10, 8, 10, 4), theme.Surface) { Height = 32, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 8, 4) };

        private void AddRow(TableLayoutPanel panel, string label, Control field, Control? button, int span = 1)
        {
            int row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = theme.Muted,
                Margin = new Padding(0, field is SegmentedControl ? 10 : 4, 14, 4),
            }, 0, row);
            panel.Controls.Add(field, 1, row);
            panel.SetColumnSpan(field, span);
            if (button is not null)
                panel.Controls.Add(button, 2, row);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            theme.ApplyChrome(this);
            theme.UseDarkScrollbars(rtbLog);
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
            settings.Format = formatSwitch.SelectedOption.Value;
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
            string ext = formatSwitch.SelectedOption.Value;
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
            Log($"Format: {formatSwitch.SelectedOption.Text}");
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
                        string? texconv = ext != "dds" ? DecompilerCore.ResolveTexconv() : null;
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
                progress.Maximum = total;
                progress.Value = Math.Max(progress.Value, done);
                lblStatus.Text = $"Processing… {progress.Value} / {total}";
            });
        }

        private void SetRunning(bool running)
        {
            txtInput.ReadOnly = running;
            txtOutput.ReadOnly = running;
            formatSwitch.Enabled = !running;
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
