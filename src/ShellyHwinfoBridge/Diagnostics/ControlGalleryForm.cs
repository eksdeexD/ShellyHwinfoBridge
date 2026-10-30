using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Diagnostics;

/// <summary>
/// Shows every themed control with the design brief's example data, for visual checks (<c>--gallery</c> in DEBUG,
/// and rendered to PNG by <c>--render-screens</c>). Page 1 mimics the main window's frame; page 2 shows the
/// remaining controls and states.
/// </summary>
internal sealed class ControlGalleryForm : ThemedForm
{
    public const int PageCount = 2;

    private readonly DipLayout _layout = new();
    private readonly int _page;
    private readonly string _title;

    public ControlGalleryForm(int page)
    {
        _page = Math.Clamp(page, 1, PageCount);
        _title = _page == 1 ? "ShellyHwinfoBridge" : "Control gallery";
        Text = $"Control gallery ({_page}/{PageCount})";
        SuspendLayout();
        if (_page == 1)
        {
            BuildPage1();
        }
        else
        {
            BuildPage2();
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
    }

    private void BuildPage1()
    {
        var pill = _layout.Place(this, new StatusPill(), 806, 26, 270, 42);
        pill.Apply(UiTone.Success, "Online · PlugMGen3-2 · 226.9 V");

        var x = 24f;
        foreach (var (caption, value, sub, tone, placeholder) in new[]
        {
            ("AC Power (Wall)", "312.4 W", "from Shelly plug", UiTone.Ink, false),
            ("DC Power (Est.)", "294.8 W", "via TX-700 curve", UiTone.Accent, false),
            ("PSU Loss", "17.6 W", "94.4 % efficiency", UiTone.Ink, false),
            ("Other Components", "48.1 W", "DC minus CPU, GPU, RAM", UiTone.Ink, false),
        })
        {
            var card = _layout.Place(this, new StatCard(), x, 92, 251, 128);
            card.Apply(caption, value, sub, tone, placeholder);
            x += 251 + 16;
        }

        var energy = _layout.Place(this, new CardPanel { Title = "Energy" }, 24, 236, 1052, 300);
        var segmented = _layout.Place(energy, new SegmentedControl(), 1052 - 20 - 290, 14, 290, 40);
        segmented.SetItems(["Hours", "Days", "Months"], 0);

        var config = _layout.Place(this, new CardPanel(), 24, 552, 756, 150);
        _layout.Place(config, new InfoLine { Text = "PSU  Seasonic Prime Fanless TX-700 · 230 V curve · Cybenetics" }, 24, 24, 700, 26);
        _layout.Place(config, new InfoLine { Text = "Sensors  CPU Package Power, GPU Power, DIMM1 Power, DIMM3 Power" }, 24, 62, 700, 26);
        _layout.Place(config, new InfoLine { Text = "HWiNFO  running · sensors visible" }, 24, 100, 700, 26);

        _layout.Place(this, new PillButton { Text = "Settings" }, 800, 552, 276, 40);
        _layout.Place(this, new PillButton { Text = "Setup wizard" }, 800, 607, 276, 40);
        _layout.Place(this, new PillButton { Text = "Pause polling" }, 800, 662, 276, 40);
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
        _layout.Place(buttons, new TextLink { Text = "Change…", Semibold = true }, 200, 130, 90, 30);
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

        var placeholder = _layout.Place(this, new StatCard(), 560, 514, 251, 128);
        placeholder.Apply("Other Components", "Enable Shared Memory in HWiNFO to see this", string.Empty, UiTone.Ink, placeholder: true);
        var selected = _layout.Place(this, new CardPanel { Title = "Selected", Selected = true }, 827, 514, 249, 128);
        _layout.Place(selected, new InfoLine { Text = "Accent border 1.5 px" }, 20, 60, 200, 26);
    }
}
