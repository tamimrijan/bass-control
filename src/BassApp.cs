// Bass Control - system-wide bass control for speakers without a bass knob.
// Lives in the system tray and drives Equalizer APO with a low-shelf / high-pass filter.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

static class Program
{
    public static BassState State;
    public static TrayApp Tray;

    [STAThread]
    static int Main(string[] args)
    {
        string arg = args.Length > 0 ? args[0] : "";
        if (arg == "--setup") return EqApo.Hook() ? 0 : 1;
        if (arg == "--uninstall") return Uninstaller.Run();

        bool created;
        using (var mutex = new Mutex(true, "BassControl_SingleInstance", out created))
        {
            var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "BassControl_ShowWindow");
            if (!created)
            {
                // Already running: ask the running copy to show its window.
                showSignal.Set();
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            State = BassState.Load();
            Tray = new TrayApp(arg != "--tray", showSignal);
            Application.Run(Tray);
            GC.KeepAlive(mutex);
        }
        return 0;
    }
}

// The current bass settings, shared by the main window, tray flyout and tray menu.
class BassState
{
    public int Gain;            // tenths of a dB, -100..150
    public int Freq = 100;
    public bool Enabled = true, Protect = true, Zero;
    public bool TrayTipShown;
    public string Status = "";
    public bool StatusError;

    public static readonly int[] Frequencies = { 60, 80, 100, 120, 150, 200, 250 };
    public static readonly string[] PresetNames = { "Zero", "Mid", "High", "Flat" };

    public event Action Changed;
    readonly Timer writeTimer = new Timer { Interval = 120 };

    static string SettingsPath
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BassControl", "settings.txt"); }
    }

    public BassState() { writeTimer.Tick += (s, e) => { writeTimer.Stop(); Write(); }; }

    public double GainDb { get { return Gain / 10.0; } }

    public string Display
    {
        get
        {
            if (!Enabled) return "Off";
            if (Zero) return "Zero Bass";
            return (Gain >= 0 ? "+" : "") + GainDb.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
        }
    }

    public int PresetIndex
    {
        get
        {
            if (Zero) return 0;
            if (Gain == 60) return 1;
            if (Gain == 120) return 2;
            if (Gain == 0) return 3;
            return -1;
        }
    }

    public void ApplyPreset(int i)
    {
        Enabled = true;
        Zero = i == 0;
        if (i == 1) Gain = 60;
        if (i == 2) Gain = 120;
        if (i == 3) Gain = 0;
        Commit();
    }

    // Call after changing any field: refreshes every view and saves (debounced).
    public void Commit()
    {
        if (Changed != null) Changed();
        writeTimer.Stop(); writeTimer.Start();
    }

    public void SetStatus(string text, bool error)
    {
        Status = text; StatusError = error;
        if (Changed != null) Changed();
    }

    public void Write()
    {
        Save();
        var path = EqApo.FilterPath;
        if (path == null) { SetStatus("Equalizer APO is not installed", true); return; }

        var inv = CultureInfo.InvariantCulture;
        string text = "# Managed by Bass Control - changes here are overwritten\r\n";
        if (Enabled && Zero)
        {
            // Two stacked high-pass filters = a steep 24 dB/octave cut below 150 Hz.
            text += "Filter: ON HP Fc 150 Hz\r\nFilter: ON HP Fc 150 Hz\r\n";
        }
        else if (Enabled && Gain != 0)
        {
            double preamp = Protect && Gain > 0 ? -GainDb : 0;
            text += "Preamp: " + preamp.ToString("0.0", inv) + " dB\r\n";
            text += "Filter: ON LSC Fc " + Freq + " Hz Gain " + GainDb.ToString("0.0", inv) + " dB\r\n";
        }
        try
        {
            File.WriteAllText(path, text);
            SetStatus("Connected to Equalizer APO", false);
        }
        catch (Exception ex)
        {
            SetStatus("Could not save: " + ex.Message, true);
        }
    }

    public static BassState Load()
    {
        var s = new BassState();
        try
        {
            if (!File.Exists(SettingsPath)) return s;
            foreach (var line in File.ReadAllLines(SettingsPath))
            {
                var kv = line.Split('=');
                int v;
                if (kv.Length != 2 || !int.TryParse(kv[1], out v)) continue;
                switch (kv[0])
                {
                    case "gain": s.Gain = Math.Max(-100, Math.Min(150, v)); break;
                    case "freq": if (Array.IndexOf(Frequencies, v) >= 0) s.Freq = v; break;
                    case "on": s.Enabled = v != 0; break;
                    case "protect": s.Protect = v != 0; break;
                    case "zero": s.Zero = v != 0; break;
                    case "traytip": s.TrayTipShown = v != 0; break;
                }
            }
        }
        catch { }
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, "gain=" + Gain + "\r\nfreq=" + Freq + "\r\non=" + (Enabled ? 1 : 0) +
                "\r\nprotect=" + (Protect ? 1 : 0) + "\r\nzero=" + (Zero ? 1 : 0) + "\r\ntraytip=" + (TrayTipShown ? 1 : 0) + "\r\n");
        }
        catch { }
    }
}

