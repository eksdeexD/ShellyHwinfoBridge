using ShellyHwinfoBridge.Theming;

namespace ShellyHwinfoBridge.Diagnostics;

/// <summary>Developer switches handled before the single-instance check: <c>--render-screens &lt;dir&gt;</c> and (DEBUG) <c>--gallery</c>.</summary>
internal static class DiagnosticsSwitches
{
    public const string RenderScreensSwitch = "--render-screens";
    public const string GallerySwitch = "--gallery";

    /// <summary>Runs a diagnostics mode when requested.</summary>
    /// <returns><c>true</c> when a mode ran and the process should exit with <paramref name="exitCode"/>.</returns>
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        var render = Array.FindIndex(args, a => a.Equals(RenderScreensSwitch, StringComparison.OrdinalIgnoreCase));
        if (render >= 0)
        {
            var directory = render + 1 < args.Length ? args[render + 1] : Path.Combine(Path.GetTempPath(), "shb-screens");
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetDefaultFont(ThemeFonts.CreateApplicationDefault());
            exitCode = ScreenRenderer.Run(directory);
            return true;
        }

#if DEBUG
        var gallery = Array.FindIndex(args, a => a.Equals(GallerySwitch, StringComparison.OrdinalIgnoreCase));
        if (gallery >= 0)
        {
            ApplicationConfiguration.Initialize();
            Application.SetDefaultFont(ThemeFonts.CreateApplicationDefault());
            var page = gallery + 1 < args.Length && int.TryParse(args[gallery + 1], out var p) ? p : 1;
            Application.Run(new ControlGalleryForm(page));
            return true;
        }
#endif

        return false;
    }
}
