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

        private readonly AppSettings settings = AppSettings.Load();
        private Theme theme;

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
        private System.Windows.Forms.Timer? logTimer;
        private readonly List<PaletteSwatch> swatches = new();

        // Every log line shown so far, so the log can be redrawn in new colours after a palette change.
        private readonly List<string> logHistory = new();

        // Status line colour role, kept across palette changes.
        private enum StatusKind { Muted, Success, Warning, Error }
        private StatusKind statusKind = StatusKind.Muted;

        private CancellationTokenSource? cts;

        // Log messages are queued here (from worker threads) and flushed to the RichTextBox by a UI timer.
        private readonly ConcurrentQueue<string> logQueue = new();

        public MainForm()
        {
            theme = Theme.Load(settings.Palette);
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
            rtbLog.SelectionStart = rtbLog.TextLength;
            rtbLog.SelectionColor = theme.Muted;
            rtbLog.AppendText(
                "Choose the game's CACHE\\TEXTURES folder (or a single .DT1 file) and press Start.\n" +
                "You can also drop a folder or a file onto this window.");
        }

        private void InitializeComponent()
        {
            bool firstTime = logTimer is null; // later calls rebuild the controls for another palette
            if (firstTime)
            {
                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                Text = "Xenus DT1 Decompiler" + (ver is null ? "" : $" v{ver.Major}.{ver.Minor}.{ver.Build}");
                Size = new Size(820, 600);
                MinimumSize = new Size(620, 460);
                StartPosition = FormStartPosition.CenterScreen;
                // The icon is embedded in the exe (ApplicationIcon); single-file builds have no app.ico next to it.
                if (Environment.ProcessPath is { } exePath)
                    Icon = Icon.ExtractAssociatedIcon(exePath);
            }
            Font = theme.UiFont;
            BackColor = theme.Back;
            ForeColor = theme.Text;
            Padding = new Padding(16, 14, 16, 14);

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
            AddRow(top, "Format", formatSwitch, BuildThemePicker());

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
            var dropTargets = new List<Control> { top, middle, logFrame, rtbLog, txtInput };
            if (firstTime) dropTargets.Add(this);
            foreach (var c in dropTargets)
            {
                c.AllowDrop = true;
                c.DragEnter += Drop_DragEnter;
                c.DragDrop += Drop_DragDrop;
            }

            if (firstTime)
            {
                logTimer = new System.Windows.Forms.Timer { Interval = 100 };
                logTimer.Tick += (_, _) => FlushLog();
                logTimer.Start();
            }
        }

        // Round swatches (Mono, White Gold) at the right end of the format row.
        private Control BuildThemePicker()
        {
            var picker = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, 10, 0, 0),
                BackColor = theme.Back,
                AccessibleName = "Theme",
            };
            var tip = new ToolTip();
            swatches.Clear();
            foreach (var palette in Theme.PaletteList)
            {
                var swatch = new PaletteSwatch(palette.Key, palette.Name) { Checked = palette.Key == theme.PaletteKey };
                tip.SetToolTip(swatch, $"Theme: {palette.Name}");
                swatch.CheckedChanged += (_, _) => { if (swatch.Checked) SwitchPalette(swatch.PaletteKey); };
                swatches.Add(swatch);
                picker.Controls.Add(swatch);
            }
            return picker;
        }

        // Rebuilds the window's controls in another palette, keeping the inputs, the status and the log.
        private void SwitchPalette(string key)
        {
            if (key == theme.PaletteKey || cts is not null) return;
            BeginInvoke(() =>
            {
                string input = txtInput.Text, output = txtOutput.Text, status = lblStatus.Text;
                int format = formatSwitch.SelectedIndex;
                float shown = progress.Value;
                int max = progress.Maximum;
                var lines = logHistory.ToList();

                SuspendLayout();
                theme = Theme.Load(key);
                foreach (var c in Controls.Cast<Control>().ToList())
                    c.Dispose();
                Controls.Clear();
                InitializeComponent();
                txtInput.Text = input;
                txtOutput.Text = output;
                formatSwitch.SelectedIndex = format;
                lblFormatHint.Text = formatSwitch.SelectedOption.Description;
                progress.Maximum = max;
                progress.Value = shown;
                SetStatus(status, statusKind);
                UpdateOpenOutputButton();
                ResumeLayout();

                theme.ApplyChrome(this);
                theme.UseDarkScrollbars(rtbLog);
                logHistory.Clear();
                if (lines.Count == 0)
                    ShowEmptyLogHint();
                foreach (var line in lines)
                    logQueue.Enqueue(line);
                FlushLog();

                SaveSettings();
                swatches.First(sw => sw.PaletteKey == key).Focus();
            });
        }

        private void SetStatus(string text, StatusKind kind)
        {
            statusKind = kind;
            lblStatus.Text = text;
            lblStatus.ForeColor = kind switch
            {
                StatusKind.Success => theme.Success,
                StatusKind.Warning => theme.Warning,
                StatusKind.Error => theme.Error,
                _ => theme.Muted,
            };
        }

        private TextBox NewTextBox(string placeholder) => new()
        {
            BorderStyle = BorderStyle.None,
            PlaceholderText = placeholder,
            ForeColor = theme.Text,
            Font = theme.UiFont,
        };

        private FieldFrame Field(TextBox box) =>
            new(box, new Padding(14, 8, 14, 4), theme.Surface) { Height = 32, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 8, 4) };

        private void AddRow(TableLayoutPanel panel, string label, Control field, Control? button, int span = 1)
        {
            int row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            // Font set explicitly so PreferredHeight below is measured with the font the label will use.
            var caption = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = theme.Muted, Font = theme.UiFont };
            int top = 4;
            if (field is SegmentedControl s)
            {
                // Centre the caption on the switch.
                top = s.Margin.Top + (s.Height - caption.PreferredHeight) / 2;
            }
            caption.Margin = new Padding(0, top, 14, 4);
            panel.Controls.Add(caption, 0, row);
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
            settings.Palette = theme.PaletteKey;
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
                logHistory.Add(line);
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
            logHistory.Clear();
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

            SetStatus(
                cancelled ? $"Cancelled: {res.Ok} OK, {res.Fail} failed" : $"Done: {res.Ok} OK, {res.Fail} failed",
                res.Fail > 0 ? StatusKind.Error : cancelled ? StatusKind.Warning : StatusKind.Success);
        }

        // Progress reported by worker threads; the UI timer below eases the shown count towards it.
        private int targetDone, targetTotal;
        private float shownDone;
        private System.Windows.Forms.Timer? progressTimer;

        // Called from worker threads: only records the numbers, no UI calls.
        private void ReportProgress(int done, int total)
        {
            Volatile.Write(ref targetTotal, total);
            int current;
            do
            {
                current = Volatile.Read(ref targetDone);
                if (done <= current) return;
            } while (Interlocked.CompareExchange(ref targetDone, done, current) != current);
        }

        private void AnimateProgress()
        {
            int total = Volatile.Read(ref targetTotal);
            if (total == 0) return; // still collecting files: the bar shows the running segment
            int target = Volatile.Read(ref targetDone);

            progress.Indeterminate = false;
            progress.Maximum = total;
            shownDone = Motion.Enabled && target - shownDone > 0.5f
                ? shownDone + (target - shownDone) * 0.25f
                : target;
            progress.Value = shownDone;
            lblStatus.Text = $"Processing… {(int)MathF.Round(shownDone):N0} / {total:N0}";
        }

        private void SetRunning(bool running)
        {
            txtInput.ReadOnly = running;
            txtOutput.ReadOnly = running;
            formatSwitch.Enabled = !running;
            foreach (var sw in swatches) sw.Enabled = !running;
            btnBrowseInput.Enabled = !running;
            btnBrowseOutput.Enabled = !running;
            btnStart.Enabled = true;
            btnStart.Text = running ? "Cancel" : "Start";
            UpdateOpenOutputButton();

            progressTimer ??= new System.Windows.Forms.Timer { Interval = 16 };
            if (running)
            {
                targetDone = targetTotal = 0;
                shownDone = 0;
                progress.Value = 0;
                progress.Indeterminate = true;
                progress.Busy = true;
                SetStatus("Collecting files…", StatusKind.Muted);
                progressTimer.Tick -= ProgressTimer_Tick;
                progressTimer.Tick += ProgressTimer_Tick;
                progressTimer.Start();
            }
            else
            {
                progressTimer.Stop();
                progress.Busy = false;
                progress.Indeterminate = false;
                if (targetTotal > 0)
                {
                    progress.Maximum = targetTotal;
                    progress.Value = shownDone = targetDone;
                }
            }
        }

        private void ProgressTimer_Tick(object? sender, EventArgs e) => AnimateProgress();
    }
}
