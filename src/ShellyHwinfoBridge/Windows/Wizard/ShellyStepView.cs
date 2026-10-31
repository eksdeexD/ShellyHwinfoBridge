using System.Globalization;
using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Shelly.Discovery;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>
/// Step 2. It opens with the question card ("Is your Shelly plug already on your Wi-Fi network?" with "Yes, find it"
/// and "No, show me how to set it up"). "No" shows the setup guide card (numbered steps, "Done, search now"); "Yes"
/// and "Done, search now" show "Found on your network" (mDNS list, "Search again") and "Connection" (host, password,
/// "Test connection", Power/Voltage/Current tiles, result line with "Continue anyway"), plus the fixed-IP tip. After a
/// successful test the "Add a Shelly for your monitor or another device" card lists the added devices (Remove) and
/// opens the shared <see cref="DeviceFormView"/> in place of the step content while a device is being added.
/// </summary>
internal sealed class ShellyStepView : WizardStepView
{
    private readonly ShellyStepModel _model;

    // Question.
    private readonly CardPanel _question = new() { Title = ShellyStepModel.SetupQuestion };
    private readonly TextLink _yes = new() { Text = ShellyStepModel.ReadyChoiceText, Semibold = true };
    private readonly TextLink _no = new() { Text = ShellyStepModel.GuideChoiceText, Semibold = true };

    // Setup guide.
    private readonly CardPanel _guide = new() { Title = ShellyStepModel.GuideTitle };
    private readonly List<(TextLabel Number, TextLabel Text)> _guideSteps = [];
    private readonly TextLink _done = new() { Text = ShellyStepModel.GuideDoneText, Semibold = true };

    // Discovery and connection.
    private readonly CardPanel _found = new() { Title = "Found on your network" };
    private readonly SelectableList _list = new() { RowHeightDip = 44, AccessibleName = "Shelly plugs found" };
    private readonly TextLabel _discoveryStatus = new() { Tone = UiTone.Secondary };
    private readonly PillButton _searchAgain = new() { Text = "Search again" };
    private readonly CardPanel _connection = new() { Title = "Connection" };
    private readonly TextLabel _hostLabel = new() { Text = "Host", Tone = UiTone.Secondary };
    private readonly TextLabel _passwordLabel = new() { Text = "Password", Tone = UiTone.Secondary };
    private readonly ThemedTextBox _host = new() { Placeholder = "IP address or host name", AccessibleName = "Host" };
    private readonly ThemedTextBox _password = new() { Placeholder = "leave empty if not set", UseSystemPasswordChar = true, AccessibleName = "Password" };
    private readonly PillButton _test = new() { Text = "Test connection" };
    private readonly ValueTile _power = new() { Caption = "Power" };
    private readonly ValueTile _voltage = new() { Caption = "Voltage" };
    private readonly ValueTile _current = new() { Caption = "Current" };
    private readonly InfoLine _result = new();
    private readonly InfoLine _tip = new() { Text = ShellyStepModel.FixedIpTip };

    // Additional devices (after a successful test of the PC's plug).
    private readonly CardPanel _addCard = new() { AccessibleName = DeviceFormModel.AddTitle };
    private readonly TextLabel _addTitle = new() { Text = DeviceFormModel.AddTitle, Role = FontRole.ItemTitle, WordWrap = true };
    private readonly TextLink _addDevice = new() { Text = "Add device", Semibold = true };
    private readonly InfoLine _addHint = new();
    private readonly List<(TextLabel Text, TextLink Remove)> _addedRows = [];
    private DeviceFormView? _formView;
    private IReadOnlyList<DiscoveredShelly>? _shownDevices;
    private ShellySetupMode? _shownMode;

