using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation.Wizard;
using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>
/// Step 6 (after Apply): report the component sensors to the HWiNFO Gadget. A "Steps" card with the numbered how-to and
/// the selected sensors with their live markers (<see cref="GadgetHowToView"/>, refreshed about once per second) on the
/// left, the large <c>hwinfo-gadget.png</c> screenshot on the right. Next is always enabled; the wizard asks first while
/// a sensor is not reported yet (<see cref="WizardSession.NeedsNextConfirmation"/>).
/// </summary>
internal sealed class GadgetStepView : WizardStepView
{
    private const float LeftWidth = 400;

    private readonly CardPanel _card = new() { Title = "Steps" };
    private readonly GadgetHowToView _howTo;
    private readonly ScreenshotFrame _screenshot = new(AppAssets.TryLoadHwinfoGadgetScreenshot(), "Screenshot: HWiNFO Settings, HWiNFO Gadget tab");

    public GadgetStepView(GadgetStepModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _howTo = new GadgetHowToView(model);
        _card.Controls.Add(_howTo);
        Controls.AddRange([_card, _screenshot]);
        RefreshView();
    }

    protected override void OnRefreshView()
    {
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_howTo is null)
        {
            return;
        }

        var w = WidthDip;
        var h = HeightDip;
        const float gap = Theme.Gap;
        const float p = Theme.CardPadding;

        // Left: the steps card, as tall as the list needs (at most the step).
        var inner = Px(LeftWidth - (2 * p));
        var top = _card.ContentBounds.Top;
        var listHeight = _howTo.GetPreferredHeight(inner);
        _card.Bounds = new Rectangle(0, 0, Px(LeftWidth), Math.Min(Px(h), top + listHeight + Px(p)));
        _howTo.Bounds = new Rectangle(Px(p), top, inner, Math.Max(0, _card.Height - top - Px(p)));

        // Right: the screenshot, as large as the rest of the step allows.
        var left = Px(LeftWidth + gap);
        var size = _screenshot.FitInto(new Size(Math.Max(0, Width - left), Height));
        _screenshot.Visible = _screenshot.HasImage;
        _screenshot.Bounds = new Rectangle(new Point(left + ((Width - left - size.Width) / 2), 0), size);
    }
}
