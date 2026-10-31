using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Theming;
using ShellyHwinfoBridge.Windows.Wizard;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// The "Customize HWiNFO sensor names" table (<see cref="SensorNamesModel"/>), hosted by a <see cref="StepDialog"/>:
/// a header row (DEFAULT NAME, YOUR NAME, "Reset to defaults") and one row per published sensor with its default name,
/// an editable name (empty = default, shown as the placeholder) and the row's validation error. The rows scroll when
/// the dialog is short. While names differ from the saved ones, the rename warning and "Show me how" (opening
/// <see cref="RenameHelpDialog"/>) sit under the rows. Nothing is applied before Save.
/// </summary>
internal sealed class SensorNamesView : WizardStepView
{
    private const float HeaderHeight = 26;
    private const float RowHeight = 34;
    private const float FieldHeight = 28;
    private const float DefaultWidth = 220;
    private const float NameWidth = 260;
    private const float WarningGap = 8;

    private readonly SensorNamesModel _model;
    private readonly TextLabel _defaultCaption = new() { Text = "Default name", Caption = true, Tone = UiTone.Secondary };
    private readonly TextLabel _nameCaption = new() { Text = "Your name", Caption = true, Tone = UiTone.Secondary };
    private readonly TextLink _reset = new() { Text = "Reset to defaults", Semibold = true };
    private readonly ScrollPanel _rows = new();
    private readonly List<(string KeyId, TextLabel Default, ThemedTextBox Name, InfoLine Error)> _controls = [];
    private readonly InfoLine _applyError = new() { Tone = InfoTone.Error, Visible = false };
    private readonly InfoLine _warning = new() { Tone = InfoTone.Warning, Text = DeviceFormModel.RenameWarningText, Visible = false };
    private readonly TextLink _showHow = new() { Text = HwinfoRenameHelp.ShowMeHowText, Semibold = true, Visible = false, AccessibleName = "Show me how to update the names in HWiNFO" };
    private bool _warningShown;

    public SensorNamesView(SensorNamesModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        AccessibleName = "HWiNFO sensor names";
        var index = 0;
        foreach (var row in model.Rows)
        {
            var keyId = row.KeyId;
            var label = new TextLabel { Text = row.DefaultName };
            var name = new ThemedTextBox { Placeholder = row.DefaultName, AccessibleName = "Your name for " + row.DefaultName, MaxLength = Core.Hwinfo.SensorNaming.MaxNameLength, TabIndex = index++ };
            var error = new InfoLine { Tone = InfoTone.Error, Visible = false };
            name.TextChanged += (_, _) =>
            {
                if (!IsRefreshing)
                {
                    _model.SetCustomName(keyId, name.Text);
                }
            };
            _rows.Controls.AddRange([label, name, error]);
            _controls.Add((keyId, label, name, error));
        }

        _reset.TabIndex = 0;
        _rows.TabIndex = 1;
        _warning.TabIndex = 2;
        _showHow.TabIndex = 3;
        Controls.AddRange([_defaultCaption, _nameCaption, _reset, _rows, _warning, _showHow, _applyError]);
        _showHow.Click += (_, _) => RenameHelpDialog.Show(FindForm(), _model.RenameHelp);
        _reset.Click += (_, _) => _model.ResetToDefaults();
        _model.Changed += OnModelChanged;
        RefreshView();
    }

    public override void OnStepShown()
    {
        if (_controls.Count > 0)
        {
            _controls[0].Name.Focus();
        }
    }

