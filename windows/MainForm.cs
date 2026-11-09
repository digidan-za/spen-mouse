// The window: the laptop's name and address in big type, a live pen indicator, speed and smoothing, tray behaviour.
sealed class MainForm : Form
{
    readonly Settings cfg = Settings.Load();
    readonly Receiver rx = new();
    readonly NotifyIcon tray;
    readonly Label nameLabel = new(), ipLabel = new(), statusLabel = new(), gainValue = new(), smoothValue = new();
    readonly Panel dot = new();
    readonly TrackBar gainBar = new(), smoothBar = new();
    readonly CheckBox flipBox = new(), holdBox = new(), startupBox = new();
    bool allowVisible, reallyClose;
    int tick;

    public MainForm(bool startMinimized)
    {
        allowVisible = !startMinimized;
        Text = "spen-mouse";
        Icon = MakeIcon();
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(440, 470);

        var col = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20, 16, 20, 8) };
        Controls.Add(col);
        int w = ClientSize.Width - 40;

        col.Controls.Add(Text_("On the phone, open spen-mouse and tap this laptop:", w, FontStyle.Regular, 10, Color.DimGray));
        Style(nameLabel, w, FontStyle.Bold, 20); col.Controls.Add(nameLabel);
        Style(ipLabel, w, FontStyle.Regular, 11, Color.DimGray); ipLabel.Margin = new Padding(0, 0, 0, 16); col.Controls.Add(ipLabel);

        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
        dot.Size = new Size(14, 14); dot.Margin = new Padding(0, 5, 8, 0); dot.BackColor = Color.Silver;
        dot.Paint += (_, e) => { e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; e.Graphics.Clear(BackColor); using var b = new SolidBrush(dot.BackColor); e.Graphics.FillEllipse(b, 0, 0, 13, 13); };
        Style(statusLabel, w - 30, FontStyle.Regular, 10);
        row.Controls.Add(dot); row.Controls.Add(statusLabel); col.Controls.Add(row);

        col.Controls.Add(Slider("Speed", gainBar, gainValue, 50, 1500, (int)cfg.Gain, w));
        col.Controls.Add(Slider("Smoothing", smoothBar, smoothValue, 0, 90, (int)(cfg.Smooth * 100), w));
        gainBar.ValueChanged += (_, _) => { rx.Gain = cfg.Gain = gainBar.Value; gainValue.Text = gainBar.Value.ToString(); cfg.Save(); };
        smoothBar.ValueChanged += (_, _) => { rx.Smooth = cfg.Smooth = smoothBar.Value / 100f; smoothValue.Text = smoothBar.Value + "%"; cfg.Save(); };

        flipBox.Text = "Flip vertical (pen up moves the cursor up)"; flipBox.AutoSize = true; flipBox.Checked = cfg.FlipY;
        flipBox.CheckedChanged += (_, _) => { rx.FlipY = cfg.FlipY = flipBox.Checked; cfg.Save(); };
        holdBox.Text = "Move only while the pen button is held"; holdBox.AutoSize = true; holdBox.Checked = cfg.HoldToMove;
        holdBox.CheckedChanged += (_, _) => { rx.HoldToMove = cfg.HoldToMove = holdBox.Checked; cfg.Save(); };
        startupBox.Text = "Start with Windows, in the tray"; startupBox.AutoSize = true; startupBox.Checked = cfg.StartWithWindows;
        startupBox.CheckedChanged += (_, _) => { cfg.StartWithWindows = startupBox.Checked; cfg.Save(); cfg.ApplyStartup(); };
        col.Controls.Add(flipBox); col.Controls.Add(holdBox); col.Controls.Add(startupBox);

        col.Controls.Add(Text_("Press the pen button once to wake the pen, then move it; tap the button to click. The pen goes back to sleep " +
            "after a few seconds of holding still. Closing this window keeps spen-mouse running in the tray. If Windows asks about the firewall, allow it.",
            w, FontStyle.Regular, 9, Color.DimGray, new Padding(0, 16, 0, 0)));

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Restore());
        menu.Items.Add("Quit", null, (_, _) => { reallyClose = true; Close(); });
        tray = new NotifyIcon { Icon = Icon, Text = "spen-mouse", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => Restore();

        rx.Gain = cfg.Gain; rx.Smooth = cfg.Smooth; rx.FlipY = cfg.FlipY; rx.HoldToMove = cfg.HoldToMove;
        rx.Start(); Beacon.Start();
        RefreshAddresses();
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    void Tick()
    {
        var age = (DateTime.UtcNow - rx.LastPacket).TotalSeconds;
        if (rx.Packets == 0) Set(Color.Silver, "Waiting for the phone. Nothing received yet.");
        else if (age < 3) Set(Color.FromArgb(40, 170, 80), $"Pen connected from {rx.Sender}. {rx.Packets:N0} packets.");
        else Set(Color.Orange, $"Phone went quiet {age:0}s ago. Hold the pen button; if it stays quiet, press it once more.");
        if (++tick % 20 == 0) RefreshAddresses();
    }

    void Set(Color c, string text) { if (dot.BackColor != c) { dot.BackColor = c; dot.Invalidate(); } statusLabel.Text = text; }

    void RefreshAddresses()
    {
        var ips = Net.Addresses().Select(a => a.ip.ToString()).Distinct().ToList();
        nameLabel.Text = Environment.MachineName;
        ipLabel.Text = ips.Count == 0 ? "Not connected to any network." : "If it does not show up, type the address: " + string.Join("  or  ", ips);
    }

    void Restore() { allowVisible = true; Show(); WindowState = FormWindowState.Normal; Activate(); }

    protected override void SetVisibleCore(bool value)   // start-minimized: never show the window at all
    {
        if (!allowVisible) { value = false; if (!IsHandleCreated) CreateHandle(); }
        base.SetVisibleCore(value);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!reallyClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true; Hide();
            tray.ShowBalloonTip(2000, "spen-mouse", "Still running here. Right-click to quit.", ToolTipIcon.None);
        }
        else tray.Visible = false;
        base.OnFormClosing(e);
    }

    static Control Slider(string title, TrackBar bar, Label value, int min, int max, int current, int w)
    {
        var p = new Panel { Width = w, Height = 58, Margin = new Padding(0) };
        p.Controls.Add(new Label { Text = title, AutoSize = true, Location = new Point(0, 0) });
        value.AutoSize = true; value.TextAlign = ContentAlignment.TopRight; value.ForeColor = Color.DimGray; value.Location = new Point(w - 60, 0); value.Width = 60;
        bar.Minimum = min; bar.Maximum = max; bar.Value = Math.Clamp(current, min, max); bar.TickStyle = TickStyle.None;
        bar.Location = new Point(-8, 20); bar.Width = w + 16; bar.AutoSize = false; bar.Height = 36;
        value.Text = title == "Smoothing" ? bar.Value + "%" : bar.Value.ToString();
        p.Controls.Add(value); p.Controls.Add(bar);
        return p;
    }

    static Label Text_(string text, int w, FontStyle style, float size, Color? color = null, Padding? margin = null) =>
        Style(new Label { Text = text }, w, style, size, color, margin);

    static Label Style(Label l, int w, FontStyle style, float size, Color? color = null, Padding? margin = null)
    {
        l.AutoSize = true; l.MaximumSize = new Size(w, 0); l.Font = new Font("Segoe UI", size, style);
        l.Margin = margin ?? new Padding(0, 0, 0, 4); if (color != null) l.ForeColor = color.Value;
        return l;
    }

    static Icon MakeIcon()   // a blue tile with a white pen stroke; no .ico file to ship
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var tile = new SolidBrush(Color.FromArgb(20, 40, 160));
        g.FillRectangle(tile, 0, 0, 32, 32);
        using var pen = new Pen(Color.White, 5) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Triangle };
        g.DrawLine(pen, 8, 24, 24, 8);
        return Icon.FromHandle(bmp.GetHicon());
    }
}
