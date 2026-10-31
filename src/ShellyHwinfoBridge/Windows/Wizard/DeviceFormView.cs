using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Core.Shelly.Discovery;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>
/// The "Add a Shelly for your monitor or another device" form (<see cref="DeviceFormModel"/>), shared by wizard step 2
/// (inline in place of the step content, opened by "Add device" on the step's add-device card, with its own Add device /
/// Cancel buttons) and Manage Shellys (Add / Edit, hosted by a
/// <see cref="StepDialog"/>). Left: "Found on your network" (mDNS list, "Search again"), as in step 2. Right: Label and
/// "Include in totals", Host and Password, "Test connection" with its result, the Power / Voltage / Current tiles and
/// one message line (label error, or the rename warning when an existing device's label changed). Thin: every
/// decision lives in the model.
/// </summary>
internal sealed class DeviceFormView : WizardStepView
{
    private readonly DeviceFormModel _form;
    private readonly bool _inline;

    private readonly CardPanel _found = new() { Title = "Found on your network" };
    private readonly SelectableList _list = new() { RowHeightDip = 56, AccessibleName = "Shelly plugs found" };
    private readonly TextLabel _discoveryStatus = new() { Tone = UiTone.Secondary };
    private readonly PillButton _searchAgain = new() { Text = "Search again" };

    private readonly CardPanel _device = new();
    private readonly TextLabel _labelCaption = new() { Text = "Label", Tone = UiTone.Secondary };
    private readonly ThemedTextBox _label = new() { MaxLength = Core.Settings.DeviceLabelRules.MaxLength, AccessibleName = "Label" };
    private readonly ThemedCheckBox _include = new() { Text = "Include in totals" };
    private readonly TextLabel _hostLabel = new() { Text = "Host", Tone = UiTone.Secondary };
    private readonly TextLabel _passwordLabel = new() { Text = "Password", Tone = UiTone.Secondary };
    private readonly ThemedTextBox _host = new() { Placeholder = "IP address or host name", AccessibleName = "Host" };
    private readonly ThemedTextBox _password = new() { Placeholder = "leave empty if not set", UseSystemPasswordChar = true, AccessibleName = "Password" };
    private readonly PillButton _test = new() { Text = "Test connection" };
    private readonly InfoLine _result = new();
    private readonly ValueTile _power = new() { Caption = "Power" };
    private readonly ValueTile _voltage = new() { Caption = "Voltage" };
    private readonly ValueTile _current = new() { Caption = "Current" };
    private readonly InfoLine _message = new();
    private readonly PillButton _save = new() { Variant = PillVariant.Primary };
    private readonly PillButton _cancel = new() { Text = "Cancel" };
    private IReadOnlyList<DiscoveredShelly>? _shownDevices;