    protected override void OnRefreshView()
    {
        var rows = _model.Rows;
        for (var i = 0; i < _controls.Count && i < rows.Count; i++)
        {
            var row = rows[i];
            var (_, _, name, error) = _controls[i];
            SetTextIfChanged(name, row.CustomName);
            name.HasError = row.Error is not null;
            error.Visible = row.Error is not null;
            error.Text = row.Error ?? string.Empty;
            name.AccessibleDescription = row.Error;
        }

        _reset.Enabled = rows.Any(r => r.CustomName.Length > 0);
        _applyError.Visible = _model.ApplyError is not null;
        _applyError.Text = _model.ApplyError ?? string.Empty;

        // The rename warning with "Show me how" while names differ from the saved ones.
        var warn = _model.ShowRenameWarning;
        if (_warningShown != warn)
        {
            _warningShown = warn;
            _warning.Visible = warn;
            _showHow.Visible = warn;
            PerformLayout();
        }
    }

    /// <summary>Every row without scrolling, plus room for the rename warning (it appears while typing) and an error.</summary>
    public override float PreferredHeightDip(float widthDip)
    {
        var warning = WarningHeight(Px(widthDip)) * 96f / DeviceDpi;
        return HeaderHeight + (_controls.Count * RowHeight) + WarningGap + warning + 4 + Theme.LineHeight;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var w = WidthDip;
        var h = HeightDip;
        var resetSize = _reset.GetPreferredSize(Size.Empty);
        _defaultCaption.Bounds = Dip(2, 4, DefaultWidth, 18);
        _nameCaption.Bounds = Dip(2 + DefaultWidth + 12, 4, NameWidth, 18);
        _reset.Bounds = new Rectangle(Width - resetSize.Width, Px(2), resetSize.Width, resetSize.Height);

        var errorHeight = _applyError.Visible ? Theme.LineHeight + 4 : 0f;
        var showHow = _showHow.GetPreferredSize(Size.Empty);
        var warningWidth = Math.Max(0, Width - showHow.Width - Px(16));
        var warningHeight = _warningShown ? WarningHeight(Width) : 0;
        var warningTop = Height - Px(errorHeight) - warningHeight;
        _warning.Bounds = new Rectangle(0, warningTop, warningWidth, warningHeight);
        _showHow.Bounds = new Rectangle(Width - showHow.Width, warningTop + Math.Max(0, (warningHeight - showHow.Height) / 2), showHow.Width, showHow.Height);
        var rowsHeight = Math.Max(Px(RowHeight), warningTop - (_warningShown ? Px(WarningGap) : 0) - Px(HeaderHeight));
        _rows.Bounds = new Rectangle(0, Px(HeaderHeight), Width, rowsHeight);
        _applyError.Bounds = Dip(0, h - Theme.LineHeight, w, Theme.LineHeight);

        var scroll = _rows.AutoScrollPosition;
        var errorLeft = 2 + DefaultWidth + 12 + NameWidth + 12;
        var errorWidth = Math.Max(0, w - errorLeft - 20);
        for (var i = 0; i < _controls.Count; i++)
        {
            var (_, label, name, error) = _controls[i];
            var top = i * RowHeight;
            label.Bounds = Offset(Dip(2, top, DefaultWidth, FieldHeight), scroll);
            name.Bounds = Offset(Dip(2 + DefaultWidth + 12, top, NameWidth, FieldHeight), scroll);
            error.Bounds = Offset(Dip(errorLeft, top + 2, errorWidth, Theme.LineHeight + 2), scroll);
        }

        var content = new Size(0, Px(_controls.Count * RowHeight));
        if (_rows.AutoScrollMinSize != content)
        {
            _rows.AutoScrollMinSize = content;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _model.Changed -= OnModelChanged;
        }

        base.Dispose(disposing);
    }

    private static Rectangle Offset(Rectangle r, Point scroll) => new(r.X + scroll.X, r.Y + scroll.Y, r.Width, r.Height);

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed)
        {
            RefreshView();
        }
    }

    /// <summary>The rename warning's height for the view width (it shares its line with "Show me how"), in pixels.</summary>
    private int WarningHeight(int width)
    {
        var warningWidth = Math.Max(0, width - _showHow.GetPreferredSize(Size.Empty).Width - Px(16));
        return Math.Max(Px(Theme.LineHeight), _warning.GetPreferredHeight(warningWidth));
    }
}
