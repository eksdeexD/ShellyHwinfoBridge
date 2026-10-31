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

    /// <summary>The licence line.</summary>
    public const string Licence = "MIT licence";

    /// <summary>The Cybenetics attribution line.</summary>
    public const string Attribution = "PSU efficiency data: Cybenetics, used with attribution; each PSU links to its report";

    /// <summary>Returns the name with the version ("Shelly HWiNFO Bridge v0.1.0-beta.5").</summary>
    /// <param name="version">The version text ("v0.1.0-beta.5").</param>
    public static string NameWithVersion(string version) => string.IsNullOrWhiteSpace(version) ? ProductName : ProductName + " " + version.Trim();
}