    /// <summary>Creates the form view.</summary>
    /// <param name="form">The form model.</param>
    /// <param name="inline">
    /// <c>true</c> in wizard step 2: the card has its own Add device / Cancel buttons
    /// (<see cref="SaveRequested"/>, <see cref="CancelRequested"/>); <c>false</c> in a dialog that has its own buttons.
    /// </param>
    public DeviceFormView(DeviceFormModel form, bool inline)
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));
        _inline = inline;
        AccessibleName = form.Title;
        _device.Title = inline && form.Mode == DeviceFormMode.Add ? "New device" : "Device";
        _device.AccessibleName = _device.Title;
        _save.Text = form.Mode == DeviceFormMode.Add ? "Add device" : "Save";
        _save.Visible = inline;
        _cancel.Visible = inline;

        _found.Controls.AddRange([_list, _discoveryStatus, _searchAgain]);
        _device.Controls.AddRange([_labelCaption, _label, _include, _hostLabel, _passwordLabel, _host, _password, _test, _result, _power, _voltage, _current, _message, _save, _cancel]);
        for (var i = 0; i < _device.Controls.Count; i++)
        {
            _device.Controls[i].TabIndex = i;
        }

        // The device card first: Label is the first field.
        _device.TabIndex = 0;
        _found.TabIndex = 1;
        Controls.AddRange([_device, _found]);

        _label.ReadOnly = !form.CanEditLabel;
        _include.Visible = form.ShowIncludeInTotals;

        var connection = form.Connection;
        _label.TextChanged += (_, _) =>
        {
            if (!IsRefreshing && _form.CanEditLabel)
            {
                _form.Label = _label.Text;
            }
        };
        _include.CheckedChanged += (_, _) =>
        {
            if (!IsRefreshing)
            {
                _form.IncludeInTotals = _include.Checked;
            }
        };
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (!IsRefreshing && _list.SelectedIndex >= 0 && _list.SelectedIndex < connection.Devices.Count)
            {
                connection.SelectDevice(connection.Devices[_list.SelectedIndex]);
            }
        };
        _host.TextChanged += (_, _) =>
        {
            if (!IsRefreshing)
            {
                connection.Host = _host.Text;
            }
        };
        _password.TextChanged += (_, _) =>
        {
            if (!IsRefreshing)
            {
                connection.Password = _password.Text.Length == 0 ? null : _password.Text;
            }
        };
        _searchAgain.Click += async (_, _) => await connection.DiscoverAsync();
        _test.Click += async (_, _) => await connection.TestAsync();
        // The owner may dispose this view in response: raise both after the click has been handled.
        _save.Click += (_, _) => Defer(() => SaveRequested?.Invoke(this, EventArgs.Empty));
        _cancel.Click += (_, _) => Defer(() => CancelRequested?.Invoke(this, EventArgs.Empty));
        _form.Changed += OnModelChanged;
        RefreshView();
    }

    /// <summary>Raised when the inline "Add device" / "Save" button is clicked.</summary>
    public event EventHandler? SaveRequested;

    /// <summary>Raised when the inline "Cancel" button is clicked.</summary>
    public event EventHandler? CancelRequested;

    /// <summary>Gets the form model.</summary>
    public DeviceFormModel Form => _form;

    /// <summary>Returns why the form cannot be saved yet, or <c>null</c> when it can.</summary>
    public string? ValidationMessage =>
        _form.IsValid ? null : _form.LabelError ?? _form.Connection.HostError ?? "Enter the plug's IP address or host name, or pick it from the list.";

    public override async void OnStepShown()
    {
        if (_form.CanEditLabel)
        {
            _label.Focus();
            _label.InnerTextBox.SelectAll();
        }
        else
        {
            _host.Focus();
        }

        if (_form.Connection.ShouldDiscoverOnShow)
        {
            await _form.Connection.DiscoverAsync();
        }
    }

    protected override void OnRefreshView()
    {
        var connection = _form.Connection;
        if (!ReferenceEquals(_shownDevices, connection.Devices))
        {
            _shownDevices = connection.Devices;
            _list.SetRows(connection.Devices.Select(d => new SelectableRow(
                d.DisplayName,
                string.Join(" · ", new[] { d.DisplayModel, d.Host }.Where(s => !string.IsNullOrWhiteSpace(s))))));
        }

        _list.EmptyText = connection.DeviceListEmptyText;
        _list.SelectedIndex = connection.SelectedDeviceIndex;
        _list.Invalidate();
        _discoveryStatus.Text = connection.DiscoveryStatusText;
        _searchAgain.Enabled = !connection.IsDiscovering;

        SetTextIfChanged(_label, _form.Label);
        _label.HasError = _form.LabelError is not null;
        _include.Checked = _form.IncludeInTotals;
        SetTextIfChanged(_host, connection.Host);
        SetTextIfChanged(_password, connection.Password);
        _host.HasError = connection.HostError is not null;
        _test.Enabled = connection.TestState != ShellyTestState.Testing && connection.IsHostValid;
        _power.Value = connection.PowerText;
        _voltage.Value = connection.VoltageText;
        _current.Value = connection.CurrentText;

        var line = connection.HostError is { } hostError ? new WizardStatusLine(Tone.Error, hostError) : connection.ResultLine;
        _result.Visible = line is not null;
        if (line is not null)
        {
            _result.Text = line.Text;
            _result.Tone = HwinfoStepView.ToInfoTone(line.Tone);
        }

        // One message line: the label error first, else the rename warning.
        if (_form.LabelError is { } labelError)
        {
            _message.Text = labelError;
            _message.Tone = InfoTone.Error;
            _message.Visible = true;
        }
        else if (_form.ShowRenameWarning)
        {
            _message.Text = DeviceFormModel.RenameWarningText;
            _message.Tone = InfoTone.Warning;
            _message.Visible = true;
        }
        else
        {
            _message.Visible = false;
        }

        _save.Enabled = _form.IsValid;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var w = WidthDip;
        var h = HeightDip;
        const float gap = 16;
        var half = (w - gap) / 2;

        // Left: the discovery list, as in step 2.
        _found.Bounds = Dip(0, 0, half, h);
        var inner = _found.Width - Px(40);
        _list.Bounds = new Rectangle(Px(20), Px(60), inner, Math.Max(Px(56), _found.Height - Px(60 + 20 + 40 + 16)));
        _searchAgain.Bounds = new Rectangle(_found.Width - Px(20 + 150), _found.Height - Px(20 + 40), Px(150), Px(40));
        _discoveryStatus.Bounds = new Rectangle(Px(20), _searchAgain.Top, Math.Max(0, _searchAgain.Left - Px(36)), Px(40));

        // Right: label and include, host and password, test and result, tiles, message (and the inline buttons).
        _device.Bounds = Dip(half + gap, 0, w - half - gap, h);
        var cw = _device.Width - Px(40);
        var fieldWidth = (cw - Px(16)) / 2;
        var rightLeft = Px(20) + fieldWidth + Px(16);
        _labelCaption.Bounds = new Rectangle(Px(20), Px(52), fieldWidth, Px(22));
        _label.Bounds = new Rectangle(Px(20), Px(76), fieldWidth, Px(40));
        _include.Bounds = new Rectangle(rightLeft, Px(82), Math.Min(fieldWidth, _include.GetPreferredSize(Size.Empty).Width), Px(28));
        _hostLabel.Bounds = new Rectangle(Px(20), Px(124), fieldWidth, Px(22));
        _passwordLabel.Bounds = new Rectangle(rightLeft, Px(124), fieldWidth, Px(22));
        _host.Bounds = new Rectangle(Px(20), Px(148), fieldWidth, Px(40));
        _password.Bounds = new Rectangle(rightLeft, Px(148), fieldWidth, Px(40));
        _test.Bounds = new Rectangle(Px(20), Px(200), Px(170), Px(40));
        var resultLeft = _test.Right + Px(16);
        var resultWidth = Math.Max(0, Px(20) + cw - resultLeft);
        var resultHeight = Math.Min(Px(48), Math.Max(Px(28), _result.GetPreferredHeight(resultWidth)));
        _result.Bounds = new Rectangle(resultLeft, _test.Top + ((_test.Height - resultHeight) / 2), resultWidth, resultHeight);

        var tileWidth = (cw - Px(24)) / 3;
        var x = Px(20);
        foreach (var tile in new[] { _power, _voltage, _current })
        {
            tile.Bounds = new Rectangle(x, Px(252), tileWidth, Px(56));
            x += tileWidth + Px(12);
        }

        var buttonsTop = _device.Height - Px(20 + 40);
        var messageTop = Px(318);
        var messageBottom = _inline ? buttonsTop - Px(8) : _device.Height - Px(16);
        _message.Bounds = new Rectangle(Px(20), messageTop, cw, Math.Max(Px(28), Math.Min(messageBottom - messageTop, _message.GetPreferredHeight(cw))));

        var saveWidth = Math.Max(Px(130), _save.GetPreferredSize(Size.Empty).Width);
        var cancelWidth = Math.Max(Px(108), _cancel.GetPreferredSize(Size.Empty).Width);
        _save.Bounds = new Rectangle(Px(20) + cw - saveWidth, buttonsTop, saveWidth, Px(40));
        _cancel.Bounds = new Rectangle(_save.Left - Px(12) - cancelWidth, buttonsTop, cancelWidth, Px(40));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _form.Changed -= OnModelChanged;
        }

        base.Dispose(disposing);
    }

    /// <summary>Runs <paramref name="action"/> once the current message is handled (on the window, which outlives the view).</summary>
    private void Defer(Action action)
    {
        if (FindForm() is { IsHandleCreated: true, IsDisposed: false } form)
        {
            form.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed)
        {
            RefreshView();
        }
    }
}
