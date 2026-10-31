using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Diagnostics;

/// <summary>
/// Shows every themed control with the design brief's example data, for visual checks (<c>--gallery</c> in DEBUG,
/// and rendered to PNG by <c>--render-screens</c>). Page 1 mimics the main window, page 3 the wizard's PSU step and
/// page 4 the settings window; pages 2 and 5 show the remaining controls and states.
/// </summary>
internal sealed class ControlGalleryForm : ThemedForm
{
    public const int PageCount = 5;

    private readonly DipLayout _layout = new();
    private readonly int _page;
    private readonly string _title;

    public ControlGalleryForm(int page)
    {
        _page = Math.Clamp(page, 1, PageCount);
        _title = _page switch { 1 => "ShellyHwinfoBridge", 3 => "Setup wizard", 4 => "Settings", _ => "Control gallery" };
        Text = $"Control gallery ({_page}/{PageCount})";
        SuspendLayout();
        switch (_page)
        {
            case 1:
                BuildPage1();
                break;
            case 2:
                BuildPage2();
                break;
            case 3:
                BuildPage3();
                break;
            case 4:
                BuildPage4();
                break;
            default:
                BuildPage5();
                break;
        }

        ResumeLayout(false);
        SetClientSizeDip(new Size(1100, 740), new Size(980, 680));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        _layout.Apply(DeviceDpi);
        base.OnLayout(levent);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var logoHeight = this.Px(46);
        var top = this.Px(24) + ((this.Px(44) - logoHeight) / 2);
        var logoWidth = AppAssets.DrawLogo(g, new Point(this.Px(34), top), logoHeight);
        var titleLeft = this.Px(34) + logoWidth + this.Px(18);
        Gfx.Text(g, _title, Fonts.AppTitle, new Rectangle(titleLeft, this.Px(24), this.Px(600), this.Px(44)), Theme.Ink);
        if (_page == 3)
        {
            Gfx.Text(g, "Choose your power supply", Fonts.PageTitle, new Rectangle(this.Px(30), this.Px(104), this.Px(1000), this.Px(48)), Theme.Ink);
            Gfx.Text(g, "Pick it from the database for the most accurate curve. If it is missing, use a generic curve by rating or enter your own.", Fonts.Subtitle, new Rectangle(this.Px(30), this.Px(152), this.Px(1040), this.Px(30)), Theme.InkSecondary);
        }
        else if (_page == 4)
        {
            Gfx.Text(g, "Changes apply immediately.", Fonts.Small, new Rectangle(this.Px(26), this.Px(700), this.Px(400), this.Px(24)), Theme.InkSecondary);
        }
    }

    private void BuildPage1()
    {
        var pill = _layout.Place(this, new StatusPill(), 806, 26, 270, 42);
        pill.Apply(UiTone.Success, "Online · PlugMGen3-2 · 226.9 V");

        var x = 24f;
        foreach (var (caption, value, sub, tone, placeholder) in new[]
        {
            ("Total Power (AC)", "348.9 W", "PC + Monitor", UiTone.Ink, false),
            ("PC Power (AC)", "312.4 W", "from Shelly plug", UiTone.Ink, false),
            ("PC Power (DC)", "294.8 W", "via TX-700 curve", UiTone.Accent, false),
            ("PC PSU Loss", "17.6 W", "94.4 % efficiency", UiTone.Ink, false),
            ("PC Other Components", "48.1 W", "DC minus CPU, GPU, RAM", UiTone.Ink, false),
        })
        {
            var card = _layout.Place(this, new StatCard(), x, 92, 197.6f, 128);
            card.Apply(caption, value, sub, tone, placeholder);
            x += 197.6f + 16;
        }

        var energy = _layout.Place(this, new CardPanel { Title = "Energy history" }, 24, 236, 1052, 300);
        var mode = _layout.Place(energy, new SegmentedControl(), 1052 - 20 - 290 - 12 - 250, 14, 250, 40);
        mode.SetItems(["Combined", "Per device"], 1);
        var segmented = _layout.Place(energy, new SegmentedControl(), 1052 - 20 - 290, 14, 290, 40);
        segmented.SetItems(["Hours", "Days", "Months"], 0);
        var chart = _layout.Place(energy, new BarChart(), 20, 74, 1012, 140);
        var hours = SampleHours();
        chart.SetData(hours, 0.4, ["0", "0.2", "0.4 kWh"], SampleGroups(hours));
        var readout = _layout.Place(energy, new InfoLine { Text = "Hover a bar" }, 220, 20, 280, 26);
        chart.HoveredIndexChanged += (_, _) => readout.Text = chart.HoveredTooltip ?? "Hover a bar";
        energy.Paint += (_, e) => PaintTotals(energy, e.Graphics);

        var config = _layout.Place(this, new CardPanel(), 24, 552, 756, 150);
        _layout.Place(config, new InfoLine { Text = "PSU  Seasonic Prime Fanless TX-700 · 230 V curve · Cybenetics" }, 24, 24, 700, 26);
        _layout.Place(config, new InfoLine { Text = "Sensors  CPU Package Power, GPU Power, DIMM1 Power, DIMM3 Power" }, 24, 62, 700, 26);
        _layout.Place(config, new InfoLine { Text = "HWiNFO  running · sensors visible" }, 24, 100, 700, 26);

        _layout.Place(this, new PillButton { Text = "Settings" }, 800, 552, 276, 40);
        _layout.Place(this, new PillButton { Text = "Pause polling" }, 800, 607, 276, 40);
    }

