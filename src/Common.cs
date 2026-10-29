// Shared code for Bass Control and its installer: app info, Material 3 theme,
// custom-drawn Material 3 controls, and Equalizer APO helpers.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

static class AppInfo
{
    public const string Name = "Bass Control";
    public const string Version = "2.0.0";
    public const string Author = "Tamim Ahmed Rijan";
    public const string GitHub = "https://github.com/tamimrijan";
    public const string Instagram = "https://www.instagram.com/tamim_ahmed_rijan/";
    public const string ExeName = "BassControl.exe";
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BassControl";
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValue = "BassControl";

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    public static Image LoadImage(string resource)
    {
        var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        return s == null ? null : Image.FromStream(s);
    }

    public static Icon LoadIcon(Size size)
    {
        var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico");
        return s == null ? SystemIcons.Application : new Icon(s, size);
    }
}

// Material 3 dark color scheme (baseline violet) and drawing helpers.
static class M3
{
    public static readonly Color Surface = Hex("141218");
    public static readonly Color SurfaceContainerLow = Hex("1D1B20");
    public static readonly Color SurfaceContainer = Hex("211F26");
    public static readonly Color SurfaceContainerHigh = Hex("2B2930");
    public static readonly Color SurfaceContainerHighest = Hex("36343B");
    public static readonly Color OnSurface = Hex("E6E0E9");
    public static readonly Color OnSurfaceVariant = Hex("CAC4D0");
    public static readonly Color Outline = Hex("938F99");
    public static readonly Color OutlineVariant = Hex("49454F");
    public static readonly Color Primary = Hex("D0BCFF");
    public static readonly Color OnPrimary = Hex("381E72");
    public static readonly Color PrimaryContainer = Hex("4F378B");
    public static readonly Color OnPrimaryContainer = Hex("EADDFF");
    public static readonly Color SecondaryContainer = Hex("4A4458");
    public static readonly Color OnSecondaryContainer = Hex("E8DEF8");
    public static readonly Color Tertiary = Hex("EFB8C8");
    public static readonly Color Success = Hex("9CD67D");
    public static readonly Color Error = Hex("F2B8B5");

    public const string IconFont = "Segoe MDL2 Assets";

    static float scale = 0;
    public static float Scale
    {
        get
        {
            if (scale == 0)
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            return scale;
        }
    }
    public static int D(float v) { return (int)Math.Round(v * Scale); }

    public static Color Hex(string h)
    {
        return Color.FromArgb(Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16));
    }

    public static Color Blend(Color baseColor, Color overlay, float amount)
    {
        return Color.FromArgb(
            (int)(baseColor.R + (overlay.R - baseColor.R) * amount),
            (int)(baseColor.G + (overlay.G - baseColor.G) * amount),
            (int)(baseColor.B + (overlay.B - baseColor.B) * amount));
    }

    public static Font Font(float size, bool bold = false)
    {
        return new Font("Segoe UI" + (bold ? " Semibold" : ""), size, FontStyle.Regular, GraphicsUnit.Point);
    }

    public static Font Icons(float size) { return new Font(IconFont, size, FontStyle.Regular, GraphicsUnit.Point); }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
    {
        using (var b = new SolidBrush(c)) using (var p = Round(r, radius)) g.FillPath(b, p);
    }

    public static void StrokeRound(Graphics g, Color c, RectangleF r, float radius, float width = 1)
    {
        using (var pen = new Pen(c, width)) using (var p = Round(r, radius)) g.DrawPath(pen, p);
    }

    public static void Hq(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    }

    public static void Text(Graphics g, string text, Font f, Color c, Rectangle r, TextFormatFlags flags)
    {
        TextRenderer.DrawText(g, text, f, r, c, flags | TextFormatFlags.NoPadding);
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void DarkTitleBar(Form f)
    {
        int on = 1;
        try
        {
            if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0)
                DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
        }
        catch { }
    }

    public static Label Label(Control parent, string text, Font font, Color color, Rectangle bounds,
        ContentAlignment align = ContentAlignment.MiddleLeft)
    {
        var l = new Label { Text = text, Font = font, ForeColor = color, BackColor = parent.BackColor,
            Bounds = bounds, TextAlign = align, AutoSize = false, UseMnemonic = false };
        parent.Controls.Add(l);
        return l;
    }

    public static Rectangle R(float x, float y, float w, float h) { return new Rectangle(D(x), D(y), D(w), D(h)); }
}

