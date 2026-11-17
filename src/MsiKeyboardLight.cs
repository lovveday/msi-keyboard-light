using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// MSI Keyboard Light - lighting control for MSI SteelSeries laptop keyboards.
//
// The controller on the GE60 2PE (firmware 2.2.x) accepts only preset colours:
//   1,2,66, region, colourIndex, brightness(0-3), 0, 236
//   1,2,65, mode(1 normal, 2 gaming), 0,0,0, 236
// region: 1 left, 2 middle, 3 right ; colour: 1 red 2 orange 3 yellow 4 green
// 5 sky 6 blue 7 purple 8 white, 0 off
// Raw RGB reports are acknowledged but ignored by this firmware.

static class Hid
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);
    [DllImport("hid.dll")]
    static extern bool HidD_SetFeature(IntPtr h, byte[] buffer, int length);

    const uint GENERIC_READ = 0x80000000u, GENERIC_WRITE = 0x40000000u, SHARE = 3u, OPEN_EXISTING = 3u;
    const string HidGuid = "{4d1e55b2-f16f-11cf-88cb-001111000030}";

    public static IntPtr Open()
    {
        const string baseKey = @"SYSTEM\CurrentControlSet\Enum\HID\VID_1770&PID_FF00";
        using (RegistryKey k = Registry.LocalMachine.OpenSubKey(baseKey))
        {
            if (k == null) return IntPtr.Zero;
            foreach (string inst in k.GetSubKeyNames())
            {
                string path = @"\\?\hid#vid_1770&pid_ff00#" + inst.ToLowerInvariant() + "#" + HidGuid;
                IntPtr h = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, SHARE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h != IntPtr.Zero && h != (IntPtr)(-1)) return h;
            }
        }
        return IntPtr.Zero;
    }

    public static void Send(IntPtr dev, byte[] report)
    {
        if (!HidD_SetFeature(dev, report, report.Length))
            throw new IOException("SetFeature failed: " + Marshal.GetLastWin32Error());
    }

    public static void Close(IntPtr h)
    {
        if (h != IntPtr.Zero) CloseHandle(h);
    }

    public static void SetPreset(IntPtr dev, int region, int colour, int brightness)
    {
        Send(dev, new byte[] { 1, 2, 66, (byte)region, (byte)colour, (byte)brightness, 0, 236 });
    }

    public static void SetAll(IntPtr dev, int colour, int brightness)
    {
        SetPreset(dev, 1, colour, brightness);
        SetPreset(dev, 2, colour, brightness);
        SetPreset(dev, 3, colour, brightness);
    }

    public static void SetMode(IntPtr dev, int mode)
    {
        Send(dev, new byte[] { 1, 2, 65, (byte)mode, 0, 0, 0, 236 });
    }
}

class LightForm : Form
{
    static readonly Color[] Palette =
    {
        Color.FromArgb(255, 60, 60),
        Color.FromArgb(255, 150, 40),
        Color.FromArgb(255, 220, 60),
        Color.FromArgb(60, 220, 120),
        Color.FromArgb(80, 200, 255),
        Color.FromArgb(46, 155, 255),
        Color.FromArgb(180, 100, 255),
        Color.FromArgb(255, 245, 230)
    };

    static readonly string[] Names = { "Red", "Orange", "Yellow", "Green", "Sky", "Blue", "Purple", "White" };

    readonly IntPtr _dev;
    int _colour;
    int _brightness = 3;
    int _zone;
    string _effect = "Static";
    readonly Timer _timer = new Timer();
    int _phase;

    ComboBox _zoneBox, _effectBox;
    TrackBar _brightnessBar;
    Panel _swatch;
    Label _status;
    NotifyIcon _tray;
    bool _exiting;

    public LightForm()
    {
        _dev = Hid.Open();

        Text = "MSI Keyboard Light";
        ClientSize = new Size(430, 452);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(24, 26, 30);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9f);

        Controls.Add(Header("MSI Keyboard Light", 18, 14, 24f));

        _status = new Label();
        _status.Text = _dev == IntPtr.Zero ? "Lighting controller not found" : "Controller ready";
        _status.ForeColor = _dev == IntPtr.Zero ? Color.IndianRed : Color.MediumSeaGreen;
        _status.Location = new Point(20, 58);
        _status.AutoSize = true;
        Controls.Add(_status);

        Controls.Add(Caption("Zone", 20, 96));