// Owns the tray icon, the tray menu, the flyout and the main window.
class TrayApp : ApplicationContext
{
    readonly NotifyIcon icon;
    readonly BassState state = Program.State;
    MainForm main;
    TrayFlyout flyout;
    readonly ToolStripMenuItem[] presetItems = new ToolStripMenuItem[4];
    ToolStripMenuItem enabledItem;
    readonly SynchronizationContext ui;

    public TrayApp(bool showWindow, EventWaitHandle showSignal)
    {
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        icon = new NotifyIcon
        {
            Icon = AppInfo.LoadIcon(SystemInformation.SmallIconSize),
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ToggleFlyout(); };
        icon.BalloonTipClicked += (s, e) => ShowMain(0);
        state.Changed += Refresh;

        // Wait for a second launch asking us to show the window.
        var t = new Thread(() =>
        {
            while (true) { showSignal.WaitOne(); ui.Post(_ => ShowMain(0), null); }
        }) { IsBackground = true };
        t.Start();

        EnsureConnected(showWindow);
        Refresh();
        if (showWindow) ShowMain(0);
    }

    // Make sure Equalizer APO is installed and our filter file is hooked in.
    void EnsureConnected(bool interactive)
    {
        if (!EqApo.IsInstalled)
        {
            state.SetStatus("Equalizer APO is not installed", true);
            if (!interactive) return;
            var r = MessageBox.Show("Bass Control needs the free audio engine \"Equalizer APO\" to change your sound.\n\n" +
                "Install it, tick your speakers in its device list, restart your PC, then open Bass Control again.\n\nOpen the download page?",
                AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r == DialogResult.Yes) AppInfo.OpenUrl("https://sourceforge.net/projects/equalizerapo/");
            return;
        }
        if (!EqApo.IsHooked())
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--setup") { Verb = "runas", UseShellExecute = true });
                p.WaitForExit();
            }
            catch { }
        }
        if (EqApo.IsHooked()) state.Write();
        else state.SetStatus("Not connected to Equalizer APO. Reopen the app to retry.", true);
    }

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new M3MenuRenderer(), Font = M3.Font(10f), ShowImageMargin = true, ShowCheckMargin = false };
        var open = new ToolStripMenuItem("Open " + AppInfo.Name, null, (s, e) => ShowMain(0)) { Font = M3.Font(10f, true) };
        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());
        string[] labels = { "Zero Bass", "Mid Bass", "High Bass", "Flat (no change)" };
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            presetItems[i] = new ToolStripMenuItem(labels[i], null, (s, e) => state.ApplyPreset(idx));
            menu.Items.Add(presetItems[i]);
        }
        menu.Items.Add(new ToolStripSeparator());
        enabledItem = new ToolStripMenuItem("Bass effect on", null, (s, e) => { state.Enabled = !state.Enabled; state.Commit(); });
        menu.Items.Add(enabledItem);
        menu.Items.Add(new ToolStripMenuItem("About", null, (s, e) => ShowMain(1)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => Quit()));
        return menu;
    }

    void Refresh()
    {
        string tip = AppInfo.Name + ": " + state.Display;
        icon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        int p = state.Enabled ? state.PresetIndex : -1;
        for (int i = 0; i < 4; i++) presetItems[i].Checked = i == p;
        enabledItem.Checked = state.Enabled;
    }

    DateTime flyoutHiddenAt;

    void ToggleFlyout()
    {
        if (flyout != null && flyout.Visible) { flyout.Hide(); return; }
        // A click on the icon deactivates the flyout first; don't instantly reopen it.
        if ((DateTime.Now - flyoutHiddenAt).TotalMilliseconds < 300) return;
        if (flyout == null)
        {
            flyout = new TrayFlyout(() => ShowMain(0));
            flyout.VisibleChanged += (s, e) => { if (!flyout.Visible) flyoutHiddenAt = DateTime.Now; };
        }
        flyout.ShowNearTray();
    }

    public void ShowMain(int page)
    {
        if (flyout != null) flyout.Hide();
        if (main == null || main.IsDisposed) main = new MainForm(this);
        main.ShowPage(page);
        if (!main.Visible) main.Show();
        if (main.WindowState == FormWindowState.Minimized) main.WindowState = FormWindowState.Normal;
        main.Activate();
    }

    public void MainHidden()
    {
        if (state.TrayTipShown) return;
        state.TrayTipShown = true;
        state.Save();
        icon.ShowBalloonTip(4000, AppInfo.Name + " is still running",
            "Click the speaker icon in the system tray to change the bass any time.", ToolTipIcon.Info);
    }

    public void Quit()
    {
        state.Save();
        icon.Visible = false;
        icon.Dispose();
        ExitThread();
    }
}

