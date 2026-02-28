using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XenusDt1Decompiler
{
    public class MainForm : Form
    {
        private TextBox txtInput = null!;
        private TextBox txtOutput = null!;
        private TextBox txtVeloader = null!;
        private ComboBox cmbFormat = null!;
        private Button btnStart = null!;
        private RichTextBox rtbLog = null!;
        private Label lblStatus = null!;
        private Button btnBrowseInput = null!;
        private Button btnBrowseOutput = null!;
        private Button btnBrowseVeloader = null!;

        public MainForm()
        {
            InitializeComponent();
            CheckDefaultVeloader();
            txtOutput.Text = Path.Combine(Directory.GetCurrentDirectory(), "out_tex");
        }

        private void InitializeComponent()
        {
            this.Text = "Xenus 2 DT1/DT2 Decompiler";
            this.Size = new Size(680, 520);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(500, 400);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            var icoPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(icoPath))
                this.Icon = new Icon(icoPath);

            var panelTop = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 4,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10)
            };
            panelTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            panelTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panelTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            
            for (int i = 0; i < 4; i++)
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

            // Row 3: VELoader
            panelTop.Controls.Add(new Label { Text = "VELoader.dll:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            txtVeloader = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 5, 5, 0) };
            panelTop.Controls.Add(txtVeloader, 1, 2);
            btnBrowseVeloader = new Button { Text = "Browse...", Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3) };
            btnBrowseVeloader.Click += BtnBrowseVeloader_Click;
            panelTop.Controls.Add(btnBrowseVeloader, 2, 2);

            // Row 4: Format
            panelTop.Controls.Add(new Label { Text = "Output Format:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
            
            cmbFormat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new Padding(0, 5, 5, 0) };
            cmbFormat.Items.AddRange(new object[] { "Auto (from filename)", "dds", "tga", "bmp", "png", "jpg" });
            cmbFormat.SelectedIndex = 0;
            panelTop.Controls.Add(cmbFormat, 1, 3);

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
                ScrollBars = RichTextBoxScrollBars.None
            };
            pnlHost.Controls.Add(rtbLog);

            // Container for the host panel, this handles outside margins
            var panelMiddle = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            panelMiddle.Controls.Add(pnlHost);
            this.Controls.Add(panelMiddle);
            
            // Critical fix: make sure the Fill panel is evaluated last during Docking
            // by bringing it to the front of the Z-order.
            panelMiddle.BringToFront();
        }

        private void CheckDefaultVeloader()
        {
            string defaultPath = DecompilerCore.ResolveDefaultVELoader();
            if (File.Exists(defaultPath))
            {
                txtVeloader.Text = defaultPath;
            }
        }

        private void LogInfo(string message)
        {
            if (rtbLog.InvokeRequired)
            {
                rtbLog.Invoke(new Action<string>(LogInfo), message);
                return;
            }
            rtbLog.AppendText(message + Environment.NewLine);
            rtbLog.ScrollToCaret();
        }

        private void LogError(string message)
        {
            if (rtbLog.InvokeRequired)
            {
                rtbLog.Invoke(new Action<string>(LogError), message);
                return;
            }
            rtbLog.SelectionStart = rtbLog.TextLength;
            rtbLog.SelectionLength = 0;
            rtbLog.SelectionColor = Color.LightCoral;
            rtbLog.AppendText(message + Environment.NewLine);
            rtbLog.SelectionColor = rtbLog.ForeColor;
            rtbLog.ScrollToCaret();
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

        private void BtnBrowseVeloader_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog { Filter = "DLL Files (*.dll)|*.dll|All Files (*.*)|*.*", Title = "Select VELoader.dll" };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                txtVeloader.Text = ofd.FileName;
            }
        }

        private async void BtnStart_Click(object? sender, EventArgs e)
        {
            string input = txtInput.Text.Trim();
            string output = txtOutput.Text.Trim();
            string veloader = txtVeloader.Text.Trim();
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

            if (string.IsNullOrEmpty(veloader) || !File.Exists(veloader))
            {
                MessageBox.Show("VELoader.dll not found. Please provide a valid path.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SetUIState(false);
            rtbLog.Clear();
            LogInfo("--- Starting Decompilation ---");
            LogInfo($"Input: {input}");
            LogInfo($"Output: {output}");
            LogInfo($"Format: {(string.IsNullOrEmpty(ext) ? "Auto" : ext)}");
            LogInfo("");

            var progress = new Progress<string>(msg => lblStatus.Text = msg);

            await Task.Run(() =>
            {
                var res = DecompilerCore.DecodeDirectory(input, output, veloader, ext, LogInfo, LogError);
                LogInfo("");
                LogInfo($"--- Finished. OK: {res.Ok}, FAIL: {res.Fail} ---");
                ((IProgress<string>)progress).Report($"Done. OK: {res.Ok}, FAIL: {res.Fail}");
            });

            SetUIState(true);
        }

        private void SetUIState(bool enabled)
        {
            txtInput.Enabled = enabled;
            txtOutput.Enabled = enabled;
            txtVeloader.Enabled = enabled;
            cmbFormat.Enabled = enabled;
            btnStart.Enabled = enabled;
            btnBrowseInput.Enabled = enabled;
            btnBrowseOutput.Enabled = enabled;
            btnBrowseVeloader.Enabled = enabled;

            if (!enabled)
            {
                btnStart.Text = "WORKING...";
                lblStatus.Text = "Processing files...";
            }
            else
            {
                btnStart.Text = "START DECOMPILE";
            }
        }
    }
}