    public ShellyStepView(ShellyStepModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _question.Controls.AddRange([_yes, _no]);
        for (var i = 0; i < ShellyStepModel.GuideSteps.Count; i++)
        {
            var number = new TextLabel { Text = (i + 1).ToString(CultureInfo.InvariantCulture) + ".", VerticalCenter = false };
            var text = new TextLabel { Text = ShellyStepModel.GuideSteps[i], WordWrap = true };
            _guideSteps.Add((number, text));
            _guide.Controls.Add(number);
            _guide.Controls.Add(text);
        }

        _guide.Controls.Add(_done);
        _found.Controls.AddRange([_list, _discoveryStatus, _searchAgain]);
        _connection.Controls.AddRange([_hostLabel, _passwordLabel, _host, _password, _test, _power, _voltage, _current, _result]);
        _addCard.Controls.AddRange([_addTitle, _addDevice, _addHint]);
        Controls.AddRange([_question, _guide, _found, _connection, _tip, _addCard]);
        _addDevice.Click += (_, _) => _model.BeginAddDevice();

        _yes.Click += async (_, _) => await _model.FindAsync();
        _no.Click += (_, _) => _model.ShowGuide();
        _done.Click += async (_, _) => await _model.FindAsync();
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (!IsRefreshing && _list.SelectedIndex >= 0 && _list.SelectedIndex < _model.Devices.Count)
            {
                _model.SelectDevice(_model.Devices[_list.SelectedIndex]);
            }
        };
        _host.TextChanged += (_, _) =>
        {
            if (!IsRefreshing)
            {
                _model.Host = _host.Text;
            }
        };
        _password.TextChanged += (_, _) =>
        {
            if (!IsRefreshing)
            {
                _model.Password = _password.Text.Length == 0 ? null : _password.Text;
            }
        };
        _searchAgain.Click += async (_, _) => await _model.DiscoverAsync();
        _test.Click += async (_, _) => await _model.TestAsync();
        _result.LinkClicked += (_, _) => ContinueAnywayRequested?.Invoke(this, EventArgs.Empty);
        _model.Changed += OnModelChanged;
        RefreshView();
    }

    /// <summary>Raised when "Continue anyway" is clicked after a failed test.</summary>
    public event EventHandler? ContinueAnywayRequested;

    public override async void OnStepShown()
    {
        FocusForMode();
        if (_model.ShouldDiscoverOnShow)
        {
            await _model.DiscoverAsync();
        }
    }

    protected override void OnRefreshView()
    {
        var mode = _model.SetupMode;
        SyncFormView();
        var formOpen = _formView is not null;
        _question.Visible = mode == ShellySetupMode.Unanswered;
        _guide.Visible = mode == ShellySetupMode.Guide;
        foreach (var control in new Control[] { _found, _connection, _tip })
        {
            control.Visible = mode == ShellySetupMode.Ready && !formOpen;
        }

        _addCard.Visible = _model.ShowAddDeviceCard && !formOpen;
        RefreshAdded();

        if (!ReferenceEquals(_shownDevices, _model.Devices))
        {
            _shownDevices = _model.Devices;
            _list.SetRows(_model.Devices.Select(d => new SelectableRow(
                d.DisplayName,
                string.Join(" · ", new[] { d.DisplayModel, d.Host }.Where(s => !string.IsNullOrWhiteSpace(s))))));
        }

        _list.EmptyText = _model.DeviceListEmptyText;
        _list.SelectedIndex = _model.SelectedDeviceIndex;
        _list.Invalidate();
        _discoveryStatus.Text = _model.DiscoveryStatusText;
        _searchAgain.Enabled = !_model.IsDiscovering;

        SetTextIfChanged(_host, _model.Host);
        SetTextIfChanged(_password, _model.Password);
        _host.HasError = _model.HostError is not null;
        _test.Enabled = _model.TestState != ShellyTestState.Testing && _model.IsHostValid;
        _power.Value = _model.PowerText;
        _voltage.Value = _model.VoltageText;
        _current.Value = _model.CurrentText;

        var line = _model.HostError is { } hostError ? new WizardStatusLine(Core.Presentation.Tone.Error, hostError) : _model.ResultLine;
        _result.Visible = line is not null;
        if (line is not null)
        {
            _result.Text = line.Text;
            _result.Tone = HwinfoStepView.ToInfoTone(line.Tone);
            _result.LinkText = _model.OfferContinueAnyway ? "Continue anyway" : null;
        }

        if (_shownMode is { } shown && shown != mode && Visible && IsHandleCreated)
        {
            // The clicked button just disappeared: move the focus into the new view.
            BeginInvoke(new Action(FocusForMode));
        }

        _shownMode = mode;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        LayoutQuestion();
        LayoutGuide();
        LayoutDiscovery();
        if (_formView is not null)
        {
            _formView.Bounds = new Rectangle(Point.Empty, Size);
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

    /// <summary>Creates the inline device form when the model opened one, and removes it when it closed.</summary>
    private void SyncFormView()
    {
        var form = _model.DeviceForm;
        if (ReferenceEquals(_formView?.Form, form))
        {
            return;
        }

        if (_formView is { } old)
        {
            _formView = null;
            Controls.Remove(old);
            old.Dispose();
            if (Visible && IsHandleCreated)
            {
                BeginInvoke(new Action(() =>
                {
                    if (!IsDisposed && _addDevice.Visible && _addDevice.Enabled)
                    {
                        _addDevice.Focus();
                    }
                }));
            }
        }

        if (form is null)
        {
            return;
        }

        var view = new DeviceFormView(form, inline: true) { TabIndex = 0 };
        view.SaveRequested += (_, _) => _model.CommitDeviceForm();
        view.CancelRequested += (_, _) => _model.CancelDeviceForm();
        _formView = view;
        Controls.Add(view);
        view.BringToFront();
        view.Bounds = new Rectangle(Point.Empty, Size);
        if (IsHandleCreated)
        {
            BeginInvoke(new Action(() =>
            {
                if (ReferenceEquals(_formView, view) && !view.IsDisposed)
                {
                    view.OnStepShown();
                }
            }));
        }
    }

    /// <summary>Fills the add-device card: the hint at the cap and one row per added device with a Remove link.</summary>
    private void RefreshAdded()
    {
        _addDevice.Enabled = _model.CanAddDevice;
        var hint = _model.AddDeviceHint;
        _addHint.Text = hint ?? (_model.AdditionalDevices.Count == 0
            ? "Optional. An added plug is measured on the AC side and counts in Total Power and the energy totals."
            : string.Empty);
        _addHint.Visible = _addHint.Text.Length > 0;

        var devices = _model.AdditionalDevices;
        while (_addedRows.Count < devices.Count)
        {
            var text = new TextLabel();
            var remove = new TextLink { Text = "Remove", Tone = LinkTone.Danger, Semibold = true };
            var index = _addedRows.Count;
            remove.Click += (_, _) =>
            {
                if (index < _model.AdditionalDevices.Count)
                {
                    _model.RemoveAdditionalDevice(_model.AdditionalDevices[index].Id);
                }
            };
            _addCard.Controls.Add(text);
            _addCard.Controls.Add(remove);
            _addedRows.Add((text, remove));
        }

        for (var i = 0; i < _addedRows.Count; i++)
        {
            var visible = i < devices.Count;
            var (text, remove) = _addedRows[i];
            text.Visible = visible;
            remove.Visible = visible;
            if (visible)
            {
                var d = devices[i];
                var parts = new[] { d.Label, d.Host, d.IncludeInTotals ? null : "not in totals" }.Where(s => !string.IsNullOrWhiteSpace(s));
                text.Text = string.Join(" · ", parts);
                remove.AccessibleName = "Remove " + d.Label;
            }
        }
    }

    private void LayoutQuestion()
    {
        // One card: the question as its title, the two answers (text commands) side by side under it.
        var p = Px(Theme.CardPadding);
        var yes = _yes.GetPreferredSize(Size.Empty);
        var no = _no.GetPreferredSize(Size.Empty);
        var titleWidth = Gfx.Measure(ShellyStepModel.SetupQuestion, Fonts.CardTitle).Width;
        var width = Math.Min(Width, Math.Max(titleWidth, yes.Width + Px(24) + no.Width) + (2 * p));
        var top = _question.ContentBounds.Top;
        _yes.Bounds = new Rectangle(p, top, yes.Width, yes.Height);
        _no.Bounds = new Rectangle(_yes.Right + Px(24), top, no.Width, no.Height);
        _question.Bounds = new Rectangle(0, 0, width, _yes.Bottom + p);
    }

    private void LayoutGuide()
    {
        // Full width: numbered steps with a hanging indent (as in step 1), then "Done, search now".
        var p = Px(Theme.CardPadding);
        var width = Width;
        var inner = width - (2 * p);
        var numberWidth = Px(16);
        var textWidth = inner - numberWidth;
        var top = _guide.ContentBounds.Top;
        foreach (var (number, text) in _guideSteps)
        {
            var height = text.GetPreferredHeight(textWidth);
            number.Bounds = new Rectangle(p, top, numberWidth, height);
            text.Bounds = new Rectangle(p + numberWidth, top, textWidth, height);
            top += height + Px(4);
        }

        var done = _done.GetPreferredSize(Size.Empty);
        _done.Bounds = new Rectangle(p, top + Px(6), done.Width, done.Height);
        _guide.Bounds = new Rectangle(0, 0, width, Math.Min(Height, _done.Bottom + p));
    }

    private void LayoutDiscovery()
    {
        var w = WidthDip;
        var h = HeightDip;
        const float gap = Theme.Gap;
        const float p = Theme.CardPadding;
        var half = (w - gap) / 2;
        var t = _found.ContentBounds.Top * 96f / DeviceDpi;

        // With the add-device card, the left column splits: the discovery card on top, the add-device card under it
        // (Add device and its hint, then one row per added device with Remove).
        var addHeight = 0f;
        if (_addCard.Visible)
        {
            // The title wraps (the card is half the step wide).
            var rows = _addedRows.Count(r => r.Text.Visible);
            var titleHeight = _addTitle.GetPreferredHeight(Px(half - (2 * p))) * 96f / DeviceDpi;
            var commandTop = p + titleHeight + 4;
            var hintHeight = _addHint.Visible ? _addHint.GetPreferredHeight(Px(half - (2 * p))) * 96f / DeviceDpi : 0;
            var rowsTop = commandTop + Theme.LineHeight + (hintHeight > 0 ? hintHeight + 2 : 0) + 4;
            addHeight = rowsTop + (rows * Theme.LineHeight) + p - 4;
            _addCard.Bounds = Dip(0, h - addHeight, half, addHeight);
            var addInner = _addCard.Width - Px(2 * p);
            _addTitle.Bounds = new Rectangle(Px(p), Px(p), addInner, Px(titleHeight));
            var add = _addDevice.GetPreferredSize(Size.Empty);
            _addDevice.Bounds = new Rectangle(Px(p), Px(commandTop) + ((Px(Theme.LineHeight) - add.Height) / 2), add.Width, add.Height);
            _addHint.Bounds = Dip(p, commandTop + Theme.LineHeight, half - (2 * p), hintHeight);
            var y = Px(rowsTop);
            foreach (var (text, remove) in _addedRows.Where(r => r.Text.Visible))
            {
                var removeSize = remove.GetPreferredSize(Size.Empty);
                remove.Bounds = new Rectangle(Px(p) + addInner - removeSize.Width, y + ((Px(Theme.LineHeight) - removeSize.Height) / 2), removeSize.Width, removeSize.Height);
                text.Bounds = new Rectangle(Px(p), y, Math.Max(0, remove.Left - Px(24)), Px(Theme.LineHeight));
                y += Px(Theme.LineHeight);
            }

            addHeight += gap;
        }

        _found.Bounds = Dip(0, 0, half, h - addHeight);
        var inner = _found.Width - Px(2 * p);
        var search = _searchAgain.GetPreferredSize(Size.Empty);
        _searchAgain.Bounds = new Rectangle(_found.Width - Px(p) - search.Width, _found.Height - Px(p) - search.Height, search.Width, search.Height);
        _list.Bounds = new Rectangle(Px(p), Px(t), inner, Math.Max(Px(44), _searchAgain.Top - Px(8) - Px(t)));
        _discoveryStatus.Bounds = new Rectangle(Px(p), _searchAgain.Top, Math.Max(0, _searchAgain.Left - Px(p + 12)), search.Height);

        // Connection: host and password, Test connection, the Power / Voltage / Current tiles and the result line.
        var cw = (w - half - gap) - (2 * p);
        var fieldWidth = (cw - 10) / 2;
        _hostLabel.Bounds = Dip(p, t, fieldWidth, 18);
        _passwordLabel.Bounds = Dip(p + fieldWidth + 10, t, fieldWidth, 18);
        _host.Bounds = Dip(p, t + 20, fieldWidth, Theme.FieldHeight);
        _password.Bounds = Dip(p + fieldWidth + 10, t + 20, fieldWidth, Theme.FieldHeight);
        var testTop = t + 20 + Theme.FieldHeight + 10;
        var test = _test.GetPreferredSize(Size.Empty);
        _test.Bounds = new Rectangle(Px(p), Px(testTop), test.Width, test.Height);
        var tilesTop = testTop + Theme.ButtonHeight + 10;
        var tileWidth = (cw - 16) / 3;
        for (var i = 0; i < 3; i++)
        {
            new[] { _power, _voltage, _current }[i].Bounds = Dip(p + (i * (tileWidth + 8)), tilesTop, tileWidth, 50);
        }

        var resultTop = tilesTop + 50 + 8;
        var resultHeight = Math.Max(Theme.LineHeight, _result.GetPreferredHeight(Px(cw)) * 96f / DeviceDpi);
        _result.Bounds = Dip(p, resultTop, cw, resultHeight);
        _connection.Bounds = Dip(half + gap, 0, w - half - gap, resultTop + Math.Max(resultHeight, Theme.LineHeight) + p);

        _tip.Bounds = new Rectangle(_connection.Left, _connection.Bottom + Px(gap), _connection.Width, _tip.GetPreferredHeight(_connection.Width));
    }

    private void FocusForMode()
    {
        if (IsDisposed || !Visible)
        {
            return;
        }

        switch (_model.SetupMode)
        {
            case ShellySetupMode.Unanswered:
                _yes.Focus();
                break;
            case ShellySetupMode.Guide:
                _done.Focus();
                break;
            default:
                _host.Focus();
                break;
        }
    }

    private void OnModelChanged(object? sender, EventArgs e) => RefreshView();
}