class MainForm : Form
{
    readonly TrayApp app;
    readonly BassState state = Program.State;
    Panel bassPage, aboutPage;
    M3NavBar nav;
    Label valueLabel, statusLabel, statusDot;
    M3Slider slider;
    M3Segmented presets, freqs;
    M3Switch enabledSwitch, protectSwitch, startupSwitch;
    bool syncing;

    public MainForm(TrayApp app)
    {
        this.app = app;
        Text = AppInfo.Name;
        Icon = AppInfo.LoadIcon(new Size(32, 32));
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = M3.Surface;
        ForeColor = M3.OnSurface;
        ClientSize = new Size(M3.D(420), M3.D(664));
        DoubleBuffered = true;

        nav = new M3NavBar(new[] { "", "" }, new[] { "Bass", "About" }) { Dock = DockStyle.Bottom };
        nav.SelectionChanged += (s, e) => { bassPage.Visible = nav.SelectedIndex == 0; aboutPage.Visible = nav.SelectedIndex == 1; };

        bassPage = new Panel { Dock = DockStyle.Fill, BackColor = M3.Surface };
        aboutPage = new Panel { Dock = DockStyle.Fill, BackColor = M3.Surface, Visible = false };
        BuildBassPage();
        BuildAboutPage();
        Controls.Add(bassPage);
        Controls.Add(aboutPage);
        Controls.Add(nav);

        state.Changed += Sync;
        FormClosed += (s, e) => state.Changed -= Sync;
        Sync();
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); M3.DarkTitleBar(this); }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            app.MainHidden();
        }
        base.OnFormClosing(e);
    }

    public void ShowPage(int i) { nav.SelectedIndex = i; }

    void BuildBassPage()
    {
        var p = bassPage;
        var logo = new PictureBox { Image = AppInfo.LoadImage("icon.png"), SizeMode = PictureBoxSizeMode.Zoom, Bounds = M3.R(20, 18, 40, 40), BackColor = p.BackColor };
        p.Controls.Add(logo);
        M3.Label(p, AppInfo.Name, M3.Font(16f, true), M3.OnSurface, M3.R(70, 14, 300, 28));
        statusDot = M3.Label(p, "●", M3.Font(8f), M3.Success, M3.R(70, 42, 14, 18));
        statusLabel = M3.Label(p, "", M3.Font(9f), M3.OnSurfaceVariant, M3.R(84, 42, 316, 18));

        var hero = new M3Card(M3.SurfaceContainerHigh) { Bounds = M3.R(16, 74, 388, 168), Radius = M3.D(28) };
        p.Controls.Add(hero);
        M3.Label(hero, "BASS LEVEL", M3.Font(8.5f, true), M3.OnSurfaceVariant, M3.R(24, 18, 200, 18));
        valueLabel = M3.Label(hero, "", M3.Font(28f, true), M3.Primary, M3.R(20, 36, 348, 52));
        slider = new M3Slider { Bounds = M3.R(16, 94, 356, 44) };
        slider.ValueChanged += (s, e) =>
        {
            if (syncing) return;
            state.Gain = slider.Value; state.Zero = false; state.Enabled = true; state.Commit();
        };
        hero.Controls.Add(slider);
        M3.Label(hero, "-10 dB", M3.Font(8f), M3.OnSurfaceVariant, M3.R(22, 138, 80, 18));
        M3.Label(hero, "0", M3.Font(8f), M3.OnSurfaceVariant, M3.R(22 + 344 * 0.4f - 30, 138, 60, 18), ContentAlignment.MiddleCenter);
        M3.Label(hero, "+15 dB", M3.Font(8f), M3.OnSurfaceVariant, M3.R(286, 138, 80, 18), ContentAlignment.MiddleRight);

        M3.Label(p, "Presets", M3.Font(10f, true), M3.OnSurface, M3.R(20, 254, 200, 22));
        presets = new M3Segmented(BassState.PresetNames) { Bounds = M3.R(16, 280, 388, 40) };
        presets.SelectionChanged += (s, e) => state.ApplyPreset(presets.SelectedIndex);
        p.Controls.Add(presets);

        M3.Label(p, "Bass frequency (Hz)", M3.Font(10f, true), M3.OnSurface, M3.R(20, 332, 250, 22));
        var fl = Array.ConvertAll(BassState.Frequencies, f => f.ToString());
        freqs = new M3Segmented(fl) { Bounds = M3.R(16, 358, 388, 40), Font = M3.Font(9f, true) };
        freqs.SelectionChanged += (s, e) => { state.Freq = BassState.Frequencies[freqs.SelectedIndex]; state.Commit(); };
        p.Controls.Add(freqs);

        var settings = new M3Card(M3.SurfaceContainer) { Bounds = M3.R(16, 412, 388, 168), Radius = M3.D(24) };
        p.Controls.Add(settings);
        enabledSwitch = AddRow(settings, 0, "Bass effect", "Turn all bass changes on or off");
        enabledSwitch.CheckedChanged += (s, e) => { if (!syncing) { state.Enabled = enabledSwitch.Checked; state.Commit(); } };
        protectSwitch = AddRow(settings, 1, "Prevent distortion", "Lowers volume to match the boost");
        protectSwitch.CheckedChanged += (s, e) => { if (!syncing) { state.Protect = protectSwitch.Checked; state.Commit(); } };
        startupSwitch = AddRow(settings, 2, "Start with Windows", "Runs quietly in the system tray");
        startupSwitch.Checked = Shell.StartsWithWindows();
        startupSwitch.CheckedChanged += (s, e) => Shell.SetStartWithWindows(Application.ExecutablePath, startupSwitch.Checked);
    }

    M3Switch AddRow(Control card, int row, string title, string sub)
    {
        float y = 8 + row * 52;
        M3.Label(card, title, M3.Font(10.5f), M3.OnSurface, M3.R(20, y + 6, 280, 22));
        M3.Label(card, sub, M3.Font(8.5f), M3.OnSurfaceVariant, M3.R(20, y + 27, 280, 18));
        if (row > 0)
        {
            var div = new Panel { BackColor = M3.OutlineVariant, Bounds = new Rectangle(M3.D(20), M3.D(y), M3.D(348), 1) };
            card.Controls.Add(div);
        }
        var sw = new M3Switch { Location = new Point(M3.D(312), M3.D(y + 10)) };
        card.Controls.Add(sw);
        return sw;
    }

    void BuildAboutPage()
    {
        var p = aboutPage;
        var logoCard = new M3Card(M3.PrimaryContainer) { Bounds = M3.R(170, 28, 80, 80), Radius = M3.D(24) };
        p.Controls.Add(logoCard);
        logoCard.Controls.Add(new PictureBox { Image = AppInfo.LoadImage("icon.png"), SizeMode = PictureBoxSizeMode.Zoom,
            Bounds = M3.R(16, 16, 48, 48), BackColor = logoCard.BackColor });
        M3.Label(p, AppInfo.Name, M3.Font(18f, true), M3.OnSurface, M3.R(16, 118, 388, 34), ContentAlignment.MiddleCenter);
        M3.Label(p, "Version " + AppInfo.Version, M3.Font(9.5f), M3.OnSurfaceVariant, M3.R(16, 152, 388, 20), ContentAlignment.MiddleCenter);

        var dev = new M3Card(M3.SurfaceContainerHigh) { Bounds = M3.R(16, 190, 388, 176), Radius = M3.D(28) };
        p.Controls.Add(dev);
        M3.Label(dev, "DEVELOPER", M3.Font(8.5f, true), M3.OnSurfaceVariant, M3.R(24, 18, 200, 18));
        var avatar = new Avatar("TR") { Bounds = M3.R(24, 44, 56, 56) };
        dev.Controls.Add(avatar);
        M3.Label(dev, AppInfo.Author, M3.Font(14f, true), M3.OnSurface, M3.R(92, 46, 280, 28));
        M3.Label(dev, "Developer & Designer", M3.Font(9.5f), M3.OnSurfaceVariant, M3.R(92, 74, 280, 20));
        var gh = new M3Button("GitHub", ButtonKind.Tonal, "") { Bounds = M3.R(24, 116, 162, 40) };
        gh.Click += (s, e) => AppInfo.OpenUrl(AppInfo.GitHub);
        var ig = new M3Button("Instagram", ButtonKind.Tonal, "") { Bounds = M3.R(202, 116, 162, 40) };
        ig.Click += (s, e) => AppInfo.OpenUrl(AppInfo.Instagram);
        dev.Controls.Add(gh);
        dev.Controls.Add(ig);

        var info = new M3Card(M3.SurfaceContainer) { Bounds = M3.R(16, 380, 388, 150), Radius = M3.D(24) };
        p.Controls.Add(info);
        M3.Label(info, "ABOUT THIS APP", M3.Font(8.5f, true), M3.OnSurfaceVariant, M3.R(24, 18, 300, 18));
        M3.Label(info, "Bass Control adds a bass knob to speakers that don't have one. " +
            "Pick Zero, Mid or High bass, or set your own level, and it applies to all sound on your PC. " +
            "Click the tray icon to change it any time.",
            M3.Font(9.5f), M3.OnSurface, M3.R(24, 40, 340, 76), ContentAlignment.TopLeft);
        M3.Label(info, "Audio engine: Equalizer APO (GPL)", M3.Font(8.5f), M3.OnSurfaceVariant, M3.R(24, 118, 340, 18));

        M3.Label(p, "Made with ♥ by " + AppInfo.Author + "  ·  © 2026", M3.Font(9f), M3.OnSurfaceVariant,
            M3.R(16, 546, 388, 20), ContentAlignment.MiddleCenter);
    }

    void Sync()
    {
        syncing = true;
        valueLabel.Text = state.Display;
        valueLabel.ForeColor = state.Enabled ? M3.Primary : M3.OnSurfaceVariant;
        slider.Value = state.Zero ? slider.Minimum : state.Gain;
        slider.Enabled = state.Enabled;
        presets.SelectedIndex = state.Enabled ? state.PresetIndex : -1;
        freqs.SelectedIndex = Array.IndexOf(BassState.Frequencies, state.Freq);
        enabledSwitch.Checked = state.Enabled;
        protectSwitch.Checked = state.Protect;
        statusLabel.Text = state.Status;
        statusDot.ForeColor = state.StatusError ? M3.Error : M3.Success;
        statusLabel.ForeColor = state.StatusError ? M3.Error : M3.OnSurfaceVariant;
        syncing = false;
    }
}

