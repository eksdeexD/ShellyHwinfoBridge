using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>
/// Base of the wizard step views (also hosted by the Settings step dialog): a transparent panel the size of the step
/// host (<see cref="StepHeightDip"/> DIP high) that lays its children out in DIP and re-reads its model in <see cref="RefreshView"/>.
/// Views are thin: every decision lives in the Core step model.
/// </summary>
internal abstract class WizardStepView : ThemedPanel
{
    private bool _refreshing;

    protected WizardStepView()
    {
        AccessibleRole = AccessibleRole.Pane;
    }

    /// <summary>Gets the fonts for the current DPI.</summary>
    protected ThemeFontSet Fonts => ThemeFonts.For(DeviceDpi);

    /// <summary>Gets whether <see cref="RefreshView"/> is running (ignore control events raised by it).</summary>
    protected bool IsRefreshing => _refreshing;

    /// <summary>Re-reads the model into the controls; control events raised meanwhile see <see cref="IsRefreshing"/>.</summary>
    public void RefreshView()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            OnRefreshView();
        }
        finally
        {
            _refreshing = false;
        }

        PerformLayout();
        Invalidate(true);
    }

    /// <summary>The height of the wizard's step host in DIP (the wizard window is sized for it).</summary>
    public const float StepHeightDip = 452;

    /// <summary>Returns the height in DIP the view needs at <paramref name="widthDip"/> (a step dialog sizes itself to it).</summary>
    public virtual float PreferredHeightDip(float widthDip) => StepHeightDip;

    /// <summary>Called when the step becomes visible.</summary>
    public virtual void OnStepShown()
    {
    }

    /// <summary>Called when the step is left or the window closes.</summary>
    public virtual void OnStepHidden()
    {
    }

    /// <summary>Copies the model state into the controls.</summary>
    protected abstract void OnRefreshView();

    /// <summary>Converts a DIP rectangle to device pixels.</summary>
    protected Rectangle Dip(float x, float y, float width, float height) =>
        new(this.Px(x), this.Px(y), this.Px(width), this.Px(height));

    /// <summary>Gets the view width in DIP.</summary>
    protected float WidthDip => Width * 96f / DeviceDpi;

    /// <summary>Gets the view height in DIP.</summary>
    protected float HeightDip => Height * 96f / DeviceDpi;

    /// <summary>Sets a text box's text from the model without raising the view's handlers (keeps the caret when equal).</summary>
    protected static void SetTextIfChanged(ThemedTextBox box, string? text)
    {
        if (box.Text != (text ?? string.Empty))
        {
            box.SetText(text);
        }
    }

    /// <summary>Converts a DIP length to device pixels for a child of this view.</summary>
    protected int Px(float dip) => Theme.Scale(dip, DeviceDpi);

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        PerformLayout();
        Invalidate(true);
    }
}
