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
    private readonly TextLink _checkAgain = new() { Text = "Check again", Semibold = true };
    private readonly InfoLine _warning = new() { Tone = InfoTone.Warning };
    private readonly InfoLine _freeNote = new() { Text = HwinfoStepModel.FreeVersionNote };
    private readonly CardPanel _howTo = new() { Title = "Turn on Shared Memory" };
    private readonly List<(TextLabel Number, TextLabel Text)> _steps = [];
    private readonly ScreenshotFrame _screenshot = new(
        AppAssets.TryLoadHwinfoScreenshot(),
        "Screenshot: HWiNFO Settings, General tab, with Shared Memory Support ticked",
        "Screenshot: HWiNFO Shared Memory Support setting");

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
        const float left = 280;
        const float gap = Theme.Gap;
        var p = Px(Theme.CardPadding);
        var contentTop = _status.ContentBounds.Top;

        // Left column: status card, then the notes.
        var statusWidth = Px(left);
        var lineWidth = statusWidth - (2 * p);
        var y = contentTop;
        foreach (var line in _lines.Where(l => l.Visible))
        {
            var height = Math.Max(Px(Theme.LineHeight), line.GetPreferredHeight(lineWidth));
            line.Bounds = new Rectangle(p, y, lineWidth, height);
            y += height + Px(4);
        }

        var checkSize = _checkAgain.GetPreferredSize(Size.Empty);
        _checkAgain.Bounds = new Rectangle(statusWidth - p - checkSize.Width, y + Px(2), checkSize.Width, checkSize.Height);
        _status.Bounds = new Rectangle(0, 0, statusWidth, _checkAgain.Bottom + p);

        var noteTop = _status.Bottom + Px(gap);
        var noteWidth = _status.Width;
        foreach (var note in new[] { _warning, _freeNote }.Where(n => n.Visible))
        {
            var height = note.GetPreferredHeight(noteWidth);
            note.Bounds = new Rectangle(0, noteTop, noteWidth, height);
            noteTop += height + Px(6);
        }

        // Right column: the numbered steps (hanging indent), then the screenshot fitted to the remaining space.
        _howTo.Bounds = Dip(left + gap, 0, w - left - gap, HeightDip);
        var inner = _howTo.Width - (2 * p);
        var numberWidth = Px(16);
        var textWidth = inner - numberWidth;
        var stepTop = _howTo.ContentBounds.Top;
        foreach (var (number, text) in _steps)
        {
            var height = text.GetPreferredHeight(textWidth);
            number.Bounds = new Rectangle(p, stepTop, numberWidth, height);
            text.Bounds = new Rectangle(p + numberWidth, stepTop, textWidth, height);
            stepTop += height + Px(2);
        }

        var slotTop = stepTop + Px(6);
        var slot = _screenshot.FitInto(new Size(inner, Math.Max(Px(80), _howTo.Height - slotTop - p)));
        _screenshot.Bounds = new Rectangle(new Point(p, slotTop), slot);
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
}
