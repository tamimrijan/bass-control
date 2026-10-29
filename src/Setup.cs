// Bass Control Setup - installer wizard. Embeds BassControl.exe and the Equalizer APO installer.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class SetupProgram
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
    }
}

class SetupForm : Form
{
    const int Pages = 4;
    int page;
    readonly Panel[] pages = new Panel[Pages];
    readonly M3Button backBtn, nextBtn, cancelBtn;
    readonly Panel content;

    TextBox pathBox;
    M3Switch desktopSwitch, startupSwitch, eqSwitch, launchSwitch, restartSwitch;
    M3Card launchCard, restartCard;
    M3ProgressBar progress;
    Label progressText, finishText;
    bool installing, installed, eqInstalledNow;
    string error;

    public SetupForm()
    {
        Text = AppInfo.Name + " Setup";
        Icon = AppInfo.LoadIcon(new Size(32, 32));
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = M3.Surface;
        ClientSize = new Size(M3.D(680), M3.D(460));

        var side = new SidePanel { Dock = DockStyle.Left, Width = M3.D(230) };
        Controls.Add(side);

        content = new Panel { Bounds = new Rectangle(M3.D(230), 0, M3.D(450), M3.D(388)), BackColor = M3.Surface };
        Controls.Add(content);

        var footer = new Panel { Bounds = new Rectangle(M3.D(230), M3.D(388), M3.D(450), M3.D(72)), BackColor = M3.Surface };
        Controls.Add(footer);
        footer.Controls.Add(new Panel { BackColor = M3.OutlineVariant, Bounds = new Rectangle(0, 0, M3.D(450), 1) });
        cancelBtn = new M3Button("Cancel", ButtonKind.Text) { Bounds = M3.R(24, 16, 96, 40) };
        backBtn = new M3Button("Back", ButtonKind.Outlined) { Bounds = M3.R(196, 16, 104, 40) };
        nextBtn = new M3Button("Next", ButtonKind.Filled) { Bounds = M3.R(312, 16, 114, 40) };
        cancelBtn.Click += (s, e) => Close();
        backBtn.Click += (s, e) => Go(page - 1);
        nextBtn.Click += (s, e) => Next();
        footer.Controls.AddRange(new Control[] { cancelBtn, backBtn, nextBtn });

        for (int i = 0; i < Pages; i++)
        {
            pages[i] = new Panel { Dock = DockStyle.Fill, BackColor = M3.Surface, Visible = false };
            content.Controls.Add(pages[i]);
        }
        BuildWelcome(pages[0]);
        BuildOptions(pages[1]);
        BuildProgress(pages[2]);
        BuildFinish(pages[3]);
        Go(0);
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); M3.DarkTitleBar(this); }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (installing) { e.Cancel = true; return; }
        if (!installed && page < 3 && e.CloseReason == CloseReason.UserClosing &&
            MessageBox.Show(this, "Cancel " + AppInfo.Name + " setup?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            e.Cancel = true;
        base.OnFormClosing(e);
    }

    void Go(int i)
    {
        page = Math.Max(0, Math.Min(Pages - 1, i));
        for (int k = 0; k < Pages; k++) pages[k].Visible = k == page;
        backBtn.Visible = page == 1;
        cancelBtn.Visible = page < 2;
        nextBtn.Enabled = page != 2;
        nextBtn.Text = page == 0 ? "Next" : page == 1 ? "Install" : "Finish";
        nextBtn.Glyph = page == 1 ? "" : null;
        nextBtn.Invalidate();
    }

    void Next()
    {
        if (page == 0) Go(1);
        else if (page == 1) StartInstall();
        else if (page == 3)
        {
            if (installed && eqInstalledNow && restartSwitch.Checked) Restart();
            else if (installed && launchSwitch.Checked) LaunchApp();
            Close();
        }
    }

    static Label Title(Control p, string text) { return M3.Label(p, text, M3.Font(17f, true), M3.OnSurface, M3.R(32, 30, 390, 36)); }

    void BuildWelcome(Panel p)
    {
        Title(p, "Welcome to Bass Control");
        M3.Label(p, "This will install " + AppInfo.Name + " " + AppInfo.Version + " on your PC.\n\n" +
            "Bass Control gives your speakers a bass knob: choose Zero, Mid or High bass, or set your own level. " +
            "It sits in the system tray so you can change the bass any time.",
            M3.Font(10f), M3.OnSurfaceVariant, M3.R(32, 76, 390, 110), ContentAlignment.TopLeft);

        var card = new M3Card(M3.SurfaceContainerHigh) { Bounds = M3.R(32, 204, 390, 150), Radius = M3.D(24) };
        p.Controls.Add(card);
        M3.Label(card, "CREATED BY", M3.Font(8.5f, true), M3.OnSurfaceVariant, M3.R(22, 16, 200, 18));
        card.Controls.Add(new Initials("TR") { Bounds = M3.R(22, 42, 52, 52) });
        M3.Label(card, AppInfo.Author, M3.Font(14f, true), M3.OnSurface, M3.R(88, 42, 290, 28));
        M3.Label(card, "Developer & Designer", M3.Font(9.5f), M3.OnSurfaceVariant, M3.R(88, 70, 290, 20));
        var gh = new M3Button("GitHub", ButtonKind.Text, "") { Bounds = M3.R(14, 102, 110, 36) };
        gh.Click += (s, e) => AppInfo.OpenUrl(AppInfo.GitHub);
        var ig = new M3Button("Instagram", ButtonKind.Text, "") { Bounds = M3.R(126, 102, 130, 36) };
        ig.Click += (s, e) => AppInfo.OpenUrl(AppInfo.Instagram);
        card.Controls.Add(gh);
        card.Controls.Add(ig);
    }

    void BuildOptions(Panel p)
    {
        Title(p, "Choose your options");
        M3.Label(p, "Install location", M3.Font(9.5f, true), M3.OnSurface, M3.R(32, 76, 300, 20));
        var field = new M3Card(M3.SurfaceContainerHighest) { Bounds = M3.R(32, 100, 280, 44), Radius = M3.D(8) };
        p.Controls.Add(field);
        pathBox = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = field.BackColor, ForeColor = M3.OnSurface, Font = M3.Font(10f),
            Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppInfo.Name),
            Bounds = M3.R(14, 12, 254, 22)
        };
        field.Controls.Add(pathBox);
        var browse = new M3Button("Browse", ButtonKind.Outlined) { Bounds = M3.R(322, 102, 100, 40) };
        browse.Click += (s, e) =>
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choose where to install " + AppInfo.Name })
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    pathBox.Text = dlg.SelectedPath.TrimEnd('\\').EndsWith(AppInfo.Name, StringComparison.OrdinalIgnoreCase)
                        ? dlg.SelectedPath : Path.Combine(dlg.SelectedPath, AppInfo.Name);
        };
        p.Controls.Add(browse);

        var card = new M3Card(M3.SurfaceContainer) { Bounds = M3.R(32, 162, 390, 196), Radius = M3.D(24) };
        p.Controls.Add(card);
        desktopSwitch = Row(card, 0, "Desktop shortcut", "Put a Bass Control icon on the desktop");
        desktopSwitch.Checked = true;
        startupSwitch = Row(card, 1, "Start with Windows", "Keep Bass Control ready in the system tray");
        startupSwitch.Checked = true;
        if (EqApo.IsInstalled)
        {
            float y = 8 + 2 * 60;
            card.Controls.Add(new Panel { BackColor = M3.OutlineVariant, Bounds = new Rectangle(M3.D(20), M3.D(y), M3.D(350), 1) });
            M3.Label(card, "Equalizer APO", M3.Font(10.5f), M3.OnSurface, M3.R(20, y + 10, 280, 22));
            M3.Label(card, "Already installed. Bass Control will connect to it.", M3.Font(8.5f), M3.Success, M3.R(20, y + 32, 340, 18));
        }
        else
        {
            eqSwitch = Row(card, 2, "Install Equalizer APO first", "Required audio engine (included in this setup)");
            eqSwitch.Checked = true;
        }
    }

    static M3Switch Row(Control card, int row, string title, string sub)
    {
        float y = 8 + row * 60;
        if (row > 0) card.Controls.Add(new Panel { BackColor = M3.OutlineVariant, Bounds = new Rectangle(M3.D(20), M3.D(y), M3.D(350), 1) });
        M3.Label(card, title, M3.Font(10.5f), M3.OnSurface, M3.R(20, y + 10, 280, 22));
        M3.Label(card, sub, M3.Font(8.5f), M3.OnSurfaceVariant, M3.R(20, y + 32, 290, 18));
        var sw = new M3Switch { Location = new Point(M3.D(316), M3.D(y + 14)) };
        card.Controls.Add(sw);
        return sw;
    }

    void BuildProgress(Panel p)
    {
        Title(p, "Installing...");
        M3.Label(p, "Please wait while " + AppInfo.Name + " is installed.", M3.Font(10f), M3.OnSurfaceVariant, M3.R(32, 76, 390, 22));
        progress = new M3ProgressBar { Bounds = M3.R(32, 150, 390, 6) };
        p.Controls.Add(progress);
        progressText = M3.Label(p, "", M3.Font(9.5f), M3.OnSurfaceVariant, M3.R(32, 166, 390, 40), ContentAlignment.TopLeft);
    }

    void BuildFinish(Panel p)
    {
        var badge = new CheckBadge { Bounds = M3.R(32, 34, 56, 56) };
        p.Controls.Add(badge);
        M3.Label(p, "All set!", M3.Font(17f, true), M3.OnSurface, M3.R(32, 102, 390, 36));
        finishText = M3.Label(p, "", M3.Font(10f), M3.OnSurfaceVariant, M3.R(32, 144, 390, 110), ContentAlignment.TopLeft);
        var card = launchCard = new M3Card(M3.SurfaceContainer) { Bounds = M3.R(32, 268, 390, 76), Radius = M3.D(24) };
        p.Controls.Add(card);
        launchSwitch = Row(card, 0, "Launch Bass Control", "Open the app when you click Finish");
        launchSwitch.Checked = true;

        // Shown instead of the launch card when Equalizer APO was just installed.
        restartCard = new M3Card(M3.SurfaceContainer) { Bounds = card.Bounds, Radius = M3.D(24), Visible = false };
        p.Controls.Add(restartCard);
        restartSwitch = Row(restartCard, 0, "Restart now", "Needed once so Equalizer APO can start working");
        restartSwitch.Checked = true;
        M3.Label(p, "Thank you for using Bass Control  ·  by " + AppInfo.Author, M3.Font(8.5f), M3.OnSurfaceVariant, M3.R(32, 356, 390, 20));
    }

    void Report(float value, string text)
    {
        BeginInvoke((Action)(() => { progress.Value = value; progressText.Text = text; }));
    }

    void StartInstall()
    {
        var dir = pathBox.Text.Trim();
        if (dir.Length == 0 || !Path.IsPathRooted(dir))
        {
            MessageBox.Show(this, "Please choose a valid install folder.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        bool desktop = desktopSwitch.Checked, startup = startupSwitch.Checked, eq = eqSwitch != null && eqSwitch.Checked;
        installing = true;
        Go(2);
        new Thread(() =>
        {
            try { Install(dir, desktop, startup, eq); installed = true; }
            catch (Exception ex) { error = ex.Message; }
            BeginInvoke((Action)FinishInstall);
        }) { IsBackground = true }.Start();
    }

    void Install(string dir, bool desktop, bool startup, bool installEq)
    {
        bool needEq = installEq && !EqApo.IsInstalled;
        string step1 = needEq ? "Step 1 of 2: " : "", step2 = needEq ? "Step 2 of 2: " : "";

        // Equalizer APO goes first: Bass Control needs its config folder to connect to.
        if (needEq)
        {
            Report(0.05f, step1 + "Installing Equalizer APO (audio engine)...");
            var tmp = Path.Combine(Path.GetTempPath(), "BassControl-EqualizerAPO-setup.exe");
            Extract("EqualizerAPO.exe", tmp);
            // Silent install (/S) so its own wizard can't restart the PC halfway through our setup.
            using (var p = Process.Start(new ProcessStartInfo(tmp, "/S") { UseShellExecute = true })) p.WaitForExit();
            try { File.Delete(tmp); } catch { }
            if (!EqApo.IsInstalled) throw new Exception("Equalizer APO could not be installed.");
            eqInstalledNow = true;

            var configurator = EqApo.ConfiguratorPath();
            if (configurator != null)
            {
                Report(0.3f, step1 + "Choose your speakers.\nIn the Equalizer APO window, tick the speakers you use, click OK, then OK again.");
                using (var p = Process.Start(new ProcessStartInfo(configurator) { UseShellExecute = true })) p.WaitForExit();
            }
        }

        Report(0.45f, step2 + "Closing running copies of Bass Control...");
        foreach (var p in Process.GetProcessesByName("BassControl"))
            try { p.Kill(); p.WaitForExit(3000); } catch { }

        Report(0.55f, step2 + "Copying Bass Control...");
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, AppInfo.ExeName);
        Extract("BassControl.exe", exe);

        Report(0.7f, step2 + "Connecting to Equalizer APO...");
        if (EqApo.IsInstalled) EqApo.Hook();

        Report(0.8f, step2 + "Creating shortcuts...");
        Shell.CreateShortcut(Shell.StartMenuLink, exe, "", "Control your speaker bass");
        if (desktop) Shell.CreateShortcut(Shell.DesktopLink, exe, "", "Control your speaker bass");
        else if (File.Exists(Shell.DesktopLink)) File.Delete(Shell.DesktopLink);
        Shell.SetStartWithWindows(exe, startup);

        Report(0.9f, step2 + "Registering with Windows...");
        using (var k = Registry.LocalMachine.CreateSubKey(AppInfo.UninstallKey))
        {
            k.SetValue("DisplayName", AppInfo.Name);
            k.SetValue("DisplayVersion", AppInfo.Version);
            k.SetValue("Publisher", AppInfo.Author);
            k.SetValue("DisplayIcon", exe);
            k.SetValue("InstallLocation", dir);
            k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
            k.SetValue("URLInfoAbout", AppInfo.GitHub);
            k.SetValue("HelpLink", AppInfo.Instagram);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024) + 1, RegistryValueKind.DWord);
        }
        Report(1f, "Done.");
        Thread.Sleep(300);
    }

    void Extract(string resource, string target)
    {
        using (var src = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
        using (var dst = File.Create(target))
            src.CopyTo(dst);
    }

    void FinishInstall()
    {
        installing = false;
        if (installed)
        {
            string text = AppInfo.Name + " is installed. Look for the speaker icon in your system tray. " +
                "Click it to change the bass, or right-click for quick presets.";
            if (eqInstalledNow)
            {
                text += "\n\nEqualizer APO was installed too. Restart your PC once so it can start working.";
                launchCard.Visible = false;
                restartCard.Visible = true;
            }
            else if (!EqApo.IsInstalled) text += "\n\nEqualizer APO is not installed, so the bass won't change yet. You can install it later.";
            finishText.Text = text;
        }
        else
        {
            finishText.Text = "Setup could not finish:\n" + error;
            finishText.ForeColor = M3.Error;
            launchSwitch.Checked = false;
        }
        Go(3);
    }

    void Restart()
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5 /c \"Restarting to finish installing Bass Control\"")
                { CreateNoWindow = true, UseShellExecute = false });
        }
        catch { }
    }

    void LaunchApp()
    {
        // Start through Explorer so the app runs as the normal user, not as admin.
        var exe = Path.Combine(pathBox.Text.Trim(), AppInfo.ExeName);
        try { Process.Start("explorer.exe", "\"" + exe + "\""); } catch { }
    }
}