// Circle with initials, in the primary container color.
class Avatar : M3Control
{
    public Avatar(string initials) { Text = initials; Font = M3.Font(15f, true); Cursor = Cursors.Default; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; M3.Hq(g);
        using (var b = new SolidBrush(M3.PrimaryContainer)) g.FillEllipse(b, 0, 0, Width - 1, Height - 1);
        M3.Text(g, Text, Font, M3.OnPrimaryContainer, ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

// Small popup above the tray icon for quick changes.
class TrayFlyout : Form
{
    readonly BassState state = Program.State;
    readonly Label valueLabel;
    readonly M3Slider slider;
    readonly M3Segmented presets;
    readonly M3Switch enabledSwitch;
    bool syncing;

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);

    public TrayFlyout(Action openMain)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = M3.SurfaceContainerHigh;
        ClientSize = new Size(M3.D(340), M3.D(228));
        Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width + 1, Height + 1, M3.D(28), M3.D(28)));

        var logo = new PictureBox { Image = AppInfo.LoadImage("icon.png"), SizeMode = PictureBoxSizeMode.Zoom, Bounds = M3.R(20, 18, 24, 24), BackColor = BackColor };
        Controls.Add(logo);
        M3.Label(this, "Bass", M3.Font(12f, true), M3.OnSurface, M3.R(52, 16, 120, 28));
        valueLabel = M3.Label(this, "", M3.Font(12f, true), M3.Primary, M3.R(150, 16, 170, 28), ContentAlignment.MiddleRight);

