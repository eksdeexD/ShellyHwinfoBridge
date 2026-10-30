using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation.Wizard;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>Step 3 (placeholder until the step view is built).</summary>
internal sealed class PsuStepView : WizardStepView
{
    public PsuStepView(PsuStepModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Controls.Add(new TextLabel { Text = "Step 3", Dock = DockStyle.Top, Height = 40 });
    }

    protected override void OnRefreshView()
    {
    }
}
