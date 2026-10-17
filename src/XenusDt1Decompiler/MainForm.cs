using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XenusDt1Decompiler
{
    public class MainForm : Form
    {
        private TextBox txtInput = null!;
        private TextBox txtOutput = null!;
        private ComboBox cmbFormat = null!;
        private Button btnStart = null!;
        private RichTextBox rtbLog = null!;
        private Label lblStatus = null!;
        private Button btnBrowseInput = null!;
        private Button btnBrowseOutput = null!;
        private System.Windows.Forms.Timer logTimer = null!;

        // Log messages are queued here and flushed to the RichTextBox by a UI timer.
        // This avoids thousands of cross-thread Invoke calls on large directories.
        private readonly ConcurrentQueue<(string Message, bool IsError)> logQueue = new();

        public MainForm()
        {
            InitializeComponent();
            txtOutput.Text = Path.Combine(Directory.GetCurrentDirectory(), "out_tex");
        }

        private void InitializeComponent()
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string verStr = ver is null ? "" : $" v{ver.Major}.{ver.Minor}.{ver.Build}";
            this.Text = $"Xenus DT1/DT2 Decompiler{verStr}";
            this.Size = new Size(680, 520);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(500, 400);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            // The icon is embedded in the exe (ApplicationIcon); single-file builds have no app.ico next to it.
            if (Environment.ProcessPath is { } exePath)
                this.Icon = Icon.ExtractAssociatedIcon(exePath);

            var panelTop = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 3,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10)
            };
            panelTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            panelTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panelTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

            for (int i = 0; i < 3; i++)
            {
                panelTop.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            // Row 1: Input
            panelTop.Controls.Add(new Label { Text = "Input Path:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            txtInput = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 5, 5, 0) };
            panelTop.Controls.Add(txtInput, 1, 0);
            btnBrowseInput = new Button { Text = "Browse...", Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
            btnBrowseInput.Click += BtnBrowseInput_Click;
            panelTop.Controls.Add(btnBrowseInput, 2, 0);

            // Row 2: Output
            panelTop.Controls.Add(new Label { Text = "Output Path:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            txtOutput = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 5, 5, 0) };
            panelTop.Controls.Add(txtOutput, 1, 1);
            btnBrowseOutput = new Button { Text = "Browse...", Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
            btnBrowseOutput.Click += BtnBrowseOutput_Click;
            panelTop.Controls.Add(btnBrowseOutput, 2, 1);

            // Row 3: Format
            panelTop.Controls.Add(new Label { Text = "Output Format:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);

            cmbFormat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new Padding(0, 5, 5, 0) };
            cmbFormat.Items.AddRange(new object[] { "Auto (from filename)", "dds", "tga", "bmp", "png", "jpg" });
            cmbFormat.SelectedIndex = 0;
            panelTop.Controls.Add(cmbFormat, 1, 2);

            this.Controls.Add(panelTop);

            var panelBottom = new Panel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(10) };
            btnStart = new Button { Text = "START DECOMPILE", Dock = DockStyle.Right, Width = 150, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            btnStart.Click += BtnStart_Click;
            lblStatus = new Label { Text = "Ready", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Gray };
            panelBottom.Controls.Add(btnStart);
            panelBottom.Controls.Add(lblStatus);
            this.Controls.Add(panelBottom);

            var pnlHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30),
                Padding = new Padding(10)
            };

            rtbLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.LightGray,
                Font = new Font("Consolas", 9F),
                BorderStyle = BorderStyle.None,
                WordWrap = true,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            pnlHost.Controls.Add(rtbLog);

            // Container for the host panel, this handles outside margins
            var panelMiddle = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            panelMiddle.Controls.Add(pnlHost);
            this.Controls.Add(panelMiddle);

            // Critical fix: make sure the Fill panel is evaluated last during Docking
            // by bringing it to the front of the Z-order.
            panelMiddle.BringToFront();

            // Drag & drop: drop a folder (or a DT1/DT2 file) onto the window to set the input path
            void EnableDrop(Control c)
            {
                c.AllowDrop = true;
                c.DragEnter += Drop_DragEnter;
                c.DragDrop += Drop_DragDrop;
            }
            EnableDrop(this);
            EnableDrop(panelTop);
            EnableDrop(txtInput);
            EnableDrop(rtbLog);

            logTimer = new System.Windows.Forms.Timer { Interval = 100 };
            logTimer.Tick += (_, _) => FlushLog();
            logTimer.Start();
        }

        private void LogInfo(string message) => logQueue.Enqueue((message, false));

        private void LogError(string message) => logQueue.Enqueue((message, true));

        // Drains the queue and appends messages in batches, grouping
        // consecutive same-colored messages into a single AppendText call.
        private void FlushLog()
        {
            if (logQueue.IsEmpty) return;

            var sb = new StringBuilder();
            bool currentIsError = false;
            bool first = true;

            void Append()
            {
                if (sb.Length == 0) return;
                rtbLog.SelectionStart = rtbLog.TextLength;
                rtbLog.SelectionLength = 0;
                rtbLog.SelectionColor = currentIsError ? Color.LightCoral : rtbLog.ForeColor;
                rtbLog.AppendText(sb.ToString());
                rtbLog.SelectionColor = rtbLog.ForeColor;
                sb.Clear();
            }

            while (logQueue.TryDequeue(out var entry))
            {
                if (!first && entry.IsError != currentIsError)
                    Append();
                currentIsError = entry.IsError;
                first = false;
                sb.AppendLine(entry.Message);
            }
            Append();
            rtbLog.ScrollToCaret();
        }

        private void Drop_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        }

        private void Drop_DragDrop(object? sender, DragEventArgs e)
        {
            if (!btnStart.Enabled) return; // ignore drops while working
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
                return;

            var path = paths[0];
            if (Directory.Exists(path))
                txtInput.Text = path;
            else if (File.Exists(path) && DecompilerCore.IsDt1OrDt2(path))
                txtInput.Text = Path.GetDirectoryName(path) ?? path;
        }

        private void BtnBrowseInput_Click(object? sender, EventArgs e)
        {
            using var fbd = new FolderBrowserDialog { Description = "Select folder with DT1/DT2 files" };
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                txtInput.Text = fbd.SelectedPath;
            }
        }

        private void BtnBrowseOutput_Click(object? sender, EventArgs e)
        {
            using var fbd = new FolderBrowserDialog { Description = "Select output folder" };
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                txtOutput.Text = fbd.SelectedPath;
            }
        }

        private async void BtnStart_Click(object? sender, EventArgs e)
        {
            string input = txtInput.Text.Trim();
            string output = txtOutput.Text.Trim();
            string ext = cmbFormat.SelectedIndex > 0 ? cmbFormat.SelectedItem!.ToString()!.Replace(".", "") : "";

            if (string.IsNullOrEmpty(input) || !Directory.Exists(input))
            {
                MessageBox.Show("Please select a valid input directory.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrEmpty(output))
            {
                output = Path.Combine(Directory.GetCurrentDirectory(), "out_tex");
                txtOutput.Text = output;
            }

            SetUIState(false);
            rtbLog.Clear();
            while (logQueue.TryDequeue(out _)) { } // drop leftovers from a previous run
            LogInfo("--- Starting Decompilation ---");
            LogInfo($"Input: {input}");
            LogInfo($"Output: {output}");
            LogInfo($"Format: {(string.IsNullOrEmpty(ext) ? "Auto" : ext)}");
            LogInfo("");

            int lastProgressTick = 0;
            var res = await Task.Run(() =>
                DecompilerCore.DecodeDirectory(input, output, ext, LogInfo, LogError,
                    (done, total) =>
                    {
                        // Throttle UI updates to ~10/s; always show the final count
                        int now = Environment.TickCount;
                        if (done != total && now - lastProgressTick < 100) return;
                        lastProgressTick = now;
                        BeginInvoke(new Action(() => lblStatus.Text = $"Processing... {done}/{total}"));
                    }));

            LogInfo("");
            LogInfo($"--- Finished. OK: {res.Ok}, FAIL: {res.Fail} ---");
            FlushLog();
            SetUIState(true);
            lblStatus.Text = $"Done. OK: {res.Ok}, FAIL: {res.Fail}";
            lblStatus.ForeColor = res.Fail > 0 ? Color.Firebrick : Color.Green;
        }

        private void SetUIState(bool enabled)
        {
            txtInput.Enabled = enabled;
            txtOutput.Enabled = enabled;
            cmbFormat.Enabled = enabled;
            btnStart.Enabled = enabled;
            btnBrowseInput.Enabled = enabled;
            btnBrowseOutput.Enabled = enabled;

            if (!enabled)
            {
                btnStart.Text = "WORKING...";
                lblStatus.Text = "Processing files...";
                lblStatus.ForeColor = Color.Gray;
            }
            else
            {
                btnStart.Text = "START DECOMPILE";
            }
        }
    }
}
