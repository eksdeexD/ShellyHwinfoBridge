using ShellyHwinfoBridge.Core.Presentation;

namespace ShellyHwinfoBridge.Core.Tests.Presentation;

public class InlineMarkupTests
{
    [Fact]
    public void SplitsBoldRuns()
    {
        Assert.Equal(
            [new TextRun("On the ", false), new TextRun("General", true), new TextRun(" tab, click ", false), new TextRun("OK", true), new TextRun(".", false)],
            InlineMarkup.Parse("On the **General** tab, click **OK**."));
    }

    [Fact]
    public void PlainTextIsOneRun() => Assert.Equal([new TextRun("Restart HWiNFO.", false)], InlineMarkup.Parse("Restart HWiNFO."));

    [Fact]
    public void UnmatchedMarkerStaysText() => Assert.Equal([new TextRun("a ** b", false)], InlineMarkup.Parse("a ** b"));

    [Fact]
    public void EmptyInputGivesNoRuns()
    {
        Assert.Empty(InlineMarkup.Parse(null));
        Assert.Empty(InlineMarkup.Parse(string.Empty));
        Assert.Empty(InlineMarkup.Parse("****"));
    }

    [Fact]
    public void StripRemovesTheMarkers()
    {
        Assert.Equal("allow TCP port 80 from this PC", InlineMarkup.Strip("allow **TCP port 80** from this PC"));
        Assert.DoesNotContain("**", InlineMarkup.Strip(HwinfoTips.GadgetExport));
        Assert.Contains("Report value in Gadget", InlineMarkup.Strip(HwinfoTips.GadgetExport));
    }
}
