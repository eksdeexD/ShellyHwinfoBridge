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
/// one message line (label error, or the rename warning when an existing device's label changed, with "Show me how"
/// opening <see cref="RenameHelpDialog"/>). Thin: every
/// decision lives in the model.
/// </summary>
internal sealed class DeviceFormView : WizardStepView
{
    private readonly DeviceFormModel _form;
    private readonly bool _inline;

    private readonly CardPanel _found = new() { Title = "Found on your network" };
    private readonly SelectableList _list = new() { RowHeightDip = 44, AccessibleName = "Shelly plugs found" };
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
    private readonly TextLink _showHow = new() { Text = HwinfoRenameHelp.ShowMeHowText, Semibold = true, Visible = false, AccessibleName = "Show me how to update the names in HWiNFO" };
    private readonly PillButton _save = new() { Variant = PillVariant.Primary };
    private readonly PillButton _cancel = new() { Text = "Cancel" };
    private IReadOnlyList<DiscoveredShelly>? _shownDevices;
    private bool _showHowShown;

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
        _device.Controls.AddRange([_labelCaption, _label, _include, _hostLabel, _passwordLabel, _host, _password, _test, _result, _power, _voltage, _current, _message, _showHow, _save, _cancel]);
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
        _showHow.Click += (_, _) => RenameHelpDialog.Show(FindForm(), _form.RenameHelp);
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

        // "Show me how" opens the HWiNFO rename help while the rename warning is shown.
        var showHow = _form.LabelError is null && _form.ShowRenameWarning;
        if (_showHowShown != showHow)
        {
            _showHowShown = showHow;
            _showHow.Visible = showHow;
            PerformLayout();
        }

        _save.Enabled = _form.IsValid;
    }

    /// <summary>The device card's fields, tiles and the longest message (the rename warning with "Show me how").</summary>
    public override float PreferredHeightDip(float widthDip)
    {
        const float p = Theme.CardPadding;
        var title = Gfx.Measure("Ag", Fonts.CardTitle).Height * 96f / DeviceDpi;
        var t = p + title + Theme.TitleGap;
        var cw = ((widthDip - Theme.Gap) / 2) - (2 * p);
        // The warning line as InfoLine wraps it (text right of its icon).
        var message = (Gfx.MeasureWrapped(DeviceFormModel.RenameWarningText, Fonts.Body, Px(cw - 23)).Height * 96f / DeviceDpi) + 2;
        var showHow = _showHow.GetPreferredSize(Size.Empty).Height * 96f / DeviceDpi;
        var buttons = _inline ? Theme.ButtonHeight + 6 : 0;
        return t + (20 + Theme.FieldHeight + 8) + (20 + Theme.FieldHeight + 10) + (Theme.ButtonHeight + 10) + (46 + 8) + message + 4 + showHow + buttons + p;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var w = WidthDip;
        var h = HeightDip;
        const float gap = Theme.Gap;
        const float p = Theme.CardPadding;
        var half = (w - gap) / 2;
        var t = _found.ContentBounds.Top * 96f / DeviceDpi;

        // Left: the discovery list, as in step 2.
        _found.Bounds = Dip(0, 0, half, h);
        var inner = _found.Width - Px(2 * p);
        var search = _searchAgain.GetPreferredSize(Size.Empty);
        _searchAgain.Bounds = new Rectangle(_found.Width - Px(p) - search.Width, _found.Height - Px(p) - search.Height, search.Width, search.Height);
        _list.Bounds = new Rectangle(Px(p), Px(t), inner, Math.Max(Px(44), _searchAgain.Top - Px(8) - Px(t)));
        _discoveryStatus.Bounds = new Rectangle(Px(p), _searchAgain.Top, Math.Max(0, _searchAgain.Left - Px(p + 12)), search.Height);

        // Right: label and include, host and password, test and result, tiles, message (and the inline buttons).
        _device.Bounds = Dip(half + gap, 0, w - half - gap, h);
        var cw = (w - half - gap) - (2 * p);
        var fieldWidth = (cw - 10) / 2;
        var right = p + fieldWidth + 10;
        _labelCaption.Bounds = Dip(p, t, fieldWidth, 18);
        _label.Bounds = Dip(p, t + 20, fieldWidth, Theme.FieldHeight);
        var includeWidth = Math.Min(fieldWidth, _include.GetPreferredSize(Size.Empty).Width * 96f / DeviceDpi);
        _include.Bounds = Dip(right, t + 20 + ((Theme.FieldHeight - Theme.LineHeight) / 2), includeWidth, Theme.LineHeight);
        var hostTop = t + 20 + Theme.FieldHeight + 8;
        _hostLabel.Bounds = Dip(p, hostTop, fieldWidth, 18);
        _passwordLabel.Bounds = Dip(right, hostTop, fieldWidth, 18);
        _host.Bounds = Dip(p, hostTop + 20, fieldWidth, Theme.FieldHeight);
        _password.Bounds = Dip(right, hostTop + 20, fieldWidth, Theme.FieldHeight);
        var testTop = hostTop + 20 + Theme.FieldHeight + 10;
        var test = _test.GetPreferredSize(Size.Empty);
        _test.Bounds = new Rectangle(Px(p), Px(testTop), test.Width, test.Height);
        var resultLeft = _test.Right + Px(10);
        var resultWidth = Math.Max(0, Px(p + cw) - resultLeft);
        var resultHeight = Math.Min(Px(40), Math.Max(Px(Theme.LineHeight), _result.GetPreferredHeight(resultWidth)));
        _result.Bounds = new Rectangle(resultLeft, _test.Top + ((_test.Height - resultHeight) / 2), resultWidth, resultHeight);

        var tilesTop = testTop + Theme.ButtonHeight + 10;
        var tileWidth = (cw - 16) / 3;
        for (var i = 0; i < 3; i++)
        {
            new[] { _power, _voltage, _current }[i].Bounds = Dip(p + (i * (tileWidth + 8)), tilesTop, tileWidth, 46);
        }

        var buttonsTop = _device.Height - Px(p) - Px(Theme.ButtonHeight);
        var messageTop = Px(tilesTop + 46 + 8);
        var messageBottom = _inline ? buttonsTop - Px(6) : _device.Height - Px(10);
        var showHow = _showHow.GetPreferredSize(Size.Empty);
        var showHowHeight = _showHowShown ? showHow.Height + Px(4) : 0;
        var messageWidth = Px(cw);
        _message.Bounds = new Rectangle(Px(p), messageTop, messageWidth, Math.Max(Px(Theme.LineHeight), Math.Min(messageBottom - messageTop - showHowHeight, _message.GetPreferredHeight(messageWidth))));
        _showHow.Bounds = new Rectangle(Px(p + 23), _message.Bottom + Px(4), showHow.Width, showHow.Height);

        var saveWidth = Math.Max(Px(84), _save.GetPreferredSize(Size.Empty).Width);
        var cancelWidth = Math.Max(Px(84), _cancel.GetPreferredSize(Size.Empty).Width);
        _save.Bounds = new Rectangle(Px(p) + Px(cw) - saveWidth, buttonsTop, saveWidth, Px(Theme.ButtonHeight));
        _cancel.Bounds = new Rectangle(_save.Left - Px(8) - cancelWidth, buttonsTop, cancelWidth, Px(Theme.ButtonHeight));
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
