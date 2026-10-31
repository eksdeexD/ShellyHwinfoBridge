namespace ShellyHwinfoBridge.Core.Curves;

/// <summary>The result of converting one AC reading.</summary>
/// <param name="AcWatts">The raw AC reading (negative values clamped to 0).</param>
/// <param name="SmoothedAcWatts">The AC value after moving-average smoothing (equal to <paramref name="AcWatts"/> when N = 1).</param>
/// <param name="DcWatts">Estimated DC output power.</param>
/// <param name="LossWatts">PSU loss: smoothed AC minus DC.</param>
/// <param name="CurveVoltage">The voltage whose curve was used.</param>
public sealed record PowerReading(double AcWatts, double SmoothedAcWatts, double DcWatts, double LossWatts, InputVoltage CurveVoltage);

/// <summary>
/// Converts raw AC readings to DC and loss: optional smoothing, voltage-based curve selection and DC estimation.
/// Not thread-safe; intended to be called from a single poller.
/// </summary>
public sealed class PowerCalculator
{
    private readonly PsuCurveSet _curves;
    private readonly InputVoltageMode _voltageMode;
    private readonly MovingAverage _smoother;
    private readonly Dictionary<InputVoltage, DcEstimator> _estimators = new();

    /// <summary>Creates a calculator.</summary>
    /// <param name="curves">The PSU's curves.</param>
    /// <param name="voltageMode">Automatic or forced voltage selection.</param>
    /// <param name="smoothingSamples">Moving-average window over AC; 1 disables smoothing.</param>
    public PowerCalculator(PsuCurveSet curves, InputVoltageMode voltageMode = InputVoltageMode.Auto, int smoothingSamples = 1)
    {
        _curves = curves ?? throw new ArgumentNullException(nameof(curves));
        _voltageMode = voltageMode;
        _smoother = new MovingAverage(smoothingSamples);
    }

    /// <summary>Converts one AC reading.</summary>
    /// <param name="acWatts">The measured AC power.</param>
    /// <param name="measuredVolts">The measured mains voltage, if known.</param>
    public PowerReading Calculate(double acWatts, double? measuredVolts)
    {
        var ac = double.IsFinite(acWatts) && acWatts > 0 ? acWatts : 0;
        var smoothed = _smoother.Add(ac);
        // A single-voltage unit uses its one curve whatever the mains voltage (CurveVoltage reports the curve used).
        var voltage = _curves.AvailableVoltage(VoltageSelector.Resolve(_voltageMode, measuredVolts));
        if (!_estimators.TryGetValue(voltage, out var estimator))
        {
            estimator = new DcEstimator(_curves.Select(voltage));
            _estimators[voltage] = estimator;
        }

        var dc = estimator.EstimateDc(smoothed);
        return new PowerReading(ac, smoothed, dc, smoothed - dc, voltage);
    }

    /// <summary>Clears the smoothing window (e.g. after the device went offline).</summary>
    public void Reset() => _smoother.Reset();
}
