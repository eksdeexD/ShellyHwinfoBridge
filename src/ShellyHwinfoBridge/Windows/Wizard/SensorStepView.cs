using ShellyHwinfoBridge.Controls;
using ShellyHwinfoBridge.Core.Presentation.Wizard;

namespace ShellyHwinfoBridge.Windows.Wizard;

/// <summary>Step 4 (placeholder until the step view is built).</summary>
internal sealed class SensorStepView : WizardStepView
{
    public SensorStepView(SensorStepModel model, Action reload)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(reload);
        Controls.Add(new TextLabel { Text = "Step 4", Dock = DockStyle.Top, Height = 40 });
    }

    protected override void OnRefreshView()
    {
    }
}