// Left side of the wizard: gradient, app icon, and the developer's name.
class SidePanel : Panel
{
    readonly Image logo = AppInfo.LoadImage("icon.png");

    public SidePanel() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; M3.Hq(g);
        using (var b = new LinearGradientBrush(ClientRectangle, M3.PrimaryContainer, M3.Hex("2A1B4D"), 60f)) g.FillRectangle(b, ClientRectangle);
        using (var b = new SolidBrush(Color.FromArgb(28, M3.Primary)))
        {
            g.FillEllipse(b, Width - M3.D(150), -M3.D(70), M3.D(240), M3.D(240));
            g.FillEllipse(b, -M3.D(90), Height - M3.D(170), M3.D(220), M3.D(220));
        }
        var tile = new RectangleF(M3.D(32), M3.D(40), M3.D(80), M3.D(80));
        M3.FillRound(g, Color.FromArgb(60, 0, 0, 0), tile, M3.D(24));
        if (logo != null) g.DrawImage(logo, tile.X + M3.D(16), tile.Y + M3.D(16), M3.D(48), M3.D(48));

        using (var f = M3.Font(18f, true)) M3.Text(g, AppInfo.Name, f, M3.OnPrimaryContainer, M3.R(32, 136, 190, 36), TextFormatFlags.Left);
        using (var f = M3.Font(9.5f)) M3.Text(g, "Version " + AppInfo.Version, f, M3.Hex("CBBEF0"), M3.R(32, 172, 190, 20), TextFormatFlags.Left);
        using (var f = M3.Font(8.5f, true)) M3.Text(g, "DEVELOPED BY", f, M3.Hex("CBBEF0"), M3.R(32, 382, 190, 18), TextFormatFlags.Left);
        using (var f = M3.Font(11.5f, true)) M3.Text(g, AppInfo.Author, f, M3.OnPrimaryContainer, M3.R(32, 402, 190, 26), TextFormatFlags.Left);
    }
}

class Initials : M3Control
{
    public Initials(string text) { Text = text; Font = M3.Font(14f, true); Cursor = Cursors.Default; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; M3.Hq(g);
        using (var b = new SolidBrush(M3.PrimaryContainer)) g.FillEllipse(b, 0, 0, Width - 1, Height - 1);
        M3.Text(g, Text, Font, M3.OnPrimaryContainer, ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

class CheckBadge : M3Control
{
    public CheckBadge() { Cursor = Cursors.Default; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; M3.Hq(g);
        using (var b = new SolidBrush(M3.PrimaryContainer)) g.FillEllipse(b, 0, 0, Width - 1, Height - 1);
        using (var f = M3.Icons(18f))
            M3.Text(g, "", f, M3.OnPrimaryContainer, ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
