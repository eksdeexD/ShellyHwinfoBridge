using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>
/// Step 5: the summary card, "Start with Windows" and what Apply does (Apply is the form's Next button). A failed Apply
/// shows its error here; a successful one moves the wizard on to step 6 (<see cref="VerifyStepView"/>).
/// </summary>
internal sealed class ApplyStepView : WizardStepView
{
    private const float LeftWidth = 470;
    private const float LabelWidth = 108;

    private readonly ApplyStepModel _model;
    private readonly CardPanel _summary = new() { Title = "Summary" };
    private readonly List<(TextLabel Label, TextLabel Value)> _lines = [];
    private readonly ToggleSwitch _startWithWindows = new() { Text = "Start with Windows" };
    private readonly InfoLine _applyError = new() { Tone = InfoTone.Error };
    private readonly InfoLine _beforeApply = new()
    {
        Text = "Apply saves the settings, writes the Shelly Power sensors to HWiNFO's registry key and starts polling. HWiNFO then has to be restarted to show the new sensors; the next page helps with that and checks the result.",
    };

    public ApplyStepView(WizardSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _model = session.Apply;
        Controls.AddRange([_summary, _startWithWindows, _applyError, _beforeApply]);

        _startWithWindows.CheckedChanged += (_, _) =>
        {
            if (!IsRefreshing)
            {
                _model.StartWithWindows = _startWithWindows.Checked;
            }
        };
        _model.Changed += OnModelChanged;
        RefreshView();
    }

    protected override void OnRefreshView()
    {
        var lines = _model.SummaryLines;
        while (_lines.Count < lines.Count)
        {
            var label = new TextLabel { Tone = UiTone.Secondary, VerticalCenter = false };
            var value = new TextLabel { WordWrap = true };
            _summary.Controls.Add(label);
            _summary.Controls.Add(value);
            _lines.Add((label, value));
        }

        for (var i = 0; i < _lines.Count; i++)
        {
            var visible = i < lines.Count;
            _lines[i].Label.Visible = visible;
            _lines[i].Value.Visible = visible;
            if (visible)
            {
                _lines[i].Label.Text = lines[i].Label;
                _lines[i].Value.Text = lines[i].Value;
                _lines[i].Value.Role = lines[i].Monospace ? FontRole.Mono : FontRole.Body;
            }
        }

        _startWithWindows.Checked = _model.StartWithWindows;
        _startWithWindows.Enabled = !_model.IsApplied;
        _applyError.Text = _model.ApplyError ?? string.Empty;
        _applyError.Visible = _model.ApplyError is not null;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var w = WidthDip;
        var h = HeightDip;
        const float gap = 16;

        // Left: summary lines (label column, wrapped values), then the toggle and the error.
        var summaryWidth = Px(LeftWidth);
        var valueLeft = Px(20 + LabelWidth);
        var valueWidth = summaryWidth - valueLeft - Px(20);
        var y = Px(60);
        foreach (var (label, value) in _lines.Where(l => l.Label.Visible))
        {
            var height = Math.Max(value.GetPreferredHeight(valueWidth), Px(22));
            label.Bounds = new Rectangle(Px(20), y, valueLeft - Px(28), Px(22));
            value.Bounds = new Rectangle(valueLeft, y, valueWidth, height);
            y += height + Px(10);
        }

        var maxSummary = Px(h - 40 - gap - 40);
        _summary.Bounds = new Rectangle(0, 0, summaryWidth, Math.Min(maxSummary, y + Px(10)));
        _startWithWindows.Bounds = new Rectangle(0, _summary.Bottom + Px(gap), Px(260), Px(32));
        _applyError.Bounds = new Rectangle(0, _startWithWindows.Bottom + Px(10), summaryWidth, Math.Max(Px(28), _applyError.GetPreferredHeight(summaryWidth)));

        // Right: what Apply does.
        var right = LeftWidth + gap;
        var rightPx = Px(w - right);
        _beforeApply.Bounds = new Rectangle(Px(right), 0, rightPx, _beforeApply.GetPreferredHeight(rightPx));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _model.Changed -= OnModelChanged;
        }

        base.Dispose(disposing);
    }

    private void OnModelChanged(object? sender, EventArgs e) => RefreshView();
}
