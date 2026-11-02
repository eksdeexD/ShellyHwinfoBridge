namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>
/// The product name and the About content (SPEC "UI conventions": the name shown to users is "Shelly HWiNFO Bridge";
/// the executable, the AppData folder, the mutex and the HWiNFO group keep their technical names).
/// </summary>
public static class AppInfo
{
    /// <summary>The product name shown to users (window titles, About, tray tooltip).</summary>
    public const string ProductName = "Shelly HWiNFO Bridge";

    /// <summary>The project's issues page.</summary>
    public const string IssuesUrl = "https://github.com/eksdeexD/ShellyHwinfoBridge/issues";

    /// <summary>The feedback address.</summary>
    public const string FeedbackEmail = "zapador@zapador.net";

    /// <summary>The start of the feedback sentence, before the e-mail link.</summary>
    public const string FeedbackStart = "Feedback and feature requests are welcome at";

    /// <summary>The words between the e-mail link and the issues link.</summary>
    public const string FeedbackMiddle = "or as";

    /// <summary>The text of the issues link.</summary>
    public const string IssuesLinkText = "issues on GitHub";

    /// <summary>The licence line (the PSU efficiency data is under its own terms, see PSU-DATA-NOTICE.md).</summary>
    public const string Licence = "MIT licence (source code). The PSU efficiency data is not covered by it, see PSU-DATA-NOTICE.md.";

    /// <summary>
    /// The Cybenetics credit (their condition: exactly "Test results provided by Cybenetics", that the results are from
    /// the sample they tested, and per PSU the report link, the input voltage and the report date).
    /// </summary>
    public const string Attribution =
        "Test results provided by Cybenetics. The results are from the sample Cybenetics tested; each PSU shows the report link, input voltage and report date.";

    /// <summary>Returns the name with the version ("Shelly HWiNFO Bridge v0.1.0-beta.5").</summary>
    /// <param name="version">The version text ("v0.1.0-beta.5").</param>
    public static string NameWithVersion(string version) => string.IsNullOrWhiteSpace(version) ? ProductName : ProductName + " " + version.Trim();
}