    private void BuildPage2()
    {
        // Buttons and links.
        var buttons = _layout.Place(this, new CardPanel { Title = "Buttons and links" }, 24, 92, 516, 190);
        _layout.Place(buttons, new PillButton { Text = "Next", Variant = PillVariant.Primary }, 20, 64, 108, 42);
        _layout.Place(buttons, new PillButton { Text = "Back" }, 140, 64, 108, 42);
        _layout.Place(buttons, new PillButton { Text = "Disabled", Enabled = false }, 260, 64, 108, 42);
        _layout.Place(buttons, new PillButton { Text = "Off", Variant = PillVariant.Primary, Enabled = false }, 380, 64, 108, 42);
        _layout.Place(buttons, new TextLink { Text = "Cancel", Tone = LinkTone.Ink }, 20, 130, 80, 30);
        _layout.Place(buttons, new TextLink { Text = "Change", Underline = true }, 110, 130, 80, 30);
        _layout.Place(buttons, new TextLink { Text = "Change", Semibold = true }, 200, 130, 90, 30);
        _layout.Place(buttons, new TextLink { Text = "Remove HWiNFO sensors", Tone = LinkTone.Danger }, 300, 130, 200, 30);

        // Status pills and segmented controls.
        var states = _layout.Place(this, new CardPanel { Title = "Status and segments" }, 560, 92, 516, 190);
        _layout.Place(states, new StatusPill(), 20, 60, 230, 42).Apply(UiTone.Error, "Offline · PlugMGen3-2");
        _layout.Place(states, new StatusPill(), 266, 60, 230, 42).Apply(UiTone.Secondary, "Paused · PlugMGen3-2");
        var voltage = _layout.Place(states, new SegmentedControl(), 20, 122, 300, 40);
        voltage.SetItems(["Auto", "115 V", "230 V"], 0);
        var disabled = _layout.Place(states, new SegmentedControl { Enabled = false }, 336, 122, 160, 40);
        disabled.SetItems(["On", "Off"], 1);

        // Info lines.
        var info = _layout.Place(this, new CardPanel { Title = "Info lines" }, 24, 298, 1052, 200);
        _layout.Place(info, new InfoLine { Text = "Input voltage: 230 V curve (plug measured 226.9 V)", LinkText = "Change" }, 20, 58, 1000, 26);
        _layout.Place(info, new InfoLine { Text = "IA Cores Power is part of CPU Package Power. Selecting both would count it twice.", Tone = InfoTone.Warning }, 20, 92, 1000, 26);
        _layout.Place(info, new InfoLine { Text = "The device rejected the password (HTTP 401).", Tone = InfoTone.Error, LinkText = "Continue anyway" }, 20, 126, 1000, 26);
        _layout.Place(info, new InfoLine { Text = "Connected to PlugMGen3-2 (S3PL-10112EU).", Tone = InfoTone.Success }, 20, 160, 1000, 26);

        // Value tiles and a placeholder stat card.
        var tiles = _layout.Place(this, new CardPanel { Title = "Value tiles" }, 24, 514, 516, 190);
        var x = 20f;
        foreach (var (caption, value) in new[] { ("Power", "312.4 W"), ("Voltage", "226.9 V"), ("Current", "1.38 A") })
        {
            var tile = _layout.Place(tiles, new ValueTile(), x, 64, 150, 72);
            tile.Caption = caption;
            tile.Value = value;
            x += 162;
        }

        x = 20f;
        foreach (var (colour, clickable) in new[] { (Theme.Accent, false), (Color.FromArgb(0x00, 0x99, 0xFF), true), (Color.FromArgb(0x8B, 0xC3, 0x4A), true) })
        {
            _layout.Place(tiles, new ColourSwatch { Colour = colour, Clickable = clickable }, x, 146, 28, 28);
            x += 40;
        }

        var placeholder = _layout.Place(this, new StatCard(), 560, 514, 251, 128);
        placeholder.Apply("PC Other Components", "Needs HWiNFO's Gadget export for: GPU Power", string.Empty, UiTone.Ink, placeholder: true);
        placeholder.ReservedBottomDip = 46;
        _layout.Place(placeholder, new PillButton { Text = "How to enable?" }, 24, 84, 132, 30);
        var selected = _layout.Place(this, new CardPanel { Title = "Selected", Selected = true }, 827, 514, 249, 128);
        _layout.Place(selected, new InfoLine { Text = "Accent border 1.5 px" }, 20, 60, 200, 26);
    }

