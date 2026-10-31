namespace ShellyHwinfoBridge.Core.Startup;

/// <summary>A one-shot maintenance command given on the command line instead of starting the tray app.</summary>
public enum StartupCommand
{
    /// <summary>No command: start the tray app normally.</summary>
    None,

    /// <summary><c>--exit</c>: ask the running instance (if any) to exit cleanly, wait for it, then exit.</summary>
    Exit,

    /// <summary>
    /// <c>--remove-sensors</c>: ask the running instance to exit, delete the HWiNFO <c>Shelly Power</c> sensor group
    /// of the current user, then exit.
    /// </summary>
    RemoveSensors,
}

/// <summary>
/// Command-line switches understood by the app.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>--autostart</c>: started by the Windows Run key at logon. Implies <c>--minimized</c>.</item>
/// <item><c>--minimized</c>: start in the tray only; do not open the main window or wizard on its own.</item>
/// <item><c>--exit</c>: ask a running instance to exit cleanly and do not start the app (see <see cref="StartupCommand.Exit"/>).</item>
/// <item><c>--remove-sensors</c>: exit a running instance and delete the HWiNFO sensors (see <see cref="StartupCommand.RemoveSensors"/>).
/// Wins over <c>--exit</c> when both are given.</item>
/// </list>
/// Switches are case-insensitive and may also be written with a leading <c>/</c>. Unknown arguments are ignored.
/// </remarks>
/// <param name="Autostart">Whether the app was started by the Run key.</param>
/// <param name="Minimized">Whether to start in the tray only.</param>
/// <param name="Command">The maintenance command, or <see cref="StartupCommand.None"/>.</param>
public sealed record StartupArguments(bool Autostart, bool Minimized, StartupCommand Command = StartupCommand.None)
{
    /// <summary>The switch written into the Run key.</summary>
    public const string AutostartSwitch = "--autostart";

    /// <summary>The switch for starting without showing a window.</summary>
    public const string MinimizedSwitch = "--minimized";

    /// <summary>The switch that asks a running instance to exit.</summary>
    public const string ExitSwitch = "--exit";

    /// <summary>The switch that removes the HWiNFO sensors and exits.</summary>
    public const string RemoveSensorsSwitch = "--remove-sensors";

    /// <summary>Parses command-line arguments.</summary>
    /// <param name="args">The arguments (without the executable).</param>
    public static StartupArguments Parse(IEnumerable<string>? args)
    {
        var autostart = false;
        var minimized = false;
        var exit = false;
        var removeSensors = false;
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
            else if (arg.Equals(ExitSwitch, StringComparison.OrdinalIgnoreCase))
            {
                exit = true;
            }
            else if (arg.Equals(RemoveSensorsSwitch, StringComparison.OrdinalIgnoreCase))
            {
                removeSensors = true;
            }
        }

        var command = removeSensors ? StartupCommand.RemoveSensors : exit ? StartupCommand.Exit : StartupCommand.None;
        return new StartupArguments(autostart, minimized || autostart, command);
    }
}
