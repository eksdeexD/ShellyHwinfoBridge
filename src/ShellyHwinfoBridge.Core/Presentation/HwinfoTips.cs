namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>Help texts about HWiNFO shown in more than one place (<see cref="InlineMarkup"/>: bold in <c>**</c>).</summary>
public static class HwinfoTips
{
    /// <summary>
    /// How to keep Other Components visible after the free version's shared memory turns off: the gadget registry
    /// export (shown in wizard step 6 and in Settings › Sensors and formula).
    /// </summary>
    public const string GadgetExport =
        "Tip: to keep seeing Other Components here after Shared Memory turns off: in HWiNFO's sensor **Settings**, open the **HWiNFO Gadget** tab and tick **Enable reporting to Gadget**. Select **Other Components** in the list, then tick **Report value in Gadget** below the list so its Sidebar column shows Yes. Click **OK**.";
}