// Base for custom-drawn controls: double buffered, paints its parent's color behind itself.
class M3Control : Control
{
    protected bool Hover, Pressed;

    public M3Control()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Cursor = Cursors.Hand;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent != null ? Parent.BackColor : M3.Surface);
    }

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { Pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }

    // Material state layer: 8% on hover, 12% when pressed.
    protected Color State(Color container, Color content)
    {
        if (!Enabled) return container;
        return M3.Blend(container, content, Pressed ? 0.12f : Hover ? 0.08f : 0f);
    }
}

// Rounded surface card.
class M3Card : Panel
{
    public int Radius = M3.D(24);

    public M3Card(Color fill)
    {
        BackColor = fill;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent != null ? Parent.BackColor : M3.Surface);
        M3.Hq(e.Graphics);
        M3.FillRound(e.Graphics, BackColor, new RectangleF(0, 0, Width - 1, Height - 1), Radius);
    }
}

enum ButtonKind { Filled, Tonal, Outlined, Text }

class M3Button : M3Control
{
    public ButtonKind Kind = ButtonKind.Filled;
    public string Glyph;

    public M3Button(string text, ButtonKind kind, string glyph = null)
    {
        Text = text; Kind = kind; Glyph = glyph;
        Font = M3.Font(10f, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; M3.Hq(g);
        Color bg, fg;
        switch (Kind)
        {
            case ButtonKind.Filled: bg = M3.Primary; fg = M3.OnPrimary; break;
            case ButtonKind.Tonal: bg = M3.SecondaryContainer; fg = M3.OnSecondaryContainer; break;
            default: bg = Parent.BackColor; fg = M3.Primary; break;
        }
        if (!Enabled) { bg = Kind == ButtonKind.Filled || Kind == ButtonKind.Tonal ? M3.Blend(Parent.BackColor, M3.OnSurface, 0.12f) : Parent.BackColor; fg = M3.Blend(Parent.BackColor, M3.OnSurface, 0.38f); }
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        M3.FillRound(g, State(bg, fg), r, Height / 2f);
        if (Kind == ButtonKind.Outlined) M3.StrokeRound(g, M3.Outline, r, Height / 2f);

        var textSize = TextRenderer.MeasureText(Text, Font);
        int glyphW = 0;
        Font gf = null;
        if (Glyph != null) { gf = M3.Icons(10.5f); glyphW = TextRenderer.MeasureText(Glyph, gf).Width + M3.D(2); }
        int x = (Width - textSize.Width - glyphW) / 2;
        if (gf != null)
        {
            M3.Text(g, Glyph, gf, fg, new Rectangle(x, 0, glyphW, Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            gf.Dispose();
        }
        M3.Text(g, Text, Font, fg, new Rectangle(x + glyphW, 0, textSize.Width + 2, Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
}

class M3Switch : M3Control
{
    bool on;
    public event EventHandler CheckedChanged;

    public bool Checked
    {
        get { return on; }
        set { if (on == value) return; on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
    }

    public M3Switch() { Size = new Size(M3.D(52), M3.D(32)); }

    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; M3.Hq(g);
        float w = M3.D(52), h = M3.D(32);
        var track = new RectangleF((Width - w) / 2f + 0.5f, (Height - h) / 2f + 0.5f, w - 1, h - 1);
        if (on)
        {
            M3.FillRound(g, M3.Primary, track, h / 2);
            float t = M3.D(24);
            var thumb = new RectangleF(track.Right - M3.D(4) - t, track.Y + (track.Height - t) / 2, t, t);
            if (Hover) using (var b = new SolidBrush(Color.FromArgb(28, M3.Primary))) g.FillEllipse(b, RectangleF.Inflate(thumb, M3.D(8), M3.D(8)));
            using (var b = new SolidBrush(M3.OnPrimary)) g.FillEllipse(b, thumb);
            using (var f = M3.Icons(8.5f))
                M3.Text(g, "\uE73E", f, M3.OnPrimaryContainer, Rectangle.Round(thumb), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        else
        {
            M3.FillRound(g, M3.SurfaceContainerHighest, track, h / 2);
            M3.StrokeRound(g, M3.Outline, track, h / 2, M3.Scale * 1.5f);
            float t = Pressed ? M3.D(28) : M3.D(16);
            var thumb = new RectangleF(track.X + M3.D(16) - t / 2, track.Y + (track.Height - t) / 2, t, t);
            using (var b = new SolidBrush(Hover ? M3.OnSurfaceVariant : M3.Outline)) g.FillEllipse(b, thumb);
        }
    }
}

// Material 3 slider: thick rounded track with a vertical handle and a centre mark at zero.
class M3Slider : M3Control
{
    public int Minimum = -100, Maximum = 150;
    public int Mark = 0;
    int val;
    bool dragging;
    public event EventHandler ValueChanged;

    public int Value
    {
        get { return val; }
        set
        {
            value = Math.Max(Minimum, Math.Min(Maximum, value));
            if (val == value) return;
            val = value; Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }

    public M3Slider() { Height = M3.D(44); TabStop = true; }

    float Pad { get { return M3.D(6); } }
    float XFor(int v) { return Pad + (Width - 2 * Pad) * (v - Minimum) / (float)(Maximum - Minimum); }
    int ValueAt(float x)
    {
        float t = (x - Pad) / (Width - 2 * Pad);
        int v = (int)Math.Round(Minimum + t * (Maximum - Minimum));
        return (int)(Math.Round(v / 5.0) * 5);  // 0.5 dB steps
    }

    protected override void OnMouseDown(MouseEventArgs e) { dragging = true; Focus(); Value = ValueAt(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (dragging) Value = ValueAt(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { dragging = false; base.OnMouseUp(e); }
    protected override void OnMouseWheel(MouseEventArgs e) { Value += e.Delta > 0 ? 5 : -5; base.OnMouseWheel(e); }
    protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value -= 5;
        if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value += 5;
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; M3.Hq(g);
        float th = M3.D(16), cy = Height / 2f, x = XFor(val), gap = M3.D(6);
        float handleW = (dragging || Pressed) ? M3.D(2) : M3.D(4);
        var active = new RectangleF(Pad, cy - th / 2, Math.Max(0, x - gap - Pad), th);
        var inactive = new RectangleF(x + gap, cy - th / 2, Math.Max(0, Width - Pad - x - gap), th);
        if (active.Width > 0.5f) M3.FillRound(g, Enabled ? M3.Primary : M3.OutlineVariant, active, Math.Min(th / 2, active.Width / 2));
        if (inactive.Width > 0.5f) M3.FillRound(g, M3.SecondaryContainer, inactive, Math.Min(th / 2, inactive.Width / 2));

        float mx = XFor(Mark);
        if (Math.Abs(mx - x) > gap + M3.D(4))
            using (var b = new SolidBrush(mx < x ? M3.OnPrimary : M3.Primary))
                g.FillEllipse(b, mx - M3.D(2), cy - M3.D(2), M3.D(4), M3.D(4));

        M3.FillRound(g, Enabled ? M3.Primary : M3.Outline, new RectangleF(x - handleW / 2, cy - M3.D(22), handleW, M3.D(44)), handleW / 2);
        if (Focused) M3.StrokeRound(g, Color.FromArgb(90, M3.Primary), new RectangleF(x - M3.D(5), cy - M3.D(22), M3.D(10), M3.D(44)), M3.D(5));
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}

// Material 3 segmented button (single select, -1 = nothing selected).
class M3Segmented : M3Control
{
    public string[] Options;
    int selected = -1, hoverIndex = -1;
    public event EventHandler SelectionChanged;

    public M3Segmented(params string[] options) { Options = options; Height = M3.D(40); Font = M3.Font(9.5f, true); }

    public int SelectedIndex
    {
        get { return selected; }
        set { if (selected == value) return; selected = value; Invalidate(); }
    }

    float SegW { get { return (Width - 1f) / Options.Length; } }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = Math.Min(Options.Length - 1, (int)(e.X / SegW));
        if (i != hoverIndex) { hoverIndex = i; Invalidate(); }
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(EventArgs e) { hoverIndex = -1; base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int i = Math.Min(Options.Length - 1, (int)(e.X / SegW));
        selected = i; Invalidate();
        if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; M3.Hq(g);
        var outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float rad = outer.Height / 2, w = SegW;
        using (var clip = M3.Round(outer, rad))
        {
            var oldClip = g.Clip;
            g.SetClip(clip, CombineMode.Intersect);
            for (int i = 0; i < Options.Length; i++)
            {
                var seg = new RectangleF(i * w, 0, w + 1, Height);
                Color fill = i == selected ? M3.SecondaryContainer : Parent.BackColor;
                Color fg = i == selected ? M3.OnSecondaryContainer : M3.OnSurface;
                if (i == hoverIndex) fill = M3.Blend(fill, fg, Pressed ? 0.12f : 0.08f);
                using (var b = new SolidBrush(fill)) g.FillRectangle(b, seg);
            }
            g.Clip = oldClip;
        }
        using (var pen = new Pen(M3.Outline))
        {
            using (var p = M3.Round(outer, rad)) g.DrawPath(pen, p);
            for (int i = 1; i < Options.Length; i++) g.DrawLine(pen, i * w, 0.5f, i * w, Height - 1);
        }
        using (var gf = M3.Icons(9f))
            for (int i = 0; i < Options.Length; i++)
            {
                Color fg = i == selected ? M3.OnSecondaryContainer : M3.OnSurface;
                var ts = TextRenderer.MeasureText(Options[i], Font);
                int check = i == selected && w > ts.Width + M3.D(30) ? M3.D(20) : 0;
                int x = (int)(i * w + (w - ts.Width - check) / 2);
                if (check > 0) M3.Text(g, "\uE73E", gf, fg, new Rectangle(x, 0, check, Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                M3.Text(g, Options[i], Font, fg, new Rectangle(x + check, 0, ts.Width + 2, Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            }
    }
}

// Bottom navigation bar with pill indicator.
class M3NavBar : M3Control
{
    public string[] Glyphs, Labels;
    int selected;
    public event EventHandler SelectionChanged;

    public M3NavBar(string[] glyphs, string[] labels) { Glyphs = glyphs; Labels = labels; Height = M3.D(76); }

    public int SelectedIndex
    {
        get { return selected; }
        set { selected = value; Invalidate(); if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty); }
    }

    protected override void OnMouseClick(MouseEventArgs e) { SelectedIndex = Math.Min(Labels.Length - 1, e.X * Labels.Length / Width); base.OnMouseClick(e); }

    protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(M3.SurfaceContainer); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; M3.Hq(g);
        float w = Width / (float)Labels.Length;
        using (var gf = M3.Icons(13f)) using (var lf = M3.Font(9f, true))
            for (int i = 0; i < Labels.Length; i++)
            {
                bool sel = i == selected;
                float cx = i * w + w / 2;
                var pill = new RectangleF(cx - M3.D(32), M3.D(12), M3.D(64), M3.D(32));
                if (sel) M3.FillRound(g, M3.SecondaryContainer, pill, pill.Height / 2);
                M3.Text(g, Glyphs[i], gf, sel ? M3.OnSecondaryContainer : M3.OnSurfaceVariant, Rectangle.Round(pill),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                M3.Text(g, Labels[i], lf, sel ? M3.OnSurface : M3.OnSurfaceVariant,
                    new Rectangle((int)(i * w), M3.D(48), (int)w, M3.D(20)), TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
            }
    }
}

class M3ProgressBar : M3Control
{
    float value;
    public float Value { get { return value; } set { this.value = Math.Max(0, Math.Min(1, value)); Invalidate(); } }
    public M3ProgressBar() { Height = M3.D(4); Cursor = Cursors.Default; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; M3.Hq(g);
        float h = Height, x = (Width - 1) * value;
        M3.FillRound(g, M3.SecondaryContainer, new RectangleF(Math.Min(x + M3.D(4), Width - h), 0, Math.Max(h, Width - x - M3.D(4)), h), h / 2);
        if (x > 0) M3.FillRound(g, M3.Primary, new RectangleF(0, 0, Math.Max(h, x), h), h / 2);
    }
}

// Writes into Equalizer APO's config folder. Hook/Unhook need admin rights.
static class EqApo
{
    public const string FilterFileName = "BassControl.txt";
    public const string IncludeLine = "Include: " + FilterFileName;

    public static string ConfigDir()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var key = hklm.OpenSubKey(@"SOFTWARE\EqualizerAPO"))
                {
                    var p = key == null ? null : key.GetValue("ConfigPath") as string;
                    if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
                }
            }
            catch { }
        }
        var def = @"C:\Program Files\EqualizerAPO\config";
        return Directory.Exists(def) ? def : null;
    }

    public static bool IsInstalled { get { return ConfigDir() != null; } }

    // Equalizer APO's device picker. It has to be run once so the engine knows which speakers to use.
    public static string ConfiguratorPath()
    {
        try
        {
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = hklm.OpenSubKey(@"SOFTWARE\EqualizerAPO"))
            {
                var dir = key == null ? null : key.GetValue("InstallPath") as string;
                var exe = dir == null ? null : Path.Combine(dir, "Configurator.exe");
                if (exe != null && File.Exists(exe)) return exe;
            }
        }
        catch { }
        var def = @"C:\Program Files\EqualizerAPO\Configurator.exe";
        return File.Exists(def) ? def : null;
    }

    public static string FilterPath { get { var d = ConfigDir(); return d == null ? null : Path.Combine(d, FilterFileName); } }

    public static bool IsHooked()
    {
        var dir = ConfigDir();
        if (dir == null) return false;
        try
        {
            var cfg = Path.Combine(dir, "config.txt");
            if (!File.Exists(cfg) || File.ReadAllText(cfg).IndexOf(IncludeLine, StringComparison.OrdinalIgnoreCase) < 0) return false;
            using (new FileStream(Path.Combine(dir, FilterFileName), FileMode.Open, FileAccess.Write)) { }
            return true;
        }
        catch { return false; }
    }

    public static bool Hook()
    {
        try
        {
            var dir = ConfigDir();
            if (dir == null) return false;
            var filterPath = Path.Combine(dir, FilterFileName);
            if (!File.Exists(filterPath)) File.WriteAllText(filterPath, "# Managed by Bass Control\r\n");

            var sec = File.GetAccessControl(filterPath);
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.Modify, AccessControlType.Allow));
            File.SetAccessControl(filterPath, sec);

            var configPath = Path.Combine(dir, "config.txt");
            var existing = File.Exists(configPath) ? File.ReadAllText(configPath) : "";
            if (existing.IndexOf(IncludeLine, StringComparison.OrdinalIgnoreCase) < 0)
            {
                var sep = existing.Length == 0 || existing.EndsWith("\n") ? "" : "\r\n";
                File.AppendAllText(configPath, sep + IncludeLine + "\r\n");
            }
            return true;
        }
        catch { return false; }
    }

    public static void Unhook()
    {
        try
        {
            var dir = ConfigDir();
            if (dir == null) return;
            var configPath = Path.Combine(dir, "config.txt");
            if (File.Exists(configPath))
            {
                var lines = File.ReadAllLines(configPath);
                var kept = Array.FindAll(lines, l => !l.Trim().Equals(IncludeLine, StringComparison.OrdinalIgnoreCase));
                File.WriteAllLines(configPath, kept);
            }
            var filterPath = Path.Combine(dir, FilterFileName);
            if (File.Exists(filterPath)) File.Delete(filterPath);
        }
        catch { }
    }
}

static class Shell
{
    public static void CreateShortcut(string lnkPath, string target, string args, string description)
    {
        var t = Type.GetTypeFromProgID("WScript.Shell");
        object sh = Activator.CreateInstance(t);
        object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnkPath });
        var st = sc.GetType();
        st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
        st.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { args ?? "" });
        st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { description });
        st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
        st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
        st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        Marshal.FinalReleaseComObject(sc);
        Marshal.FinalReleaseComObject(sh);
    }

    public static string StartMenuLink
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppInfo.Name + ".lnk"); }
    }

    public static string DesktopLink
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), AppInfo.Name + ".lnk"); }
    }

    public static void SetStartWithWindows(string exePath, bool on)
    {
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(AppInfo.RunKey))
            {
                if (on) k.SetValue(AppInfo.RunValue, "\"" + exePath + "\" --tray");
                else if (k.GetValue(AppInfo.RunValue) != null) k.DeleteValue(AppInfo.RunValue);
            }
        }
        catch { }
    }

    public static bool StartsWithWindows()
    {
        try { using (var k = Registry.CurrentUser.OpenSubKey(AppInfo.RunKey)) return k != null && k.GetValue(AppInfo.RunValue) != null; }
        catch { return false; }
    }

    public static bool IsAdmin()
    {
        return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
