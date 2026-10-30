namespace ShellyHwinfoBridge.Core.Curves;

/// <summary>
/// A simple moving average over the last N samples. With N = 1 it returns the input unchanged (smoothing off).
/// </summary>
public sealed class MovingAverage
{
    /// <summary>The largest supported window size.</summary>
    public const int MaxSamples = 30;

    private readonly double[] _buffer;
    private int _count;
    private int _next;

    /// <summary>Creates a moving average over <paramref name="samples"/> samples.</summary>
    /// <param name="samples">Window size, 1 to <see cref="MaxSamples"/>.</param>
    public MovingAverage(int samples)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(samples, MaxSamples);
        _buffer = new double[samples];
    }

    /// <summary>Gets the window size.</summary>
    public int Samples => _buffer.Length;

    /// <summary>Adds a sample and returns the average of the samples currently in the window.</summary>
    /// <param name="value">The new sample.</param>
    public double Add(double value)
    {
        _buffer[_next] = value;
        _next = (_next + 1) % _buffer.Length;
        if (_count < _buffer.Length)
        {
            _count++;
        }

        var sum = 0.0;
        for (var i = 0; i < _count; i++)
        {
            sum += _buffer[i];
        }

        return sum / _count;
    }

    /// <summary>Discards all samples.</summary>
    public void Reset()
    {
        _count = 0;
        _next = 0;
    }
}