        _zoneBox = new ComboBox();
        _zoneBox.Location = new Point(20, 116);
        _zoneBox.Width = 140;
        _zoneBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _zoneBox.FlatStyle = FlatStyle.Flat;
        _zoneBox.Items.AddRange(new object[] { "All zones", "Left", "Middle", "Right" });
        _zoneBox.SelectedIndex = 0;
        _zoneBox.SelectedIndexChanged += delegate { _zone = _zoneBox.SelectedIndex; Restart(); };
        Controls.Add(_zoneBox);

        Controls.Add(Caption("Colour", 190, 96));

        _swatch = new Panel();
        _swatch.Location = new Point(190, 116);
        _swatch.Size = new Size(60, 26);
        _swatch.BackColor = Palette[0];
        _swatch.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(_swatch);

        Controls.Add(Caption("Brightness", 20, 158));

        _brightnessBar = new TrackBar();
        _brightnessBar.Location = new Point(16, 178);
        _brightnessBar.Width = 330;
        _brightnessBar.Minimum = 0;
        _brightnessBar.Maximum = 3;
        _brightnessBar.TickFrequency = 1;
        _brightnessBar.Value = 3;
        _brightnessBar.SmallChange = 1;
        _brightnessBar.LargeChange = 1;
        _brightnessBar.ValueChanged += delegate { _brightness = _brightnessBar.Value; Restart(); };
        Controls.Add(_brightnessBar);

        Controls.Add(Caption("Effect", 20, 230));

        _effectBox = new ComboBox();
        _effectBox.Location = new Point(20, 250);
        _effectBox.Width = 180;
        _effectBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _effectBox.FlatStyle = FlatStyle.Flat;
        _effectBox.Items.AddRange(new object[] { "Static", "Breathing", "Wave", "Off" });
        _effectBox.SelectedIndex = 0;
        _effectBox.SelectedIndexChanged += delegate { _effect = (string)_effectBox.SelectedItem; Restart(); };
        Controls.Add(_effectBox);

        for (int i = 0; i < Palette.Length; i++)
            AddSwatch(i, 20 + i * 49, 292);

        Label note = new Label();
        note.Text = "Closing this window keeps the effect running in the tray.\r\nRight-click the tray icon to exit and stop it.\r\nThe dedicated button keeps working.";
        note.Location = new Point(20, 352);
        note.AutoSize = true;
        note.ForeColor = Color.Gray;
        note.Font = new Font("Segoe UI", 7.5f);
        Controls.Add(note);

        Button apply = MakeButton("Apply", 20, 400, 110);
        apply.Click += delegate { Restart(); };
        Controls.Add(apply);

        Button off = MakeButton("Turn off", 140, 400, 110);
        off.Click += delegate { TurnOff(); };
        Controls.Add(off);

        _timer.Interval = 140;
        _timer.Tick += delegate { Tick(); };