    private static List<ChartBar> SampleHours()
    {
        var values = new[]
        {
            0.05, 0.04, 0.03, 0.03, 0.02, 0.02, 0.02, 0.02, 0.03, 0.05, 0.08, 0.10, 0.12, 0.13, 0.15, 0.14, 0.16, 0.18, 0.21, 0.24, 0.27, 0.30, 0.33, 0.34,
            0.23, 0.16, 0.12, 0.09, 0.07, 0.06, 0.05, 0.04, 0.04, 0.04, 0.04, 0.04, 0.06, 0.08, 0.08, 0.10, 0.12, 0.14, 0.15, 0.17, 0.19, 0.23, 0.28, 0.34,
        };
        var bars = new List<ChartBar>();
        var start = new DateTime(2026, 10, 28, 20, 0, 0);
        for (var i = 0; i < values.Length; i++)
        {
            var hour = start.AddHours(i);
            var label = i % 4 == 0 ? hour.ToString("HH", System.Globalization.CultureInfo.InvariantCulture) : null;
            bars.Add(new ChartBar(values[i], label, [$"{hour:ddd HH:mm}", $"{values[i]:0.00} kWh", $"PC est. {values[i] * 0.92:0.00} kWh"]));
        }

        return bars;
    }

    /// <summary>The sample hours split into PC (orange) and Monitor (the complementary blue) for the grouped chart.</summary>
    private static List<ChartGroup> SampleGroups(IReadOnlyList<ChartBar> hours)
    {
        var monitorColour = Color.FromArgb(0x00, 0x99, 0xFF);
        return hours.Select(h =>
        {
            var monitor = h.Value > 0.09 ? 0.04 : 0.0;
            var when = h.TooltipLines[0];
            return new ChartGroup(h.Label,
            [
                new ChartBar(h.Value - monitor, h.Label, [when, "PC", $"{h.Value - monitor:0.00} kWh"], Theme.Accent),
                new ChartBar(monitor, h.Label, [when, "Monitor", $"{monitor:0.00} kWh"], monitorColour),
            ]);
        }).ToList();
    }

    private static void PaintTotals(CardPanel card, Graphics g)
    {
        var f = ThemeFonts.For(card.DeviceDpi);
        var y = card.Px(232);
        var x = card.Px(24);
        foreach (var (value, caption) in new[] { ("2.41 kWh", "TODAY"), ("48.7 kWh", "THIS MONTH") })
        {
            Gfx.Text(g, value, f.TotalValue, new Rectangle(x, y, card.Px(200), card.Px(32)), Theme.Ink);
            Gfx.DrawTracked(g, caption, f.Caption, new Point(x, y + card.Px(36)), Theme.InkSecondary, card.PxF(Theme.CaptionTracking));
            x += card.Px(217);
            if (caption == "TODAY")
            {
                using var pen = new Pen(Theme.Border, Math.Max(1, card.Px(1)));
                g.DrawLine(pen, x - card.Px(1), y + card.Px(2), x - card.Px(1), y + card.Px(46));
                x += card.Px(42);
            }
        }
    }

