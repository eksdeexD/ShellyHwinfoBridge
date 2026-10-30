using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;

/// <summary>
/// Reads HWiNFO's sensor list and values from the <c>Global\HWiNFO_SENS_SM2</c> shared memory. Only the setup
/// wizard uses this (sensor picker and live verification); polling never depends on it.
/// </summary>
/// <remarks>
/// Shared memory exists only while HWiNFO runs with "Shared Memory Support" enabled; the free version switches it
/// off after 12 hours. Every call takes a fresh copy, so results are always current. All methods return empty or
/// <c>false</c> results instead of throwing when shared memory is unavailable.
/// </remarks>
public sealed class HwinfoSharedMemoryReader
{
    /// <summary>The name of HWiNFO's shared-memory mapping.</summary>
    public const string MapName = @"Global\HWiNFO_SENS_SM2";

    /// <summary>The name of the mutex HWiNFO holds while updating shared memory.</summary>
    public const string MutexName = @"Global\HWiNFO_SM2_MUTEX";

    private const long MaxMappingBytes = 64L * 1024 * 1024;

    private readonly Func<byte[]?> _source;
    private readonly ILogger _logger;

    /// <summary>Creates a reader over a custom source (tests pass a hand-built buffer).</summary>
    /// <param name="source">Returns a copy of the shared memory, or <c>null</c> when it does not exist.</param>
    /// <param name="logger">Optional logger.</param>
    public HwinfoSharedMemoryReader(Func<byte[]?> source, ILogger? logger = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets whether shared memory is present, active and parseable right now.</summary>
    public bool IsAvailable => TryGetSnapshot() is not null;

    /// <summary>Creates a reader over the real HWiNFO shared memory.</summary>
    /// <param name="logger">Optional logger.</param>
    [SupportedOSPlatform("windows")]
    public static HwinfoSharedMemoryReader CreateDefault(ILogger? logger = null) => new(ReadMapping, logger);

    /// <summary>Takes and parses a copy of shared memory; <c>null</c> when unavailable or invalid.</summary>
    public HwinfoSnapshot? TryGetSnapshot()
    {
        byte[]? buffer;
        try
        {
            buffer = _source();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "HWiNFO shared memory could not be read.");
            return null;
        }

        if (buffer is null)
        {
            return null;
        }

        try
        {
            return HwinfoSharedMemoryParser.Parse(buffer);
        }
        catch (HwinfoSharedMemoryException ex)
        {
            _logger.LogDebug("HWiNFO shared memory is not usable: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>Returns all readings, or an empty list when unavailable.</summary>
    public IReadOnlyList<HwinfoReading> GetReadings() => TryGetSnapshot()?.Readings ?? [];

    /// <summary>
    /// Returns all <c>W</c> readings grouped by device (wizard step 4), or an empty list when unavailable.
    /// </summary>
    public IReadOnlyList<HwinfoDeviceGroup> GetPowerSensors() => TryGetSnapshot()?.GetPowerSensors() ?? [];

    /// <summary>Reads the current value of the first reading with the given label (wizard step 5 live readout).</summary>
    /// <param name="label">The reading label, e.g. <c>DC Power (Est.)</c>.</param>
    /// <param name="value">The value when found.</param>
    public bool TryReadValue(string label, out double value) => TryReadValue(label, null, out value);

    /// <summary>Reads the current value of the first reading with the given label on the given device.</summary>
    /// <param name="label">The reading label.</param>
    /// <param name="device">The device name (e.g. <c>Shelly Power</c>), or <c>null</c> for any device.</param>
    /// <param name="value">The value when found.</param>
    public bool TryReadValue(string label, string? device, out double value)
    {
        var reading = TryGetSnapshot()?.FindReading(label, device);
        value = reading?.Value ?? 0;
        return reading is not null;
    }

    [SupportedOSPlatform("windows")]
    private static byte[]? ReadMapping()
    {
        MemoryMappedFile mapping;
        try
        {
            mapping = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        using (mapping)
        using (var view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
        using (var gate = SharedMemoryGate.Enter())
        {
            var capacity = Math.Min(view.Capacity, MaxMappingBytes);
            if (capacity < HwinfoSharedMemoryParser.HeaderSize)
            {
                return null;
            }

            var header = new byte[HwinfoSharedMemoryParser.HeaderSize];
            view.ReadArray(0, header, 0, header.Length);
            var length = Math.Min(HwinfoSharedMemoryParser.GetRequiredLength(header), capacity);
            var buffer = new byte[length];
            view.ReadArray(0, buffer, 0, buffer.Length);
            return buffer;
        }
    }

    /// <summary>Best-effort hold of HWiNFO's update mutex while copying, so the copy is consistent.</summary>
    [SupportedOSPlatform("windows")]
    private sealed class SharedMemoryGate : IDisposable
    {
        private Mutex? _mutex;
        private bool _owned;

        public static SharedMemoryGate Enter()
        {
            var gate = new SharedMemoryGate();
            try
            {
                if (Mutex.TryOpenExisting(MutexName, out var mutex))
                {
                    gate._mutex = mutex;
                    try
                    {
                        gate._owned = mutex.WaitOne(TimeSpan.FromMilliseconds(100));
                    }
                    catch (AbandonedMutexException)
                    {
                        gate._owned = true;
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
            {
                // Read without the mutex; a torn copy only affects one wizard refresh.
            }

            return gate;
        }

        public void Dispose()
        {
            if (_owned)
            {
                _mutex?.ReleaseMutex();
            }

            _mutex?.Dispose();
        }
    }
}
