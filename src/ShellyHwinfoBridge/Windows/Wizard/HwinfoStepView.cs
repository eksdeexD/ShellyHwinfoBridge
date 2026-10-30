using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>
/// Step 1: a "Status" card (installed, running, Shared Memory Support, "Check again") and a "Turn on Shared Memory" card
/// with the how-to and the screenshot slot (the embedded screenshot when present, else a bordered placeholder).
/// </summary>
internal sealed class HwinfoStepView : WizardStepView
{
    private const string Steps =
        "1.  Open HWiNFO64 (sensors-only mode is fine).\n" +
        "2.  Open Settings: the gear in the sensor window, or Program › Settings.\n" +
        "3.  On the General / User Interface tab, tick \"Shared Memory Support\".\n" +
        "4.  Click OK, then click Check again here.";

    private readonly HwinfoStepModel _model;
    private readonly CardPanel _status = new() { Title = "Status" };
    private readonly InfoLine[] _lines = [new(), new(), new()];
    private readonly PillButton _checkAgain = new() { Text = "Check again" };
    private readonly InfoLine _warning = new() { Tone = InfoTone.Warning };
    private readonly InfoLine _restartNote = new() { Text = HwinfoStepModel.RestartNote };
    private readonly InfoLine _freeNote = new() { Text = HwinfoStepModel.FreeVersionNote };
    private readonly CardPanel _howTo = new() { Title = "Turn on Shared Memory" };
    private readonly TextLabel _steps = new() { Text = Steps, WordWrap = true, Tone = UiTone.Ink };
    private readonly ScreenshotSlot _screenshot = new();

    public HwinfoStepView(HwinfoStepModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        foreach (var line in _lines)
        {
            _status.Controls.Add(line);
        }

        _status.Controls.Add(_checkAgain);
        _howTo.Controls.Add(_steps);
        _howTo.Controls.Add(_screenshot);
        Controls.AddRange([_status, _warning, _restartNote, _freeNote, _howTo]);
        _checkAgain.Click += (_, _) => _model.Refresh();
        _model.Changed += OnModelChanged;
        RefreshView();
    }

    protected override void OnRefreshView()
    {
        var lines = _model.Lines;
        for (var i = 0; i < _lines.Length; i++)
        {
            var line = i < lines.Count ? lines[i] : null;
            _lines[i].Visible = line is not null;
            if (line is not null)
            {
                _lines[i].Text = line.Text;
                _lines[i].Tone = ToInfoTone(line.Tone);
            }
        }

        _warning.Text = _model.Warning ?? string.Empty;
        _warning.Visible = _model.Warning is not null;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var w = WidthDip;
        const float left = 400;
        const float gap = 16;

        // Left column: status card, then the notes.
        var statusWidth = Px(left);
        var lineWidth = statusWidth - Px(40);
        var y = Px(60);
        foreach (var line in _lines.Where(l => l.Visible))
        {
            var height = Math.Max(Px(28), line.GetPreferredHeight(lineWidth));
            line.Bounds = new Rectangle(Px(20), y, lineWidth, height);
            y += height + Px(8);
        }

        _checkAgain.Bounds = new Rectangle(Px(20), y + Px(8), Px(150), Px(40));
        _status.Bounds = new Rectangle(0, 0, statusWidth, _checkAgain.Bottom + Px(20));

        var noteTop = _status.Bottom + Px(gap);
        var noteWidth = _status.Width;
        foreach (var note in new[] { _warning, _restartNote, _freeNote }.Where(n => n.Visible))
        {
            var height = note.GetPreferredHeight(noteWidth);
            note.Bounds = new Rectangle(0, noteTop, noteWidth, height);
            noteTop += height + Px(10);
        }

        // Right column: how-to and the 16:9 screenshot slot.
        _howTo.Bounds = Dip(left + gap, 0, w - left - gap, HeightDip);
        var inner = _howTo.Width - Px(40);
        var stepsHeight = _steps.GetPreferredHeight(inner);
        _steps.Bounds = new Rectangle(Px(20), Px(60), inner, stepsHeight);
        var slotTop = _steps.Bottom + Px(14);
        var slotHeight = Math.Max(Px(80), _howTo.Height - slotTop - Px(20));
        var slotWidth = Math.Min(inner, (int)(slotHeight * 16f / 9f));
        slotHeight = (int)(slotWidth * 9f / 16f);
        _screenshot.Bounds = new Rectangle(Px(20), slotTop, slotWidth, slotHeight);
    }

    internal static InfoTone ToInfoTone(Tone tone) => tone switch
    {
        Tone.Success => InfoTone.Success,
        Tone.Warning => InfoTone.Warning,
        Tone.Error => InfoTone.Error,
        _ => InfoTone.Info,
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _model.Changed -= OnModelChanged;
        }

        base.Dispose(disposing);
    }

    private void OnModelChanged(object? sender, EventArgs e) => RefreshView();

    /// <summary>The screenshot (embedded <c>Assets/hwinfo-shared-memory.png</c>) or a bordered placeholder.</summary>
    private sealed class ScreenshotSlot : ThemedControl
    {
        private readonly Image? _image = AppAssets.TryLoadHwinfoScreenshot();

        public ScreenshotSlot()
        {
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = "Screenshot: HWiNFO Shared Memory Support setting";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = new RectangleF(0, 0, Width, Height);
            var radius = this.PxF(Theme.ChipRadius);
            if (_image is not null)
            {
                var state = g.Save();
                using var clip = Gfx.RoundedRect(r, radius);
                g.SetClip(clip);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                var scale = Math.Min(Width / (float)_image.Width, Height / (float)_image.Height);
                var size = new SizeF(_image.Width * scale, _image.Height * scale);
                g.DrawImage(_image, new RectangleF((Width - size.Width) / 2, (Height - size.Height) / 2, size.Width, size.Height));
                g.Restore(state);
                Gfx.DrawRounded(g, Theme.Border, Math.Max(1f, this.PxF(1)), r, radius);
                return;
            }

            Gfx.FillAndBorder(g, Theme.Field, Theme.Border, Math.Max(1f, this.PxF(1)), r, radius);
            Gfx.Text(
                g,
                "Screenshot: HWiNFO Shared Memory Support setting",
                Fonts.Body,
                new Rectangle(Point.Empty, Size),
                Theme.InkDisabled,
                Gfx.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _image?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
