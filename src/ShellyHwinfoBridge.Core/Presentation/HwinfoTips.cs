namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>Help texts about HWiNFO shown in more than one place (<see cref="InlineMarkup"/>: bold in <c>**</c>).</summary>
public static class HwinfoTips
{
    /// <summary>The title of the "How to enable?" help dialog (the same heading as wizard step 5).</summary>
    public const string GadgetExportTitle = Wizard.GadgetStepModel.Heading;

    /// <summary>The one-line explainer of Settings › Sensors, followed by the "How to enable?" text action.</summary>
    public const string GadgetExplainer = "Without Shared Memory, Other Components needs the HWiNFO Gadget export.";

    /// <summary>The screenshot shown with the gadget how-to.</summary>
    public const string GadgetScreenshot = "hwinfo-gadget.png";

    /// <summary>
    /// The gadget how-to without sensor names (used when no sensors are selected): Other Components is computed by the
    /// app from the selected component sensors, so HWiNFO must report those sensors to its gadget export when shared
    /// memory is off. Shown in the "How to enable?" dialog, wizard step 5 and Settings › Sensors.
    /// </summary>
    public const string GadgetExport =
        "To compute Other Components without Shared Memory, HWiNFO must report your selected component sensors: in HWiNFO's sensor **Settings**, open the **HWiNFO Gadget** tab and tick **Enable reporting to Gadget**. Then select each of your selected sensors in the list and tick **Report value in Gadget** below the list so its Sidebar column shows Yes. Click **OK**. The screenshot shows this for one sensor; repeat the step for each of your selected sensors.";

    /// <summary>Builds the gadget how-to with the user's selected sensor names inserted.</summary>
    /// <param name="sensorNames">The selected component sensor names (blank and duplicate names are skipped).</param>
    public static string GadgetExportFor(IEnumerable<string> sensorNames)
    {
        ArgumentNullException.ThrowIfNull(sensorNames);
        var names = sensorNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0)
        {
            return GadgetExport;
        }

        var list = string.Join(", ", names.Select(n => "**" + n + "**"));
        return "To compute Other Components without Shared Memory, HWiNFO must report your selected component sensors: in HWiNFO's sensor **Settings**, open the **HWiNFO Gadget** tab and tick **Enable reporting to Gadget**. Then select each of these sensors in the list: "
            + list
            + ", and tick **Report value in Gadget** below the list so its Sidebar column shows Yes. Click **OK**. The screenshot shows this for one sensor; repeat the step for every sensor listed above.";
    }
}
