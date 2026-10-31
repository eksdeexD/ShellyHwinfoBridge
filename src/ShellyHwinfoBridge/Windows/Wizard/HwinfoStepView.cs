using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>
/// Step 1: a "Status" card (installed, running, Shared Memory Support, "Check again") with the note about the free
/// version's 12-hour limit under it, and a "Turn on Shared Memory" card with the numbered how-to and the screenshot
/// (the embedded one, scaled to fit with its aspect ratio; a bordered placeholder if it is missing).
/// </summary>
internal sealed class HwinfoStepView : WizardStepView
{
    private readonly HwinfoStepModel _model;
    private readonly CardPanel _status = new() { Title = "Status" };
    private readonly InfoLine[] _lines = [new(), new(), new()];
    private readonly PillButton _checkAgain = new() { Text = "Check again" };
    private readonly InfoLine _warning = new() { Tone = InfoTone.Warning };
    private readonly InfoLine _freeNote = new() { Text = HwinfoStepModel.FreeVersionNote };
    private readonly CardPanel _howTo = new() { Title = "Turn on Shared Memory" };
    private readonly List<(TextLabel Number, TextLabel Text)> _steps = [];
    private readonly ScreenshotSlot _screenshot = new();

    public HwinfoStepView(HwinfoStepModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        foreach (var line in _lines)
        {
            _status.Controls.Add(line);
        }

        _status.Controls.Add(_checkAgain);
        for (var i = 0; i < HwinfoStepModel.HowToSteps.Count; i++)
        {
            var number = new TextLabel { Text = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".", VerticalCenter = false };
            var text = new TextLabel { Text = HwinfoStepModel.HowToSteps[i], WordWrap = true };
            _steps.Add((number, text));
            _howTo.Controls.Add(number);
            _howTo.Controls.Add(text);
        }

        _howTo.Controls.Add(_screenshot);
        Controls.AddRange([_status, _warning, _freeNote, _howTo]);
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
        foreach (var note in new[] { _warning, _freeNote }.Where(n => n.Visible))
        {
            var height = note.GetPreferredHeight(noteWidth);
            note.Bounds = new Rectangle(0, noteTop, noteWidth, height);
            noteTop += height + Px(10);
        }

        // Right column: the numbered steps (hanging indent), then the screenshot fitted to the remaining space.
        _howTo.Bounds = Dip(left + gap, 0, w - left - gap, HeightDip);
        var inner = _howTo.Width - Px(40);
        var numberWidth = Px(22);
        var textWidth = inner - numberWidth;
        var stepTop = Px(58);
        foreach (var (number, text) in _steps)
        {
            var height = text.GetPreferredHeight(textWidth);
            number.Bounds = new Rectangle(Px(20), stepTop, numberWidth, height);
            text.Bounds = new Rectangle(Px(20) + numberWidth, stepTop, textWidth, height);
            stepTop += height + Px(4);
        }

        var slotTop = stepTop + Px(12);
        var available = new Size(inner, Math.Max(Px(80), _howTo.Height - slotTop - Px(20)));
        var aspect = _screenshot.AspectRatio;
        var slotWidth = Math.Min(available.Width, (int)(available.Height * aspect));
        var slotHeight = (int)Math.Round(slotWidth / aspect);
        _screenshot.Bounds = new Rectangle(Px(20), slotTop, slotWidth, slotHeight);
    }

    internal static InfoTone ToInfoTone(Tone tone) => tone switch
    {
        Tone.Success => InfoTone.Success,
        Tone.Warning => InfoTone.Warning,
        Tone.Error => InfoTone.Error,
        Tone.Pending => InfoTone.Pending,
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

    /// <summary>The screenshot (embedded <c>Assets/hwinfo-shared-memory.png</c>) in a 1 px Border frame, or a bordered placeholder.</summary>
    private sealed class ScreenshotSlot : ThemedControl
    {
        private readonly Image? _image = AppAssets.TryLoadHwinfoScreenshot();

        public ScreenshotSlot()
        {
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = "Screenshot: HWiNFO Settings, General / User Interface tab, with Shared Memory Support ticked";
        }

        /// <summary>Gets the width/height ratio to lay the slot out with (the image's, else 16:9).</summary>
        public float AspectRatio => _image is { Width: > 0, Height: > 0 } image ? image.Width / (float)image.Height : 16f / 9f;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var line = Math.Max(1, this.Px(1));
            if (_image is not null)
            {
                // The slot has the image's aspect ratio: draw it inside the 1 px frame.
                var state = g.Save();
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                var inner = new Rectangle(line, line, Math.Max(1, Width - (2 * line)), Math.Max(1, Height - (2 * line)));
                var scale = Math.Min(inner.Width / (float)_image.Width, inner.Height / (float)_image.Height);
                var size = new SizeF(_image.Width * scale, _image.Height * scale);
                g.DrawImage(_image, new RectangleF(inner.X + ((inner.Width - size.Width) / 2), inner.Y + ((inner.Height - size.Height) / 2), size.Width, size.Height));
                g.Restore(state);
                using var border = new SolidBrush(Theme.Border);
                g.FillRectangle(border, 0, 0, Width, line);
                g.FillRectangle(border, 0, Height - line, Width, line);
                g.FillRectangle(border, 0, 0, line, Height);
                g.FillRectangle(border, Width - line, 0, line, Height);
                return;
            }

            var r = new RectangleF(0, 0, Width, Height);
            Gfx.FillAndBorder(g, Theme.Field, Theme.Border, Math.Max(1f, this.PxF(1)), r, this.PxF(Theme.ChipRadius));
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
