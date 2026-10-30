using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation.Wizard;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>Step 5 (placeholder until the step view is built).</summary>
internal sealed class ApplyStepView : WizardStepView
{
    public ApplyStepView(WizardSession model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Controls.Add(new TextLabel { Text = "Step 5", Dock = DockStyle.Top, Height = 40 });
    }

    protected override void OnRefreshView()
    {
    }
}
