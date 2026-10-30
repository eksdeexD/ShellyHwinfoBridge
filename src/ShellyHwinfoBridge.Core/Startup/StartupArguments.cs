namespace ShellyHwinfoBridge.Core.Startup;

/// <summary>
/// Command-line switches understood by the app.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>--autostart</c>: started by the Windows Run key at logon. Implies <c>--minimized</c>.</item>
/// <item><c>--minimized</c>: start in the tray only; do not open the settings window or wizard on its own.</item>
/// </list>
/// Switches are case-insensitive and may also be written with a leading <c>/</c>. Unknown arguments are ignored.
/// </remarks>
public sealed record StartupArguments(bool Autostart, bool Minimized)
{
    /// <summary>The switch written into the Run key.</summary>
    public const string AutostartSwitch = "--autostart";

    /// <summary>The switch for starting without showing a window.</summary>
    public const string MinimizedSwitch = "--minimized";

    /// <summary>Parses command-line arguments.</summary>
    /// <param name="args">The arguments (without the executable).</param>
    public static StartupArguments Parse(IEnumerable<string>? args)
    {
        var autostart = false;
        var minimized = false;
        foreach (var raw in args ?? [])
        {
            var arg = (raw ?? string.Empty).Trim();
            if (arg.StartsWith('/'))
            {
                arg = "--" + arg[1..];
            }

            if (arg.Equals(AutostartSwitch, StringComparison.OrdinalIgnoreCase))
            {
                autostart = true;
            }
            else if (arg.Equals(MinimizedSwitch, StringComparison.OrdinalIgnoreCase))
            {
                minimized = true;
            }
        }

        return new StartupArguments(autostart, minimized || autostart);
    }
}
