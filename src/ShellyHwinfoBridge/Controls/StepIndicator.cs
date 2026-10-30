using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Controls;

/// <summary>
/// Wizard progress: numbered 30 DIP circles joined by a 2 DIP line, labels underneath. Completed = Success with a
/// white tick, current = Accent with a white number, pending = 1.5 px ring. The line after a completed step is Success.
/// </summary>
internal sealed class StepIndicator : ThemedControl
{
    /// <summary>Distance between circle centres in DIP.</summary>
    public const int StepSpacingDip = 82;

    private readonly List<string> _steps = [];
    private readonly HashSet<int> _completed = [];
    private int _current;

    public StepIndicator()
    {
        AccessibleRole = AccessibleRole.ProgressBar;
    }

    /// <summary>Gets the step labels.</summary>
    public IReadOnlyList<string> Steps => _steps;

    /// <summary>Gets the current step index (0-based).</summary>
    public int Current => _current;

    /// <summary>Gets the completed step indexes.</summary>
    public IReadOnlySet<int> Completed => _completed;

    /// <summary>Sets the labels.</summary>
    public void SetSteps(IEnumerable<string> labels)
    {
        _steps.Clear();
        _steps.AddRange(labels);
        Invalidate();
    }

    /// <summary>Sets the current step and the completed ones.</summary>
    public void SetState(int current, IEnumerable<int> completed)
    {
        _current = current;
        _completed.Clear();
        _completed.UnionWith(completed);
        AccessibleDescription = _current >= 0 && _current < _steps.Count ? $"Step {_current + 1} of {_steps.Count}: {_steps[_current]}" : null;
        Invalidate();
    }

    /// <summary>Preferred size: one spacing per step, circle plus label height.</summary>
    public override Size GetPreferredSize(Size proposedSize) =>
        new(this.Px(StepSpacingDip) * Math.Max(1, _steps.Count), this.Px(30 + 10) + Gfx.Measure("Ag", Fonts.Body).Height);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (_steps.Count == 0)
        {
            return;
        }

        var spacing = this.PxF(StepSpacingDip);
        var circle = this.PxF(30);
        var cy = circle / 2f + this.PxF(1);
        var line = Math.Max(1f, this.PxF(2));
        float Center(int i) => (spacing / 2f) + (i * spacing);

        for (var i = 0; i < _steps.Count - 1; i++)
        {
            var color = _completed.Contains(i) ? Theme.Success : Theme.Border;
            using var pen = new Pen(color, line);
            g.DrawLine(pen, Center(i) + (circle / 2f), cy, Center(i + 1) - (circle / 2f), cy);
        }

        var labelHeight = Gfx.Measure("Ag", Fonts.Body).Height;
        for (var i = 0; i < _steps.Count; i++)
        {
            var r = new RectangleF(Center(i) - (circle / 2f), cy - (circle / 2f), circle, circle);
            var number = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var bounds = Rectangle.Round(r);
            if (_completed.Contains(i) && i != _current)
            {
                Gfx.FillRounded(g, Theme.Success, r, circle / 2f);
                Gfx.DrawCheck(g, Theme.OnAccent, RectangleF.Inflate(r, -circle * 0.18f, -circle * 0.18f), Math.Max(1.5f, this.PxF(2.4f)));
            }
            else if (i == _current)
            {
                Gfx.FillRounded(g, Theme.Accent, r, circle / 2f);
                Gfx.Text(g, number, Fonts.BodySemibold, bounds, Theme.OnAccent, Gfx.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                Gfx.DrawRounded(g, Theme.InkSecondary, Math.Max(1f, this.PxF(1.5f)), r, circle / 2f);
                Gfx.Text(g, number, Fonts.Body, bounds, Theme.Ink, Gfx.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            var label = new Rectangle((int)(Center(i) - (spacing / 2f)), (int)(r.Bottom + this.PxF(8)), (int)spacing, labelHeight);
            Gfx.Text(g, _steps[i], Fonts.Body, label, i == _current ? Theme.Ink : Theme.InkSecondary, Gfx.SingleLine | TextFormatFlags.HorizontalCenter);
        }
    }
}