    private void BuildPage3()
    {
        var steps = _layout.Place(this, new StepIndicator(), 345, 26, 410, 66);
        steps.SetSteps(["HWiNFO", "Shelly", "PSU", "Sensors", "Apply"]);
        steps.SetState(2, [0, 1]);

        var x = 24f;
        foreach (var (title, description, isChecked) in new[]
        {
            ("From database", "Measured efficiency curves from Cybenetics reports", true),
            ("Generic by rating", "Approximate curve from the 80 Plus minimum requirements", false),
            ("Manual curve", "Enter your own AC/DC points or import a CSV", false),
        })
        {
            var card = _layout.Place(this, new RadioCard { Title = title, Description = description, Group = "mode" }, x, 196, 340, 94);
            card.Checked = isChecked;
            x += 340 + 16;
        }

        x = 24f;
        foreach (var (prefix, value) in new[] { ("Brand", "Seasonic"), ("Wattage", "700 W"), ("Rating", "Titanium") })
        {
            var dropDown = _layout.Place(this, new ThemedDropDown { Prefix = prefix }, x, 308, 251, 46);
            dropDown.SetItems([new DropDownItem(null, "Any"), new DropDownItem(value, value)], 1);
            x += 251 + 16;
        }

        _layout.Place(this, new ThemedTextBox { Placeholder = "Search model…" }, x, 308, 253, 46);
        var list = _layout.Place(this, new SelectableList(), 24, 372, 1052, 213);
        list.RowHeightDip = 71;
        list.SetRows(
        [
            new SelectableRow("Prime Fanless TX-700", "80 Plus Titanium · Cybenetics ETA Titanium", "report 21PS1872A ↗"),
            new SelectableRow("Prime TX-750", "80 Plus Titanium · Cybenetics ETA Titanium", "report 20PS1620A ↗"),
            new SelectableRow("Prime PX-750", "80 Plus Platinum · Cybenetics ETA Platinum", "report 20PS1588A ↗"),
        ], 0);
        _layout.Place(this, new InfoLine { Text = "Input voltage: 230 V curve (plug measured 226.9 V)", LinkText = "Change" }, 28, 606, 700, 28);
        _layout.Place(this, new TextLink { Text = "Cancel", Tone = LinkTone.Ink }, 30, 664, 70, 32);
        _layout.Place(this, new PillButton { Text = "Back" }, 846, 660, 108, 42);
        _layout.Place(this, new PillButton { Text = "Next", Variant = PillVariant.Primary }, 968, 660, 108, 42);
    }

    private void BuildPage4()
    {
        _layout.Place(this, new PillButton { Text = "Re-run setup wizard" }, 876, 30, 200, 40);

        var shelly = _layout.Place(this, new CardPanel { Title = "Shelly device" }, 24, 86, 518, 160);
        _layout.Place(shelly, new InfoLine { Text = "Host" }, 20, 52, 200, 22);
        _layout.Place(shelly, new InfoLine { Text = "Password" }, 268, 52, 200, 22);
        _layout.Place(shelly, new ThemedTextBox { Text = "192.168.1.42" }, 20, 76, 232, 36);
        _layout.Place(shelly, new ThemedTextBox { Text = "password", UseSystemPasswordChar = true }, 268, 76, 230, 36);
        _layout.Place(shelly, new PillButton { Text = "Test connection" }, 20, 118, 174, 36);

        var psu = _layout.Place(this, new CardPanel { Title = "Power supply" }, 558, 86, 518, 160);
        _layout.Place(psu, new InfoLine { Text = "Seasonic Prime Fanless TX-700" }, 20, 54, 380, 24);
        _layout.Place(psu, new TextLink { Text = "Change", Semibold = true }, 418, 52, 80, 28);
        _layout.Place(psu, new InfoLine { Text = "700 W · Titanium · Cybenetics 21PS1872A" }, 20, 80, 380, 24);
        _layout.Place(psu, new InfoLine { Text = "Input voltage" }, 20, 116, 120, 30);
        _layout.Place(psu, new SegmentedControl(), 134, 112, 364, 38).SetItems(["Auto", "115 V", "230 V"], 0);

        var sensors = _layout.Place(this, new CardPanel { Title = "Sensors and formula" }, 24, 262, 518, 155);
        _layout.Place(sensors, new ThemedTextBox
        {
            Multiline = true,
            Monospace = true,
            ReadOnly = true,
            Text = "PC Power (DC)\r\n − CPU Package Power\r\n − GPU Power\r\n − DIMM1 Power\r\n − DIMM3 Power",
        }, 20, 50, 410, 96);
        _layout.Place(sensors, new TextLink { Text = "Edit", Semibold = true }, 446, 50, 60, 28);

        var polling = _layout.Place(this, new CardPanel { Title = "Polling" }, 558, 262, 518, 155);
        _layout.Place(polling, new InfoLine { Text = "Interval" }, 20, 62, 90, 28);
        _layout.Place(polling, new ThemedSlider { Minimum = 500, Maximum = 5000, SmallChange = 100, Value = 1000 }, 112, 62, 300, 28);
        _layout.Place(polling, new InfoLine { Text = "1000 ms" }, 426, 62, 80, 28);
        _layout.Place(polling, new InfoLine { Text = "Smoothing" }, 20, 106, 90, 28);
        _layout.Place(polling, new ThemedSlider { Minimum = 1, Maximum = 30, Value = 1 }, 112, 106, 300, 28);
        _layout.Place(polling, new InfoLine { Text = "1 sample" }, 426, 106, 80, 28);

        var energy = _layout.Place(this, new CardPanel { Title = "Energy" }, 24, 433, 518, 144);
        _layout.Place(energy, new ToggleSwitch { Text = "Log energy use", Checked = true }, 20, 52, 220, 28);
        _layout.Place(energy, new InfoLine { Text = "Price per kWh" }, 20, 94, 110, 36);
        _layout.Place(energy, new ThemedTextBox { Text = "0.32" }, 134, 92, 170, 38);
        _layout.Place(energy, new InfoLine { Text = "Currency" }, 320, 94, 70, 36);
        _layout.Place(energy, new ThemedTextBox { Text = "DKK" }, 390, 92, 108, 38);

        var startup = _layout.Place(this, new CardPanel { Title = "Startup and tray" }, 558, 433, 518, 144);
        _layout.Place(startup, new ToggleSwitch { Text = "Start with Windows", Checked = true }, 20, 56, 260, 28);
        _layout.Place(startup, new ToggleSwitch { Text = "Show tray icon", Checked = true }, 20, 96, 260, 28);

        var database = _layout.Place(this, new CardPanel { Title = "PSU database" }, 24, 593, 518, 95);
        _layout.Place(database, new InfoLine { Text = "Last updated 2026-10-29 · 1 entry" }, 20, 52, 280, 28);
        _layout.Place(database, new PillButton { Text = "Check for update" }, 318, 26, 180, 40);

        var maintenance = _layout.Place(this, new CardPanel { Title = "Maintenance" }, 558, 593, 518, 95);
        _layout.Place(maintenance, new PillButton { Text = "Open log folder" }, 20, 50, 168, 36);
        _layout.Place(maintenance, new TextLink { Text = "Remove HWiNFO sensors", Tone = LinkTone.Danger }, 206, 53, 180, 30);
    }