        _tray = new NotifyIcon();
        _tray.Icon = SystemIcons.Application;
        _tray.Text = "MSI Keyboard Light";
        ContextMenu trayMenu = new ContextMenu();
        MenuItem openItem = new MenuItem("Open");
        openItem.Click += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); };
        MenuItem exitItem = new MenuItem("Exit (stop effects)");
        exitItem.Click += delegate { _exiting = true; Close(); };
        trayMenu.MenuItems.Add(openItem);
        trayMenu.MenuItems.Add(exitItem);
        _tray.ContextMenu = trayMenu;
        _tray.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); };
        _tray.Visible = true;

        FormClosing += delegate(object s, FormClosingEventArgs e)
        {
            if (!_exiting)
            {
                e.Cancel = true;
                Hide();
                _tray.ShowBalloonTip(2500, "MSI Keyboard Light",
                    "Still running - the effect continues.\r\nRight-click the tray icon to exit.", ToolTipIcon.Info);
                return;
            }
            _timer.Stop();
            _tray.Visible = false;
            _tray.Dispose();
            Hid.Close(_dev);
        };
        Shown += delegate { Restart(); };
    }

    static Label Header(string text, int x, int y, float size)
    {
        Label l = new Label();
        l.Text = text;
        l.Location = new Point(x, y);
        l.AutoSize = true;
        l.Font = new Font("Segoe UI Semibold", size);
        l.ForeColor = Color.White;
        return l;
    }

    static Label Caption(string text, int x, int y)
    {
        Label l = new Label();
        l.Text = text;
        l.Location = new Point(x, y);
        l.AutoSize = true;
        l.ForeColor = Color.Silver;
        return l;
    }

    static Button MakeButton(string text, int x, int y, int width)
    {
        Button b = new Button();
        b.Text = text;
        b.Location = new Point(x, y);
        b.Width = width;
        b.Height = 36;
        b.FlatStyle = FlatStyle.Flat;
        return b;
    }

    void AddSwatch(int index, int x, int y)
    {
        Button b = new Button();
        b.Location = new Point(x, y);
        b.Size = new Size(44, 30);
        b.BackColor = Palette[index];
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
        b.Click += delegate { _colour = index; _swatch.BackColor = Palette[index]; Restart(); };
        Controls.Add(b);

        Label t = new Label();
        t.Text = Names[index];
        t.Location = new Point(x - 2, y + 32);
        t.AutoSize = true;
        t.Font = new Font("Segoe UI", 7f);
        t.ForeColor = Color.Gray;
        Controls.Add(t);
    }

    void Apply(int colour, int brightness)
    {
        if (_dev == IntPtr.Zero) return;
        try
        {
            if (_zone == 0) Hid.SetAll(_dev, colour, brightness);
            else Hid.SetPreset(_dev, _zone, colour, brightness);
            Hid.SetMode(_dev, 1);
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            _status.ForeColor = Color.IndianRed;
        }
    }

    void TurnOff()
    {
        _timer.Stop();
        if (_dev == IntPtr.Zero) return;
        try
        {
            if (_zone == 0)
            {
                Hid.SetPreset(_dev, 1, 0, 2);
                Hid.SetPreset(_dev, 2, 0, 2);
                Hid.SetPreset(_dev, 3, 0, 2);
            }
            else Hid.SetPreset(_dev, _zone, 0, 2);
            Hid.SetMode(_dev, 1);
            _status.Text = "Light off";
            _status.ForeColor = Color.Silver;
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            _status.ForeColor = Color.IndianRed;
        }
    }

    void Restart()
    {
        _timer.Stop();
        _phase = 0;

        if (_dev == IntPtr.Zero)
        {
            _status.Text = "Lighting controller not found";
            _status.ForeColor = Color.IndianRed;
            return;
        }

        if (_effect == "Off") { TurnOff(); return; }

        if (_effect == "Static")
        {
            Apply(_colour + 1, _brightness);
            _status.Text = Names[_colour] + " applied";
            _status.ForeColor = Color.MediumSeaGreen;
            return;
        }

        _status.Text = _effect + " running";
        _status.ForeColor = Color.MediumSeaGreen;
        _timer.Start();
    }

    void Tick()
    {
        if (_dev == IntPtr.Zero) return;
        _phase++;

        try
        {
            if (_effect == "Breathing")
            {
                int[] steps = { 0, 1, 2, 3, 3, 2, 1, 0 };
                int b = steps[_phase % steps.Length];
                b = (int)Math.Round(b * (_brightness / 3.0));
                Apply(_colour + 1, b);
            }
            else if (_effect == "Wave")
            {
                for (int region = 1; region <= 3; region++)
                {
                    if (_zone != 0 && _zone != region) continue;
                    int idx = ((_colour + (region - 1) + _phase) % Palette.Length) + 1;
                    Hid.SetPreset(_dev, region, idx, _brightness);
                }
                Hid.SetMode(_dev, 1);
            }
        }
        catch (Exception ex)
        {
            _timer.Stop();
            _status.Text = ex.Message;
            _status.ForeColor = Color.IndianRed;
        }
    }
}

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // headless: MsiKeyboardLight.exe --preset 1-8 [--brightness 0-3] | --off
        if (args.Length > 0)
        {
            IntPtr dev = Hid.Open();
            if (dev == IntPtr.Zero) return 1;
            try
            {
                if (Array.IndexOf(args, "--off") >= 0)
                {
                    Hid.SetPreset(dev, 1, 0, 2);
                    Hid.SetPreset(dev, 2, 0, 2);
                    Hid.SetPreset(dev, 3, 0, 2);
                    Hid.SetMode(dev, 1);
                    return 0;
                }

                int preset = 6;
                int pi = Array.IndexOf(args, "--preset");
                if (pi >= 0 && pi + 1 < args.Length) int.TryParse(args[pi + 1], out preset);
                if (preset < 0) preset = 0;
                if (preset > 8) preset = 8;

                int br = 3;
                int bi = Array.IndexOf(args, "--brightness");
                if (bi >= 0 && bi + 1 < args.Length) int.TryParse(args[bi + 1], out br);
                if (br < 0) br = 0;
                if (br > 3) br = 3;

                Hid.SetAll(dev, preset, br);
                Hid.SetMode(dev, 1);
                return 0;
            }
            catch { return 1; }
            finally { Hid.Close(dev); }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new LightForm());
        return 0;
    }
}
