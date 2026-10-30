using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// A small themed message box: title, wrapped message and one or two pill buttons (Primary OK, Secondary Cancel).
/// Use instead of <see cref="MessageBox"/> for confirmations and notices.
/// </summary>
internal sealed class ThemedDialog : ThemedForm
{
    private const int WidthDip = 460;
    private const int PaddingDip = 24;

    private readonly string _title;
    private readonly string _message;
    private readonly PillButton _ok;
    private readonly PillButton? _cancel;

    private ThemedDialog(string title, string message, string okText, string? cancelText)
    {
        _title = title;
        _message = message;
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        _ok = new PillButton { Text = okText, Variant = PillVariant.Primary, DialogResult = DialogResult.OK };
        Controls.Add(_ok);
        AcceptButton = _ok;
        if (cancelText is not null)
        {
            _cancel = new PillButton { Text = cancelText, DialogResult = DialogResult.Cancel };
            Controls.Add(_cancel);
            CancelButton = _cancel;
        }
        else
        {
            CancelButton = _ok;
        }
    }

    /// <summary>Shows a dialog modally and returns OK or Cancel.</summary>
    /// <param name="owner">The owner window, or <c>null</c> (then the dialog shows in the taskbar and on top).</param>
    /// <param name="title">The title (also the window caption).</param>
    /// <param name="message">The message (wraps).</param>
    /// <param name="okText">The primary button text.</param>
    /// <param name="cancelText">The secondary button text, or <c>null</c> for a single button.</param>
    public static DialogResult Show(IWin32Window? owner, string title, string message, string okText = "OK", string? cancelText = null)
    {
        using var dialog = new ThemedDialog(title, message, okText, cancelText);
        if (owner is null)
        {
            dialog.ShowInTaskbar = true;
            dialog.TopMost = true;
        }
        else
        {
            dialog.StartPosition = FormStartPosition.CenterParent;
        }

        return dialog.ShowDialog(owner);
    }

    protected override void OnThemeDpiChanged()
    {
        base.OnThemeDpiChanged();
        var height = PaddingDip + TitleHeightDip() + 12 + MessageHeightDip() + 24 + 40 + PaddingDip;
        ClientSize = new Size(this.Px(WidthDip), this.Px(height));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var p = this.Px(PaddingDip);
        var buttonHeight = this.Px(40);
        var y = ClientSize.Height - p - buttonHeight;
        var okWidth = Math.Max(this.Px(96), _ok.GetPreferredSize(Size.Empty).Width);
        _ok.Bounds = new Rectangle(ClientSize.Width - p - okWidth, y, okWidth, buttonHeight);
        if (_cancel is not null)
        {
            var cancelWidth = Math.Max(this.Px(96), _cancel.GetPreferredSize(Size.Empty).Width);
            _cancel.Bounds = new Rectangle(_ok.Left - this.Px(12) - cancelWidth, y, cancelWidth, buttonHeight);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var p = this.Px(PaddingDip);
        var width = ClientSize.Width - (2 * p);
        var titleHeight = this.Px(TitleHeightDip());
        Gfx.Text(e.Graphics, _title, Fonts.CardTitle, new Rectangle(p, p, width, titleHeight), Theme.Ink);
        var top = p + titleHeight + this.Px(12);
        TextRenderer.DrawText(
            e.Graphics,
            _message,
            Fonts.Body,
            new Rectangle(p, top, width, this.Px(MessageHeightDip()) + this.Px(4)),
            Theme.InkSecondary,
            TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    private int TitleHeightDip() => (int)Math.Ceiling(Gfx.Measure(_title, Fonts.CardTitle).Height * 96f / DeviceDpi);

    private int MessageHeightDip()
    {
        var width = this.Px(WidthDip - (2 * PaddingDip));
        return (int)Math.Ceiling(Gfx.MeasureWrapped(_message, Fonts.Body, width).Height * 96f / DeviceDpi);
    }
}
