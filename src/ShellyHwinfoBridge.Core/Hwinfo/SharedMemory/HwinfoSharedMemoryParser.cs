using System.Buffers.Binary;
using System.Text;

namespace ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;

/// <summary>
/// Parses a byte copy of HWiNFO's <c>HWiNFO_SENS_SM2</c> shared memory, following the HWiNFO SDK header
/// <c>hwisenssm2.h</c> (all structures <c>#pragma pack(1)</c>, little-endian).
/// </summary>
/// <remarks>
/// <code>
/// HWiNFO_SENSORS_SHARED_MEM2 (header, 44 bytes; later versions may append fields)
///   0  DWORD  dwSignature            "HWiS" when active ("DAED" when shared memory is switched off or expired)
///   4  DWORD  dwVersion
///   8  DWORD  dwRevision
///  12  INT64  poll_time              __time64_t, seconds since 1970 UTC
///  20  DWORD  dwOffsetOfSensorSection
///  24  DWORD  dwSizeOfSensorElement
///  28  DWORD  dwNumSensorElements
///  32  DWORD  dwOffsetOfReadingSection
///  36  DWORD  dwSizeOfReadingElement
///  40  DWORD  dwNumReadingElements
///
/// HWiNFO_SENSORS_SENSOR_ELEMENT (264 bytes; 392 with the UTF-8 name)
///   0  DWORD  dwSensorID
///   4  DWORD  dwSensorInst
///   8  char   szSensorNameOrig[128]
/// 136  char   szSensorNameUser[128]
/// 264  BYTE   utfSensorNameUser[128]  (newer versions; UTF-8)
///
/// HWiNFO_SENSORS_READING_ELEMENT (316 bytes; 460 with the UTF-8 strings)
///   0  DWORD  tReading                SENSOR_READING_TYPE
///   4  DWORD  dwSensorIndex
///   8  DWORD  dwReadingID
///  12  char   szLabelOrig[128]
/// 140  char   szLabelUser[128]
/// 268  char   szUnit[16]
/// 284  double Value, ValueMin, ValueMax, ValueAvg
/// 316  BYTE   utfLabelUser[128]       (newer versions; UTF-8)
/// 444  BYTE   utfUnit[16]             (newer versions; UTF-8)
/// </code>
/// Element strides come from the header, so appended fields in future versions are skipped. The legacy
/// <c>char</c> strings are in the system ANSI code page and are decoded as Latin-1; the UTF-8 copies are
/// preferred when present and non-empty.
/// </remarks>
public static class HwinfoSharedMemoryParser
{
    /// <summary>The header signature when shared memory is active.</summary>
    public const string ActiveSignature = "HWiS";

    /// <summary>Size of the version 2 header.</summary>
    public const int HeaderSize = 44;

    /// <summary>Length of the name and label strings.</summary>
    public const int StringLength = 128;

    /// <summary>Length of the unit string.</summary>
    public const int UnitLength = 16;

    /// <summary>Minimum sensor element size (without the UTF-8 name).</summary>
    public const int SensorElementBaseSize = 8 + (2 * StringLength);

    /// <summary>Minimum reading element size (without the UTF-8 strings).</summary>
    public const int ReadingElementBaseSize = 12 + (2 * StringLength) + UnitLength + (4 * sizeof(double));

    private const int SensorUtfNameOffset = SensorElementBaseSize;
    private const int ReadingUtfLabelOffset = ReadingElementBaseSize;
    private const int ReadingUtfUnitOffset = ReadingUtfLabelOffset + StringLength;

    // Guards against garbage headers.
    private const int MaxElements = 100_000;
    private const int MaxElementSize = 64 * 1024;

    /// <summary>Returns whether the buffer starts with the active signature.</summary>
    /// <param name="buffer">The buffer.</param>
    public static bool HasActiveSignature(ReadOnlySpan<byte> buffer) =>
        buffer.Length >= 4 && buffer[..4].SequenceEqual("HWiS"u8);

    /// <summary>
    /// Returns how many bytes of shared memory are needed to parse everything the header describes, or 0 when the
    /// header is incomplete.
    /// </summary>
    /// <param name="header">At least the first <see cref="HeaderSize"/> bytes.</param>
    public static long GetRequiredLength(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderSize)
        {
            return 0;
        }

