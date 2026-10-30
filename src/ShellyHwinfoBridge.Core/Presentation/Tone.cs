using System.Globalization;

namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>Colour role of a piece of UI text or a status dot (mapped to theme colours by the UI, one-to-one by name).</summary>
public enum Tone
{
    /// <summary>Primary text.</summary>
    Ink,

    /// <summary>Secondary text (also the neutral dot).</summary>
    Secondary,

    /// <summary>Disabled text.</summary>
    Disabled,

    /// <summary>The accent colour (live DC value).</summary>
    Accent,

    /// <summary>Success (online dot, success lines).</summary>
    Success,

    /// <summary>Warning.</summary>
    Warning,

    /// <summary>Error (offline dot, error lines).</summary>
    Error,
}

/// <summary>Number formatting for the UI (current culture; the registry uses the invariant culture instead).</summary>
public static class UiFormat
{
    /// <summary>The placeholder for an unknown value.</summary>
    public const string Dash = "—";

    /// <summary>Formats watts with one decimal: "312.4 W".</summary>
    /// <param name="watts">The value.</param>
    /// <param name="culture">The culture.</param>
    public static string Watts(double watts, CultureInfo culture) => watts.ToString("0.0", culture) + " W";

    /// <summary>Formats volts with one decimal: "226.9 V".</summary>
    /// <param name="volts">The value.</param>
    /// <param name="culture">The culture.</param>
    public static string Volts(double volts, CultureInfo culture) => volts.ToString("0.0", culture) + " V";

    /// <summary>Formats a total with three significant digits where sensible: "2.41 kWh", "48.7 kWh", "312 kWh".</summary>
    /// <param name="kwh">The value.</param>
    /// <param name="culture">The culture.</param>
    public static string KwhTotal(double kwh, CultureInfo culture) =>
        Math.Abs(kwh) switch
        {
            < 9.995 => kwh.ToString("0.00", culture),
            < 99.95 => kwh.ToString("0.0", culture),
            _ => kwh.ToString("0", culture),
        } + " kWh";

    /// <summary>Formats a chart value with two decimals (one from 100 kWh): "0.34 kWh".</summary>
    /// <param name="kwh">The value.</param>
    /// <param name="culture">The culture.</param>
    public static string Kwh(double kwh, CultureInfo culture) =>
        (Math.Abs(kwh) < 99.995 ? kwh.ToString("0.00", culture) : kwh.ToString("0.0", culture)) + " kWh";

    /// <summary>Formats a cost with two decimals and the optional currency: "0.77 DKK".</summary>
    /// <param name="pricePerKwh">The price per kWh.</param>
    /// <param name="kwh">The energy.</param>
    /// <param name="currency">The currency text, or <c>null</c>.</param>
    /// <param name="culture">The culture.</param>
    public static string Cost(decimal pricePerKwh, double kwh, string? currency, CultureInfo culture)
    {
        var cost = pricePerKwh * (decimal)Math.Max(0, kwh);
        var text = cost.ToString("0.00", culture);
        return string.IsNullOrWhiteSpace(currency) ? text : text + " " + currency.Trim();
    }
}
