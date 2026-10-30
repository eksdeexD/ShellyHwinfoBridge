using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Shelly.Discovery;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>
/// Step 2: "Found on your network" (mDNS list, "Search again") and "Connection" (host, password, "Test connection",
/// Power/Voltage/Current tiles, result line with "Continue anyway"), plus the fixed-IP tip.
/// </summary>
internal sealed class ShellyStepView : WizardStepView
{
    private readonly ShellyStepModel _model;
    private readonly CardPanel _found = new() { Title = "Found on your network" };
    private readonly SelectableList _list = new() { RowHeightDip = 56, AccessibleName = "Shelly plugs found" };
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
    private IReadOnlyList<DiscoveredShelly>? _shownDevices;

    public ShellyStepView(ShellyStepModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _found.Controls.AddRange([_list, _discoveryStatus, _searchAgain]);
        _connection.Controls.AddRange([_hostLabel, _passwordLabel, _host, _password, _test, _power, _voltage, _current, _result]);
        Controls.AddRange([_found, _connection, _tip]);

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
        _host.Focus();
        if (!_model.HasDiscovered && !_model.IsDiscovering)
        {
            await _model.DiscoverAsync();
        }
    }

    protected override void OnRefreshView()
    {
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
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var w = WidthDip;
        var h = HeightDip;
        const float gap = 16;
        var half = (w - gap) / 2;

        _found.Bounds = Dip(0, 0, half, h);
        var inner = _found.Width - Px(40);
        _list.Bounds = new Rectangle(Px(20), Px(60), inner, _found.Height - Px(60 + 20 + 40 + 16));
        _searchAgain.Bounds = new Rectangle(_found.Width - Px(20 + 150), _found.Height - Px(20 + 40), Px(150), Px(40));
        _discoveryStatus.Bounds = new Rectangle(Px(20), _searchAgain.Top, _searchAgain.Left - Px(36), Px(40));

        _connection.Bounds = Dip(half + gap, 0, w - half - gap, 336);
        var cw = _connection.Width - Px(40);
        var fieldWidth = (cw - Px(16)) / 2;
        _hostLabel.Bounds = new Rectangle(Px(20), Px(56), fieldWidth, Px(22));
        _passwordLabel.Bounds = new Rectangle(Px(20) + fieldWidth + Px(16), Px(56), fieldWidth, Px(22));
        _host.Bounds = new Rectangle(Px(20), Px(80), fieldWidth, Px(40));
        _password.Bounds = new Rectangle(_passwordLabel.Left, Px(80), fieldWidth, Px(40));
        _test.Bounds = new Rectangle(Px(20), Px(136), Px(170), Px(40));
        var tileWidth = (cw - Px(24)) / 3;
        var x = Px(20);
        foreach (var tile in new[] { _power, _voltage, _current })
        {
            tile.Bounds = new Rectangle(x, Px(192), tileWidth, Px(72));
            x += tileWidth + Px(12);
        }

        _result.Bounds = new Rectangle(Px(20), Px(278), cw, Math.Max(Px(28), _result.GetPreferredHeight(cw)));

        _tip.Bounds = new Rectangle(_connection.Left, _connection.Bottom + Px(gap), _connection.Width, _tip.GetPreferredHeight(_connection.Width));
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
