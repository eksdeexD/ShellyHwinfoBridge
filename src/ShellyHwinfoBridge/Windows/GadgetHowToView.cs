using System.Drawing.Drawing2D;
using System.Globalization;
using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// The gadget how-to as a numbered list, shared by wizard step 5 (<see cref="Wizard.GadgetStepView"/>) and the help
/// dialog (<see cref="GadgetHelpDialog"/>): the steps of <see cref="GadgetStepModel.Steps"/> with a hanging indent, the
/// selected sensors indented under step 3 ("Enable this for:"), each with a live red cross or green tick, the HWiNFO
/// status note (or, while Shared Memory is on, the note that Other Components already works) and the free version's
/// 12-hour note. While visible it refreshes the model about once per second.
/// </summary>
internal sealed class GadgetHowToView : ThemedPanel
{
    private const int NumberWidth = 18;
    private const int ItemIndent = 18;
    private const int StepGap = 4;

    private readonly GadgetStepModel _model;
    private readonly List<(TextLabel Number, TextLabel Text)> _steps = [];
    private readonly List<MarkerLine> _items = [];
    private readonly TextLabel _noSensors = new() { Text = GadgetStepModel.NoSensorsText, Tone = UiTone.Secondary };
    private readonly InfoLine _status = new() { Tone = InfoTone.Warning, Visible = false };
    private readonly TextLabel _sharedMemory = new() { Text = GadgetStepModel.SharedMemoryOnText, Role = FontRole.Small, Tone = UiTone.Secondary, WordWrap = true, VerticalCenter = false, Visible = false };
    private readonly TextLabel _note = new() { Text = GadgetStepModel.FreeVersionNote, Role = FontRole.Small, Tone = UiTone.Secondary, WordWrap = true, VerticalCenter = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    /// <summary>Creates the view over a loaded model.</summary>
    public GadgetHowToView(GadgetStepModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        AccessibleName = GadgetStepModel.Heading;
        AccessibleRole = AccessibleRole.Grouping;
        for (var i = 0; i < GadgetStepModel.Steps.Count; i++)
        {
            var number = new TextLabel { Text = (i + 1).ToString(CultureInfo.InvariantCulture) + ".", VerticalCenter = false };
            var text = new TextLabel { Text = GadgetStepModel.Steps[i], WordWrap = true };
            _steps.Add((number, text));
            Controls.Add(number);
            Controls.Add(text);
        }

        Controls.AddRange([_noSensors, _status, _sharedMemory, _note]);
        _timer.Tick += (_, _) => _model.Refresh();
        _model.Changed += OnModelChanged;
        Sync();
    }

    /// <summary>Returns the height in pixels the list needs at <paramref name="width"/> pixels.</summary>
    public int GetPreferredHeight(int width) => Arrange(width, apply: false);

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateTimer();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateTimer();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        Arrange(ClientSize.Width, apply: true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _model.Changed -= OnModelChanged;
            _timer.Stop();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void UpdateTimer()
    {
        if (Visible && IsHandleCreated && !IsDisposed)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed)
        {
            Sync();
        }
    }

    /// <summary>Copies the markers and notes from the model.</summary>
    private void Sync()
    {
        var items = _model.Items;
        while (_items.Count < items.Count)
        {
            var line = new MarkerLine();
            _items.Add(line);
            Controls.Add(line);
        }

        for (var i = 0; i < _items.Count; i++)
        {
            var visible = i < items.Count;
            _items[i].Visible = visible;
            if (visible)
            {
                _items[i].Text = items[i].Label;
                _items[i].Reported = items[i].Reported;
            }
        }

        _noSensors.Visible = items.Count == 0;
        _status.Visible = _model.StatusText is not null;
        _status.Text = _model.StatusText ?? string.Empty;
        _sharedMemory.Visible = _model.SharedMemoryText is not null;
        PerformLayout();
        Invalidate(true);
    }

    /// <summary>Lays the list out top-down at <paramref name="width"/> pixels; returns the height used.</summary>
    private int Arrange(int width, bool apply)
    {
        var dpi = DeviceDpi;
        int Px(float dip) => Theme.Scale(dip, dpi);
        var numberWidth = Px(NumberWidth);
        var textWidth = Math.Max(1, width - numberWidth);
        var lineHeight = Px(Theme.LineHeight + 2);
        var y = 0;
        for (var i = 0; i < _steps.Count; i++)
        {
            var (number, text) = _steps[i];
            var height = text.GetPreferredHeight(textWidth);
            if (apply)
            {
                number.Bounds = new Rectangle(0, y, numberWidth, height);
                text.Bounds = new Rectangle(numberWidth, y, textWidth, height);
            }

            y += height + Px(StepGap);
            if (i == GadgetStepModel.SensorListStep)
            {
                // The selected sensors, indented under step 3.
                var left = numberWidth + Px(ItemIndent);
                var count = _model.Items.Count;
                IEnumerable<Control> rows = count == 0 ? new Control[] { _noSensors } : _items.Take(count);
                foreach (var item in rows)
                {
                    if (apply)
                    {
                        item.Bounds = new Rectangle(left, y, Math.Max(0, width - left), lineHeight);
                    }

                    y += lineHeight;
                }

                y += Px(StepGap + 2);
            }
        }

        // The HWiNFO status (a warning line), then the notes in the Small size.
        var notes = new List<(Control Control, Func<int, int> Height)>();
        if (_model.StatusText is not null)
        {
            notes.Add((_status, w => Math.Max(Px(Theme.LineHeight), _status.GetPreferredHeight(w))));
        }

        if (_model.SharedMemoryText is not null)
        {
            notes.Add((_sharedMemory, _sharedMemory.GetPreferredHeight));
        }

        notes.Add((_note, _note.GetPreferredHeight));
        foreach (var (note, measure) in notes)
        {
            y += Px(6);
            var height = measure(width);
            if (apply)
            {
                note.Bounds = new Rectangle(0, y, width, height);
            }

            y += height;
        }

        return y;
    }

    /// <summary>One sensor of the list: a red cross (not reported to the gadget yet) or a green tick, then its name.</summary>
    private sealed class MarkerLine : ThemedControl
    {
        private bool _reported;

        public MarkerLine()
        {
            AccessibleRole = AccessibleRole.StaticText;
        }

        public bool Reported
        {
            get => _reported;
            set
            {
                if (_reported != value)
                {
                    _reported = value;
                    UpdateAccessibility();
                    Invalidate();
                }
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            UpdateAccessibility();
            base.OnTextChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var size = this.PxF(12);
            var box = new RectangleF(this.PxF(1), (Height - size) / 2f, size, size);
            var width = Math.Max(1.6f, this.PxF(2.2f));
            if (_reported)
            {
                Gfx.DrawCheck(g, Theme.Success, RectangleF.Inflate(box, this.PxF(2), this.PxF(2)), width);
            }
            else
            {
                Gfx.Smooth(g);
                using var pen = new Pen(Theme.Error, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                var inset = size * 0.12f;
                g.DrawLine(pen, box.Left + inset, box.Top + inset, box.Right - inset, box.Bottom - inset);
                g.DrawLine(pen, box.Right - inset, box.Top + inset, box.Left + inset, box.Bottom - inset);
            }

            var left = this.Px(12 + 12);
            Gfx.Text(g, Text, Fonts.BodySemibold, new Rectangle(left, 0, Math.Max(0, Width - left), Height), Theme.Ink);
        }

        private void UpdateAccessibility()
        {
            AccessibleName = Text;
            AccessibleDescription = _reported ? "Reported to the HWiNFO Gadget" : "Not reported to the HWiNFO Gadget yet";
        }
    }
}