        var sensorEnd = (long)U32(header, 20) + ((long)U32(header, 24) * U32(header, 28));
        var readingEnd = (long)U32(header, 32) + ((long)U32(header, 36) * U32(header, 40));
        return Math.Max(HeaderSize, Math.Max(sensorEnd, readingEnd));
    }

    /// <summary>Parses the buffer.</summary>
    /// <param name="buffer">A copy of the shared memory.</param>
    /// <exception cref="HwinfoSharedMemoryException">The signature is not active or the layout is inconsistent.</exception>
    public static HwinfoSnapshot Parse(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < HeaderSize)
        {
            throw new HwinfoSharedMemoryException($"Buffer too small for the header ({buffer.Length} bytes).");
        }

        if (!HasActiveSignature(buffer))
        {
            throw new HwinfoSharedMemoryException($"Shared memory is not active (signature '{Encoding.Latin1.GetString(buffer[..4])}').");
        }

        var version = U32(buffer, 4);
        var revision = U32(buffer, 8);
        var pollTime = BinaryPrimitives.ReadInt64LittleEndian(buffer[12..]);
        var sensorOffset = U32(buffer, 20);
        var sensorSize = U32(buffer, 24);
        var sensorCount = U32(buffer, 28);
        var readingOffset = U32(buffer, 32);
        var readingSize = U32(buffer, 36);
        var readingCount = U32(buffer, 40);

        CheckSection(buffer.Length, "sensor", sensorOffset, sensorSize, sensorCount, SensorElementBaseSize);
        CheckSection(buffer.Length, "reading", readingOffset, readingSize, readingCount, ReadingElementBaseSize);

        var sensors = new List<HwinfoSensor>((int)sensorCount);
        for (var i = 0; i < (int)sensorCount; i++)
        {
            var e = buffer.Slice((int)(sensorOffset + ((long)i * sensorSize)), (int)sensorSize);
            var nameUser = Ansi(e.Slice(136, StringLength));
            if (e.Length >= SensorUtfNameOffset + StringLength && Utf8(e.Slice(SensorUtfNameOffset, StringLength)) is { Length: > 0 } utf)
            {
                nameUser = utf;
            }

            sensors.Add(new HwinfoSensor(i, U32(e, 0), U32(e, 4), Ansi(e.Slice(8, StringLength)), nameUser));
        }

        var readings = new List<HwinfoReading>((int)readingCount);
        for (var i = 0; i < (int)readingCount; i++)
        {
            var e = buffer.Slice((int)(readingOffset + ((long)i * readingSize)), (int)readingSize);
            var type = U32(e, 0);
            var sensorIndex = U32(e, 4);
            var labelUser = Ansi(e.Slice(140, StringLength));
            var unit = Ansi(e.Slice(268, UnitLength));
            if (e.Length >= ReadingUtfLabelOffset + StringLength && Utf8(e.Slice(ReadingUtfLabelOffset, StringLength)) is { Length: > 0 } utfLabel)
            {
                labelUser = utfLabel;
            }

            if (e.Length >= ReadingUtfUnitOffset + UnitLength && Utf8(e.Slice(ReadingUtfUnitOffset, UnitLength)) is { Length: > 0 } utfUnit)
            {
                unit = utfUnit;
            }

            var device = sensorIndex < (uint)sensors.Count ? sensors[(int)sensorIndex].Name : string.Empty;
            readings.Add(new HwinfoReading(
                Enum.IsDefined(typeof(HwinfoReadingType), (int)type) ? (HwinfoReadingType)type : HwinfoReadingType.Other,
                (int)Math.Min(sensorIndex, int.MaxValue),
                U32(e, 8),
                device,
                Ansi(e.Slice(12, StringLength)),
                labelUser,
                unit,
                F64(e, 284),
                F64(e, 292),
                F64(e, 300),
                F64(e, 308)));
        }

        var poll = pollTime > 0 && pollTime < 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(pollTime) : DateTimeOffset.MinValue;
        return new HwinfoSnapshot(version, revision, poll, sensors, readings);
    }

    private static void CheckSection(int bufferLength, string name, uint offset, uint size, uint count, int minSize)
    {
        if (count == 0)
        {
            return;
        }

        if (count > MaxElements || size < minSize || size > MaxElementSize)
        {
            throw new HwinfoSharedMemoryException($"Implausible {name} section: {count} elements of {size} bytes.");
        }

        if (offset < HeaderSize || offset + ((long)size * count) > bufferLength)
        {
            throw new HwinfoSharedMemoryException($"The {name} section (offset {offset}, {count} x {size} bytes) exceeds the buffer ({bufferLength} bytes).");
        }
    }

    private static uint U32(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);

    private static double F64(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadDoubleLittleEndian(span[offset..]);

    private static string Ansi(ReadOnlySpan<byte> field) => Encoding.Latin1.GetString(UntilNul(field)).Trim();

    private static string Utf8(ReadOnlySpan<byte> field) => Encoding.UTF8.GetString(UntilNul(field)).Trim();

    private static ReadOnlySpan<byte> UntilNul(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        return end < 0 ? field : field[..end];
    }
}