        slider = new M3Slider { Bounds = M3.R(14, 54, 312, 44) };
        slider.ValueChanged += (s, e) => { if (syncing) return; state.Gain = slider.Value; state.Zero = false; state.Enabled = true; state.Commit(); };
        Controls.Add(slider);

        presets = new M3Segmented(BassState.PresetNames) { Bounds = M3.R(20, 108, 300, 40) };
        presets.SelectionChanged += (s, e) => state.ApplyPreset(presets.SelectedIndex);
        Controls.Add(presets);

        var div = new Panel { BackColor = M3.OutlineVariant, Bounds = new Rectangle(M3.D(20), M3.D(164), M3.D(300), 1) };
        Controls.Add(div);
        enabledSwitch = new M3Switch { Location = new Point(M3.D(20), M3.D(178)) };
        enabledSwitch.CheckedChanged += (s, e) => { if (!syncing) { state.Enabled = enabledSwitch.Checked; state.Commit(); } };
        Controls.Add(enabledSwitch);
        M3.Label(this, "Bass effect", M3.Font(10f), M3.OnSurface, M3.R(80, 178, 120, 32));
        var open = new M3Button("Open app", ButtonKind.Text, "") { Bounds = M3.R(204, 174, 120, 40) };
        open.Click += (s, e) => openMain();
        Controls.Add(open);

