using System.Globalization;

namespace ShellyHwinfoBridge.Core.Hwinfo;

/// <summary>Formats sensor values for HWiNFO: invariant culture, one decimal, dot separator.</summary>
public static class SensorValueFormatter
{
    /// <summary>Formats watts, e.g. <c>141.7</c>. Non-finite values become <c>0.0</c>; values that round to zero never show a minus sign.</summary>
    /// <param name="value">The value.</param>
    public static string FormatWatts(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) < 0.05)
        {
            value = 0;
        }

        return value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a boolean flag as <c>1</c> or <c>0</c>.</summary>
    /// <param name="value">The flag.</param>
    public static string FormatFlag(bool value) => value ? "1" : "0";
}