    private void BuildPage5()
    {
        var sensors = _layout.Place(this, new CardPanel(), 24, 84, 1052, 270);
        var y = 20f;
        foreach (var (name, value, isChecked, category) in new[]
        {
            ("CPU Package Power", "142.3 W", true, "CPU"),
            ("IA Cores Power", "118.0 W", false, "—"),
            ("GPU Power", "88.6 W", true, "GPU"),
            ("DIMM1 Power", "8.1 W", true, "RAM"),
        })
        {
            _layout.Place(sensors, new ThemedCheckBox { Text = name, Checked = isChecked }, 20, y, 400, 36);
            _layout.Place(sensors, new InfoLine { Text = value }, 610, y, 120, 36);
            var dropDown = _layout.Place(sensors, new ThemedDropDown(), 877, y, 155, 32);
            dropDown.SetItems(new[] { "—", "CPU", "GPU", "RAM", "Other measured" }.Select(c => new DropDownItem(c, c)), Array.IndexOf(new[] { "—", "CPU", "GPU", "RAM", "Other measured" }, category));
            y += 50;
        }

        _layout.Place(sensors, new ThemedCheckBox { Text = "Disabled", Enabled = false }, 20, y, 200, 36);
        _layout.Place(sensors, new ToggleSwitch { Text = "Off", Checked = false }, 240, y, 200, 36);
        _layout.Place(sensors, new ToggleSwitch { Text = "Disabled", Enabled = false }, 460, y, 200, 36);

        var grid = _layout.Place(this, new DataGridView(), 24, 370, 516, 318);
        grid.Columns.Add("ac", "AC W");
        grid.Columns.Add("dc", "DC W");
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        foreach (var (ac, dc) in new[] { (77.9, 70.0), (151.9, 141.7), (370.7, 350.0), (741.7, 700.0) })
        {
            grid.Rows.Add(ac.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture), dc.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture));
        }

        GridStyler.Apply(grid);

        var empty = _layout.Place(this, new SelectableList { EmptyText = "Searching…" }, 560, 370, 516, 108);
        empty.SetRows([]);
        var devices = _layout.Place(this, new SelectableList { RowHeightDip = 56 }, 560, 494, 516, 194);
        devices.SetRows(Enumerable.Range(1, 6).Select(i => new SelectableRow($"PlugMGen3-{i}", $"S3PL-10112EU · 192.168.1.{40 + i}")), 1);
    }
}