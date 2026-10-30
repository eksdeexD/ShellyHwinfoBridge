using System.Buffers.Binary;
using System.Text;

namespace ShellyHwinfoBridge.Core.Tests.TestSupport;

/// <summary>Builds a synthetic HWiNFO_SENS_SM2 buffer following the SDK layout.</summary>
internal sealed class HwinfoBufferBuilder
{
    private readonly List<(uint Id, uint Inst, string Orig, string User)> _sensors = [];
    private readonly List<(uint Type, uint Sensor, uint Id, string Orig, string User, string Unit, double Value)> _readings = [];

    public string Signature { get; set; } = "HWiS";

    /// <summary>Gets or sets whether elements include the trailing UTF-8 strings (392 / 460 byte elements).</summary>
    public bool WithUtf8 { get; set; } = true;

    /// <summary>Gets or sets extra bytes appended to each element (simulating future fields).</summary>
    public int ExtraElementBytes { get; set; }

    public HwinfoBufferBuilder Sensor(string name, string? userName = null, uint id = 0xF0000000)
    {
        _sensors.Add((id, (uint)_sensors.Count, name, userName ?? name));
        return this;
    }

    public HwinfoBufferBuilder Reading(int sensorIndex, uint type, string label, string unit, double value, string? userLabel = null)
    {
        _readings.Add((type, (uint)sensorIndex, (uint)_readings.Count, label, userLabel ?? label, unit, value));
        return this;
    }

    public byte[] Build()
    {
        const int header = 44;
        var sensorSize = (WithUtf8 ? 392 : 264) + ExtraElementBytes;
        var readingSize = (WithUtf8 ? 460 : 316) + ExtraElementBytes;
        var sensorOffset = header;
        var readingOffset = sensorOffset + (sensorSize * _sensors.Count);
        var buffer = new byte[readingOffset + (readingSize * _readings.Count)];
        var span = buffer.AsSpan();

        Encoding.ASCII.GetBytes(Signature).CopyTo(span);
        U32(span, 4, 2);
        U32(span, 8, 1);
        BinaryPrimitives.WriteInt64LittleEndian(span[12..], 1_798_000_000);
        U32(span, 20, (uint)sensorOffset);
        U32(span, 24, (uint)sensorSize);
        U32(span, 28, (uint)_sensors.Count);
        U32(span, 32, (uint)readingOffset);
        U32(span, 36, (uint)readingSize);
        U32(span, 40, (uint)_readings.Count);

        for (var i = 0; i < _sensors.Count; i++)
        {
            var e = span.Slice(sensorOffset + (i * sensorSize), sensorSize);
            var s = _sensors[i];
            U32(e, 0, s.Id);
            U32(e, 4, s.Inst);
            Latin1(e.Slice(8, 128), s.Orig);
            Latin1(e.Slice(136, 128), s.User);
            if (WithUtf8)
            {
                Utf8(e.Slice(264, 128), s.User);
            }
        }

        for (var i = 0; i < _readings.Count; i++)
        {
            var e = span.Slice(readingOffset + (i * readingSize), readingSize);
            var r = _readings[i];
            U32(e, 0, r.Type);
            U32(e, 4, r.Sensor);
            U32(e, 8, r.Id);
            Latin1(e.Slice(12, 128), r.Orig);
            Latin1(e.Slice(140, 128), r.User);
            Latin1(e.Slice(268, 16), r.Unit);
            BinaryPrimitives.WriteDoubleLittleEndian(e[284..], r.Value);
            BinaryPrimitives.WriteDoubleLittleEndian(e[292..], r.Value - 1);
            BinaryPrimitives.WriteDoubleLittleEndian(e[300..], r.Value + 1);
            BinaryPrimitives.WriteDoubleLittleEndian(e[308..], r.Value);
            if (WithUtf8)
            {
                Utf8(e.Slice(316, 128), r.User);
                Utf8(e.Slice(444, 16), r.Unit);
            }
        }

        return buffer;
    }

    private static void U32(Span<byte> span, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(span[offset..], value);

    private static void Latin1(Span<byte> field, string text) => Encoding.Latin1.GetBytes(text).AsSpan(0, Math.Min(text.Length, field.Length - 1)).CopyTo(field);

    private static void Utf8(Span<byte> field, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        bytes.AsSpan(0, Math.Min(bytes.Length, field.Length - 1)).CopyTo(field);
    }
}
