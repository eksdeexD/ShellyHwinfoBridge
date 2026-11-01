using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.App;

/// <summary>Where the component sensor readings for Other Components come from.</summary>
public enum OtherSource
{
    /// <summary>No working source (HWiNFO not running, shared memory off and the gadget export incomplete).</summary>
    None,

    /// <summary>HWiNFO's shared memory.</summary>
    SharedMemory,

    /// <summary>HWiNFO's gadget registry export (every selected sensor is exported).</summary>
    Gadget,
}

/// <summary>The selected component sensors' current readings.</summary>
/// <param name="Source">The source; <see cref="OtherSource.None"/> when no source works.</param>
/// <param name="Values">One value per selected sensor, in selection order; <c>null</c> for a sensor the source does not report.</param>
/// <param name="HwinfoRunning">Whether HWiNFO is running.</param>
/// <param name="MissingGadgetSensors">
/// The selected sensor names the gadget export lacks (when shared memory is off and HWiNFO runs), for the hint
/// "Needs HWiNFO's Gadget export for: …"; empty otherwise.
/// </param>
public sealed record ComponentReadings(OtherSource Source, IReadOnlyList<double?> Values, bool HwinfoRunning, IReadOnlyList<string> MissingGadgetSensors)
{
    /// <summary>
    /// Gets HWiNFO's last poll time from the shared-memory snapshot the values were read from (whole seconds), or
    /// <c>null</c> when unknown (the gadget export has no timestamp). Used by the <see cref="DiagnosticTrace"/>.
    /// </summary>
    public DateTimeOffset? HwinfoPollTime { get; init; }

    /// <summary>HWiNFO is not running.</summary>
    public static ComponentReadings NotRunning { get; } = new(OtherSource.None, [], false, []);

    /// <summary>Gets whether a source works.</summary>
    public bool IsAvailable => Source != OtherSource.None;
}

/// <summary>Reads the selected component sensors (CPU, GPU, RAM, …) from HWiNFO.</summary>
public interface IComponentSensorSource
{
    /// <summary>Reads the current values of the selected sensors.</summary>
    /// <param name="sensors">The selected sensors.</param>
    ComponentReadings Read(IReadOnlyList<SensorSelection> sensors);
}

/// <summary>The app-computed Other Components value as the UI shows it.</summary>
/// <param name="Watts">The published value (the held value when the last tick was discarded; 0 before the first valid one).</param>
/// <param name="Source">Where the last component readings came from; <see cref="OtherSource.None"/> means not published (stays 0).</param>
/// <param name="HwinfoRunning">Whether HWiNFO was running at the last tick.</param>
/// <param name="MissingGadgetSensors">The selected sensors missing from the gadget export (see <see cref="ComponentReadings"/>).</param>
/// <param name="HasValue">Whether a valid value was computed since the last start or source loss.</param>
public sealed record OtherComponentsState(double Watts, OtherSource Source, bool HwinfoRunning, IReadOnlyList<string> MissingGadgetSensors, bool HasValue)
{
    /// <summary>Nothing known yet.</summary>
    public static OtherComponentsState Unknown { get; } = new(0, OtherSource.None, false, [], false);
}

/// <summary>How one <see cref="OtherComponentsCalculator.Update"/> tick went (for the <see cref="DiagnosticTrace"/>).</summary>
public enum OtherTickOutcome
{
    /// <summary>No component source: nothing computed, the value is reset to 0.</summary>
    NoSource,

    /// <summary>The result was valid and is now the published value.</summary>
    Accepted,

    /// <summary>The result was discarded (missing component, negative or non-finite): the previous value is held.</summary>
    Discarded,
}

/// <summary>The details of the last <see cref="OtherComponentsCalculator.Update"/> tick, before any discarding.</summary>
/// <param name="Outcome">Whether the result was published, discarded or there was no source.</param>
/// <param name="ComponentSum">This tick's (unsmoothed) component sum, or <c>null</c> when a component was missing.</param>
/// <param name="Raw">
/// DC − the (smoothed, when smoothing is on) component sum before any clamp; <c>null</c> when a component was missing,
/// DC was not finite or there was no source.
/// </param>
public sealed record OtherComponentsTick(OtherTickOutcome Outcome, double? ComponentSum, double? Raw)
{
    /// <summary>No tick yet, or no source.</summary>
    public static OtherComponentsTick NoSource { get; } = new(OtherTickOutcome.NoSource, null, null);
}

/// <summary>
/// Computes Other Components = DC − sum(selected component readings). A negative or non-finite result, or a missing
/// component reading, is discarded and the previous value is held; the first value is 0 until a valid reading
/// exists. Without a source the value is reset to 0 (not published). The component sum can be smoothed with a moving
/// average (the AC window, <see cref="AppSettings.SmoothingSamples"/>). Not thread-safe.
/// </summary>
public sealed class OtherComponentsCalculator
{
    private MovingAverage _smoother;
    private double _value;
    private bool _hasValue;

    /// <summary>Creates a calculator.</summary>
    /// <param name="smoothingSamples">The component-sum window; 1 = off.</param>
    public OtherComponentsCalculator(int smoothingSamples = 1) =>
        _smoother = new MovingAverage(Math.Clamp(smoothingSamples, 1, MovingAverage.MaxSamples));

    /// <summary>Gets the current (held) value in watts.</summary>
    public double Value => _value;

    /// <summary>Gets whether a valid value exists.</summary>
    public bool HasValue => _hasValue;

    /// <summary>Gets the details of the last <see cref="Update"/> tick (the unclamped result; never changes the published value).</summary>
    public OtherComponentsTick LastTick { get; private set; } = OtherComponentsTick.NoSource;

    /// <summary>Gets the smoothing window.</summary>
    public int SmoothingSamples => _smoother.Samples;

    /// <summary>Changes the smoothing window (clears the window when it changes).</summary>
    /// <param name="samples">The window; 1 = off.</param>
    public void SetSmoothing(int samples)
    {
        samples = Math.Clamp(samples, 1, MovingAverage.MaxSamples);
        if (samples != _smoother.Samples)
        {
            _smoother = new MovingAverage(samples);
        }
    }

    /// <summary>Computes one tick.</summary>
    /// <param name="dcWatts">The DC estimate of this poll.</param>
    /// <param name="readings">The component readings.</param>
    /// <returns>The value to publish (held on a discarded tick; 0 without a source).</returns>
    public double Update(double dcWatts, ComponentReadings readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        if (!readings.IsAvailable)
        {
            Reset();
            return _value;
        }

        double sum = 0;
        foreach (var value in readings.Values)
        {
            if (value is not double v || !double.IsFinite(v))
            {
                LastTick = new OtherComponentsTick(OtherTickOutcome.Discarded, null, null);
                return _value;
            }

            sum += v;
        }

        if (!double.IsFinite(dcWatts))
        {
            LastTick = new OtherComponentsTick(OtherTickOutcome.Discarded, sum, null);
            return _value;
        }

        var other = dcWatts - _smoother.Add(sum);
        if (!double.IsFinite(other) || other < 0)
        {
            LastTick = new OtherComponentsTick(OtherTickOutcome.Discarded, sum, other);
            return _value;
        }

        LastTick = new OtherComponentsTick(OtherTickOutcome.Accepted, sum, other);
        _value = other;
        _hasValue = true;
        return _value;
    }

    /// <summary>Drops the value (back to 0) and the smoothing window.</summary>
    public void Reset()
    {
        _value = 0;
        _hasValue = false;
        _smoother.Reset();
        LastTick = OtherComponentsTick.NoSource;
    }
}
