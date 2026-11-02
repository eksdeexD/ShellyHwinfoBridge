using System.Diagnostics;
using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows;

/// <summary>
/// The About window (title "About"), opened by "About" on the main window and "About…" in the tray menu: the product
/// name and version, the feedback sentence with the e-mail and GitHub issues links, the MIT licence and the Cybenetics
/// attribution. Small and fixed-size; closed with the title bar's X or Esc.
/// </summary>
internal sealed class AboutWindow : ThemedForm
{
    /// <summary>The window title.</summary>
    public const string WindowTitle = "About";

    private const int Pad = Theme.WindowPadding;
    private const int WidthDip = 470;
    private const float LineDip = 22;

    // Created in field initializers: the base constructor already lays out.
    private readonly TextLabel _name = new() { Text = AppInfo.ProductName, Role = FontRole.PageTitle };
    private readonly TextLabel _version = new() { Text = VersionText, Tone = UiTone.Secondary };
    private readonly TextLabel _feedbackStart = new() { Text = AppInfo.FeedbackStart, Tone = UiTone.Secondary };
    private readonly TextLink _email = new() { Text = AppInfo.FeedbackEmail, AccessibleName = "Send feedback to " + AppInfo.FeedbackEmail };
    private readonly TextLabel _feedbackMiddle = new() { Text = AppInfo.FeedbackMiddle, Tone = UiTone.Secondary };
    private readonly TextLink _issues = new() { Text = AppInfo.IssuesLinkText, AccessibleName = "Open the issues page on GitHub" };
    private readonly TextLabel _licence = new() { Text = AppInfo.Licence, Tone = UiTone.Secondary, WordWrap = true };
    private readonly TextLabel _attribution = new() { Text = AppInfo.Attribution, Tone = UiTone.Secondary, WordWrap = true };

    public AboutWindow()
    {
        Text = WindowTitle;
        AccessibleName = "About " + AppInfo.ProductName;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        Controls.AddRange([_name, _version, _feedbackStart, _email, _feedbackMiddle, _issues, _licence, _attribution]);
        _email.TabIndex = 0;
        _issues.TabIndex = 1;
        _email.Click += (_, _) => OpenWithShell(this, "mailto:" + AppInfo.FeedbackEmail);
        _issues.Click += (_, _) => OpenWithShell(this, AppInfo.IssuesUrl);

        // Sized to the content (the licence and the credit wrap).
        SetFixedClientSizeDip(new Size(WidthDip, (int)Math.Ceiling(Arrange(apply: false) * 96f / DeviceDpi)));
    }

    /// <summary>Gets the app version ("v0.1.0-beta.4"; build metadata after '+' dropped).</summary>
    internal static string VersionText { get; } = "v" + (typeof(AboutWindow).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
        .FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "0.0.0");

    /// <summary>Opens a link or a mailto: address with the default handler.</summary>
    internal static void OpenWithShell(IWin32Window? owner, string target)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            ThemedDialog.Show(owner, "Could not open the link", target + Environment.NewLine + ex.Message);
        }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    protected override void OnThemeDpiChanged()
    {
        base.OnThemeDpiChanged();
        if (_attribution is not null)
        {
            ClientSize = new Size(this.Px(WidthDip), Arrange(apply: false));
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_attribution is not null && ClientSize.Width > 0)
        {
            Arrange(apply: true);
        }
    }

    /// <summary>Lays the content out top-down; returns the client height in pixels.</summary>
    private int Arrange(bool apply)
    {
        var pad = this.Px(Pad);
        var inner = this.Px(WidthDip) - (2 * pad);
        var y = pad;
        void Place(Control c, int height)
        {
            if (apply)
            {
                c.Bounds = new Rectangle(pad, y, inner, height);
            }

            y += height;
        }

        Place(_name, _name.GetPreferredHeight(inner) + this.Px(2));
        y += this.Px(4);
        Place(_version, this.Px(LineDip));
        y += this.Px(12);

        // The feedback sentence: labels and links left to right, wrapping between them.
        var line = this.Px(LineDip);
        var x = 0;
        foreach (var part in new Control[] { _feedbackStart, _email, _feedbackMiddle, _issues })
        {
            var width = part.GetPreferredSize(Size.Empty).Width;
            if (x > 0 && x + width > inner)
            {
                x = 0;
                y += line;
            }

            if (apply)
            {
                part.Bounds = new Rectangle(pad + x, y, width, line);
            }

            x += width + this.Px(4);
        }

        y += line + this.Px(8);
        Place(_licence, Math.Max(line, _licence.GetPreferredHeight(inner)));
        y += this.Px(4);
        Place(_attribution, Math.Max(line, _attribution.GetPreferredHeight(inner)));
        return y + pad;
    }
}