        state.Changed += Sync;
        Sync();
        Deactivate += (s, e) => Hide();
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; /* CS_DROPSHADOW */ return cp; }
    }

    public void ShowNearTray()
    {
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        int x = Math.Min(Cursor.Position.X - Width / 2, wa.Right - Width - M3.D(12));
        x = Math.Max(wa.Left + M3.D(12), x);
        int y = Cursor.Position.Y > wa.Bottom - M3.D(40) || Cursor.Position.Y > wa.Top + wa.Height / 2
            ? wa.Bottom - Height - M3.D(12) : wa.Top + M3.D(12);
        Location = new Point(x, y);
        Show();
        Activate();
    }

    void Sync()
    {
        syncing = true;
        valueLabel.Text = state.Display;
        valueLabel.ForeColor = state.Enabled ? M3.Primary : M3.OnSurfaceVariant;
        slider.Value = state.Zero ? slider.Minimum : state.Gain;
        slider.Enabled = state.Enabled;
        presets.SelectedIndex = state.Enabled ? state.PresetIndex : -1;
        enabledSwitch.Checked = state.Enabled;
        syncing = false;
    }
}

// Dark Material-style tray menu.
class M3MenuRenderer : ToolStripProfessionalRenderer
{
    public M3MenuRenderer() : base(new M3Colors()) { RoundedEdges = true; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? M3.OnSurface : M3.Outline;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics; M3.Hq(g);
        var r = e.ImageRectangle;
        using (var f = M3.Icons(9f))
            M3.Text(g, "", f, M3.Primary, new Rectangle(r.X - 2, e.Item.ContentRectangle.Y, r.Width + 4, e.Item.ContentRectangle.Height),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using (var p = new Pen(M3.OutlineVariant)) e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
    }

    class M3Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return M3.SurfaceContainer; } }
        public override Color ImageMarginGradientBegin { get { return M3.SurfaceContainer; } }
        public override Color ImageMarginGradientMiddle { get { return M3.SurfaceContainer; } }
        public override Color ImageMarginGradientEnd { get { return M3.SurfaceContainer; } }
        public override Color MenuBorder { get { return M3.OutlineVariant; } }
        public override Color MenuItemBorder { get { return M3.SurfaceContainerHighest; } }
        public override Color MenuItemSelected { get { return M3.SurfaceContainerHighest; } }
        public override Color MenuItemSelectedGradientBegin { get { return M3.SurfaceContainerHighest; } }
        public override Color MenuItemSelectedGradientEnd { get { return M3.SurfaceContainerHighest; } }
        public override Color CheckBackground { get { return M3.SurfaceContainer; } }
        public override Color CheckSelectedBackground { get { return M3.SurfaceContainerHighest; } }
        public override Color CheckPressedBackground { get { return M3.SurfaceContainerHighest; } }
    }
}

// Removes Bass Control from the PC. Started from Settings > Apps.
static class Uninstaller
{
    public static int Run()
    {
        if (!Shell.IsAdmin())
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--uninstall") { Verb = "runas", UseShellExecute = true });
            }
            catch { }
            return 0;
        }

        Application.EnableVisualStyles();
        if (MessageBox.Show("Remove " + AppInfo.Name + " from this PC?\n\nYour speakers will go back to normal sound. Equalizer APO stays installed.",
            AppInfo.Name + " Uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;

        int me = Process.GetCurrentProcess().Id;
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppInfo.ExeName)))
            if (p.Id != me) try { p.Kill(); p.WaitForExit(3000); } catch { }

        EqApo.Unhook();
        Shell.SetStartWithWindows(null, false);
        foreach (var lnk in new[] { Shell.StartMenuLink, Shell.DesktopLink })
            try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
        try { Registry.LocalMachine.DeleteSubKeyTree(AppInfo.UninstallKey, false); } catch { }

        MessageBox.Show(AppInfo.Name + " was removed.", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);

        // Delete the install folder after this process exits.
        var dir = Path.GetDirectoryName(Application.ExecutablePath);
        Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + dir + "\"")
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        return 0;
    }
}
