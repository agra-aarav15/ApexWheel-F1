using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Reflection;

namespace ApexWheelSetup
{
    public class SetupForm : Form
    {
        private ProgressBar _prog;
        private Label _lblStatus;
        private TextBox _txtPath;
        private Button _btnInstall;
        private CheckBox _chkViGEm;
        private CheckBox _chkDesktop;
        private CheckBox _chkLaunch;
        private bool _done = false;

        public SetupForm()
        {
            this.Text = "ApexWheel F1 — Windows Setup";
            this.Size = new Size(580, 440);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(18, 14, 28);
            this.ForeColor = Color.FromArgb(245, 240, 255);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Banner
            Panel banner = new Panel();
            banner.Dock = DockStyle.Top;
            banner.Height = 80;
            banner.BackColor = Color.FromArgb(26, 20, 42);
            banner.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(50, 38, 78), 1))
                {
                    e.Graphics.DrawLine(pen, 0, banner.Height - 1, banner.Width, banner.Height - 1);
                }
            };
            this.Controls.Add(banner);

            Label lblTitle = new Label();
            lblTitle.Text = "🏎️ ApexWheel F1";
            lblTitle.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(168, 85, 247);
            lblTitle.Location = new Point(24, 14);
            lblTitle.AutoSize = true;
            banner.Controls.Add(lblTitle);

            Label lblSub = new Label();
            lblSub.Text = "Phone Steering Wheel & EA Sports F1 25 UDP Telemetry Companion";
            lblSub.Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);
            lblSub.ForeColor = Color.FromArgb(170, 160, 190);
            lblSub.Location = new Point(26, 48);
            lblSub.AutoSize = true;
            banner.Controls.Add(lblSub);

            // Content Panel
            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(26);
            this.Controls.Add(body);

            Label lblPath = new Label();
            lblPath.Text = "Destination Folder:";
            lblPath.Location = new Point(26, 96);
            lblPath.AutoSize = true;
            this.Controls.Add(lblPath);

            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexWheel");
            _txtPath = new TextBox();
            _txtPath.Text = defaultPath;
            _txtPath.Location = new Point(26, 122);
            _txtPath.Size = new Size(420, 26);
            _txtPath.BackColor = Color.FromArgb(32, 25, 52);
            _txtPath.ForeColor = Color.White;
            _txtPath.BorderStyle = BorderStyle.FixedSingle;
            this.Controls.Add(_txtPath);

            Button btnBrowse = new Button();
            btnBrowse.Text = "Browse...";
            btnBrowse.Location = new Point(454, 120);
            btnBrowse.Size = new Size(88, 28);
            btnBrowse.FlatStyle = FlatStyle.Flat;
            btnBrowse.FlatAppearance.BorderColor = Color.FromArgb(80, 60, 120);
            btnBrowse.BackColor = Color.FromArgb(36, 28, 58);
            btnBrowse.Click += (s, e) =>
            {
                using (var fbd = new FolderBrowserDialog())
                {
                    fbd.SelectedPath = _txtPath.Text;
                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        _txtPath.Text = Path.Combine(fbd.SelectedPath, "ApexWheel");
                    }
                }
            };
            this.Controls.Add(btnBrowse);

            _chkViGEm = new CheckBox();
            _chkViGEm.Text = "Install Nefarius ViGEmBus Driver (Xbox 360 controller emulation)";
            _chkViGEm.Checked = true;
            _chkViGEm.Location = new Point(26, 162);
            _chkViGEm.AutoSize = true;
            this.Controls.Add(_chkViGEm);

            _chkDesktop = new CheckBox();
            _chkDesktop.Text = "Create Desktop Shortcut";
            _chkDesktop.Checked = true;
            _chkDesktop.Location = new Point(26, 192);
            _chkDesktop.AutoSize = true;
            this.Controls.Add(_chkDesktop);

            _chkLaunch = new CheckBox();
            _chkLaunch.Text = "Launch ApexWheel F1 after installation";
            _chkLaunch.Checked = true;
            _chkLaunch.Location = new Point(26, 222);
            _chkLaunch.AutoSize = true;
            this.Controls.Add(_chkLaunch);

            _prog = new ProgressBar();
            _prog.Location = new Point(26, 262);
            _prog.Size = new Size(516, 18);
            _prog.Visible = false;
            this.Controls.Add(_prog);

            _lblStatus = new Label();
            _lblStatus.Text = "Ready to install.";
            _lblStatus.ForeColor = Color.FromArgb(170, 160, 190);
            _lblStatus.Location = new Point(26, 288);
            _lblStatus.Size = new Size(516, 22);
            this.Controls.Add(_lblStatus);

            // Bottom Buttons
            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 60;
            footer.BackColor = Color.FromArgb(24, 18, 38);
            this.Controls.Add(footer);

            Button btnCancel = new Button();
            btnCancel.Text = "Cancel";
            btnCancel.Location = new Point(454, 16);
            btnCancel.Size = new Size(88, 30);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(70, 55, 105);
            btnCancel.Click += (s, e) => this.Close();
            footer.Controls.Add(btnCancel);

            _btnInstall = new Button();
            _btnInstall.Text = "Install";
            _btnInstall.Location = new Point(356, 16);
            _btnInstall.Size = new Size(90, 30);
            _btnInstall.FlatStyle = FlatStyle.Flat;
            _btnInstall.BackColor = Color.FromArgb(168, 85, 247);
            _btnInstall.ForeColor = Color.White;
            _btnInstall.FlatAppearance.BorderSize = 0;
            _btnInstall.Click += BtnInstall_Click;
            footer.Controls.Add(_btnInstall);
        }

        private void BtnInstall_Click(object sender, EventArgs e)
        {
            if (_done)
            {
                if (_chkLaunch.Checked)
                {
                    string targetExe = Path.Combine(_txtPath.Text, "ApexWheelServer.exe");
                    if (File.Exists(targetExe))
                    {
                        Process.Start(new ProcessStartInfo(targetExe) { WorkingDirectory = _txtPath.Text });
                    }
                }
                this.Close();
                return;
            }

            _btnInstall.Enabled = false;
            _prog.Visible = true;
            _prog.Value = 10;
            _lblStatus.Text = "Creating installation directory...";

            string dest = _txtPath.Text;
            try
            {
                if (!Directory.Exists(dest)) Directory.CreateDirectory(dest);
                _prog.Value = 25;

                // Copy ApexWheelServer.exe
                string sourceDir = AppDomain.CurrentDomain.BaseDirectory;
                string serverExe = Path.Combine(sourceDir, "server", "ApexWheelServer.exe");
                if (!File.Exists(serverExe)) serverExe = Path.Combine(sourceDir, "ApexWheelServer.exe");

                if (File.Exists(serverExe))
                {
                    File.Copy(serverExe, Path.Combine(dest, "ApexWheelServer.exe"), true);
                }

                // Copy drivers folder
                string driversSrc = Path.Combine(sourceDir, "drivers");
                string driversDst = Path.Combine(dest, "drivers");
                if (Directory.Exists(driversSrc))
                {
                    if (!Directory.Exists(driversDst)) Directory.CreateDirectory(driversDst);
                    foreach (var f in Directory.GetFiles(driversSrc))
                    {
                        File.Copy(f, Path.Combine(driversDst, Path.GetFileName(f)), true);
                    }
                }

                // Copy client-web
                string webSrc = Path.Combine(sourceDir, "client-web");
                string webDst = Path.Combine(dest, "client-web");
                if (Directory.Exists(webSrc))
                {
                    if (!Directory.Exists(webDst)) Directory.CreateDirectory(webDst);
                    foreach (var f in Directory.GetFiles(webSrc))
                    {
                        File.Copy(f, Path.Combine(webDst, Path.GetFileName(f)), true);
                    }
                }

                // Copy docs and APK
                string apkSrc = Path.Combine(sourceDir, "ApexWheel.apk");
                if (File.Exists(apkSrc)) File.Copy(apkSrc, Path.Combine(dest, "ApexWheel.apk"), true);

                _prog.Value = 50;
                _lblStatus.Text = "Installing ViGEmBus controller driver...";

                // Install ViGEmBus if requested
                if (_chkViGEm.Checked)
                {
                    string vigemExe = Path.Combine(driversDst, "ViGEmBus.exe");
                    if (File.Exists(vigemExe))
                    {
                        try
                        {
                            var psi = new ProcessStartInfo(vigemExe, "/install /quiet");
                            psi.UseShellExecute = true;
                            psi.Verb = "runas";
                            var p = Process.Start(psi);
                            if (p != null) p.WaitForExit(30000);
                        }
                        catch { }
                    }
                }

                _prog.Value = 75;
                _lblStatus.Text = "Creating shortcuts...";

                // Create Desktop Shortcut via WScript.Shell
                if (_chkDesktop.Checked)
                {
                    try
                    {
                        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        string shortcutPath = Path.Combine(desktopPath, "ApexWheel F1.lnk");
                        CreateShortcut(shortcutPath, Path.Combine(dest, "ApexWheelServer.exe"), dest, "ApexWheel F1 Steering Controller");
                    }
                    catch { }
                }

                // Start Menu Shortcut
                try
                {
                    string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ApexWheel F1");
                    if (!Directory.Exists(startMenu)) Directory.CreateDirectory(startMenu);
                    CreateShortcut(Path.Combine(startMenu, "ApexWheel F1.lnk"), Path.Combine(dest, "ApexWheelServer.exe"), dest, "ApexWheel F1");
                }
                catch { }

                _prog.Value = 100;
                _lblStatus.Text = "Installation complete!";
                _btnInstall.Text = "Finish";
                _btnInstall.Enabled = true;
                _btnInstall.BackColor = Color.FromArgb(0, 230, 118);
                _btnInstall.ForeColor = Color.Black;
                _done = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Installation error: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _btnInstall.Enabled = true;
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = workingDir;
            shortcut.Description = description;
            shortcut.Save();
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }
}
