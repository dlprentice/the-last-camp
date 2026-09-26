namespace GdRuntime;

using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;

/// <summary>Less common GDScript builtins: String.num, byte-array encodings, and reflection-based get/set for plain C#
/// objects (GDScript's Object.get/set on what are now plain classes).</summary>
public static partial class G
{
    private const int MaxDecimals = 32;

    /// <summary>String.num(): printf rounding (via the engine's formatter), trailing zeros and a bare point removed.</summary>
    public static string string_num(double value, long decimals = -1)
    {
        if (double.IsNaN(value))
            return "nan";
        if (double.IsInfinity(value))
            return value < 0 ? "-inf" : "inf";
        if (decimals < 0)
        {
            decimals = 14;
            double abs = Math.Abs(value);
            if (abs > 10)
                decimals -= (long)Math.Floor(Math.Log10(abs));
        }
        if (decimals > MaxDecimals)
            decimals = MaxDecimals;
        string text = format($"%.{decimals}f", value);
        if (text.Contains('.'))
        {
            text = text.TrimEnd('0');
            if (text.EndsWith('.'))
                text = text.Substring(0, text.Length - 1);
        }
        return text;
    }

    public static string string_num_int64(long value, long radix = 10, bool capitalize = false) =>
        Eval("String.num_int64(a, b, c)", value, radix, capitalize).AsString();

    public static string string_chr(long code) => char.ConvertFromUtf32((int)code);

    public static bool is_instance_id_valid(long id) => GodotObject.IsInstanceIdValid(unchecked((ulong)id));

    public static string hex_encode(IList<byte> bytes) => Convert.ToHexString(new List<byte>(bytes).ToArray()).ToLowerInvariant();

    public static List<byte> decompress_dynamic(IList<byte> bytes, long maxOutputSize, long mode = 0) =>
        new(Eval("a.decompress_dynamic(b, c)", new List<byte>(bytes).ToArray(), maxOutputSize, mode).AsByteArray());

    public static List<byte> decompress(IList<byte> bytes, long bufferSize, long mode = 0) =>
        new(Eval("a.decompress(b, c)", new List<byte>(bytes).ToArray(), bufferSize, mode).AsByteArray());

    public static List<byte> compress(IList<byte> bytes, long mode = 0) =>
        new(Eval("a.compress(b)", new List<byte>(bytes).ToArray(), mode).AsByteArray());

    public static void encode_half(IList<byte> bytes, long offset, double value)
    {
        ushort bits = make_half_float((float)value);
        bytes[(int)offset] = (byte)(bits & 0xff);
        bytes[(int)offset + 1] = (byte)(bits >> 8);
    }

    /// <summary>The engine's float-to-half conversion (Math::make_half_float): the mantissa is truncated, not
    /// rounded, and values below the smallest normal half become zero. .NET's (Half) rounds to nearest, which
    /// differs from the engine in the last bit for about half of all values.</summary>
    public static ushort make_half_float(float value)
    {
        uint x = BitConverter.SingleToUInt32Bits(value);
        uint sign = x >> 31, mantissa = x & ((1u << 23) - 1), exponent = x & (0xFFu << 23);
        if (exponent >= 0x47800000u)
        {
            mantissa = mantissa != 0 && exponent == (0xFFu << 23) ? (1u << 23) - 1 : 0;
            return (ushort)((sign << 15) | (0x1Fu << 10) | (mantissa >> 13));
        }
        if (exponent <= 0x38000000u)
            return 0;
        return (ushort)((sign << 15) | ((exponent - 0x38000000u) >> 13) | (mantissa >> 13));
    }

    public static double decode_half(IList<byte> bytes, long offset) =>
        (double)BitConverter.UInt16BitsToHalf((ushort)(bytes[(int)offset] | (bytes[(int)offset + 1] << 8)));

    // -- PackedByteArray encode_*/decode_*: little-endian, as the engine on x86-64
    private static byte[] Slice(IList<byte> bytes, long offset, int count)
    {
        var chunk = new byte[count];
        for (int i = 0; i < count; i++)
            chunk[i] = bytes[(int)offset + i];
        return chunk;
    }
    private static void Write(IList<byte> bytes, long offset, byte[] chunk)
    {
        for (int i = 0; i < chunk.Length; i++)
            bytes[(int)offset + i] = chunk[i];
    }
    public static double decode_float(IList<byte> bytes, long offset) => BitConverter.ToSingle(Slice(bytes, offset, 4));
    public static double decode_double(IList<byte> bytes, long offset) => BitConverter.ToDouble(Slice(bytes, offset, 8));
    public static long decode_u8(IList<byte> bytes, long offset) => bytes[(int)offset];
    public static long decode_s8(IList<byte> bytes, long offset) => (sbyte)bytes[(int)offset];
    public static long decode_u16(IList<byte> bytes, long offset) => BitConverter.ToUInt16(Slice(bytes, offset, 2));
    public static long decode_s16(IList<byte> bytes, long offset) => BitConverter.ToInt16(Slice(bytes, offset, 2));
    public static long decode_u32(IList<byte> bytes, long offset) => BitConverter.ToUInt32(Slice(bytes, offset, 4));
    public static long decode_s32(IList<byte> bytes, long offset) => BitConverter.ToInt32(Slice(bytes, offset, 4));
    public static long decode_u64(IList<byte> bytes, long offset) => unchecked((long)BitConverter.ToUInt64(Slice(bytes, offset, 8)));
    public static long decode_s64(IList<byte> bytes, long offset) => BitConverter.ToInt64(Slice(bytes, offset, 8));
    public static void encode_float(IList<byte> bytes, long offset, double value) => Write(bytes, offset, BitConverter.GetBytes((float)value));
    public static void encode_double(IList<byte> bytes, long offset, double value) => Write(bytes, offset, BitConverter.GetBytes(value));
    public static void encode_u8(IList<byte> bytes, long offset, long value) => bytes[(int)offset] = unchecked((byte)value);
    public static void encode_s8(IList<byte> bytes, long offset, long value) => bytes[(int)offset] = unchecked((byte)(sbyte)value);
    public static void encode_u16(IList<byte> bytes, long offset, long value) => Write(bytes, offset, BitConverter.GetBytes(unchecked((ushort)value)));
    public static void encode_s16(IList<byte> bytes, long offset, long value) => Write(bytes, offset, BitConverter.GetBytes(unchecked((short)value)));
    public static void encode_u32(IList<byte> bytes, long offset, long value) => Write(bytes, offset, BitConverter.GetBytes(unchecked((uint)value)));
    public static void encode_s32(IList<byte> bytes, long offset, long value) => Write(bytes, offset, BitConverter.GetBytes(unchecked((int)value)));
    public static void encode_u64(IList<byte> bytes, long offset, long value) => Write(bytes, offset, BitConverter.GetBytes(unchecked((ulong)value)));
    public static void encode_s64(IList<byte> bytes, long offset, long value) => Write(bytes, offset, BitConverter.GetBytes(value));

    /// <summary>PackedXArray.to_byte_array(): the raw little-endian element bytes.</summary>
    public static List<byte> to_byte_array<T>(IList<T> values) where T : unmanaged
    {
        T[] array = values as T[] ?? new List<T>(values).ToArray();
        return new List<byte>(System.Runtime.InteropServices.MemoryMarshal.AsBytes(array.AsSpan()).ToArray());
    }

    public static string get_string_from_utf16(List<byte> bytes) => System.Text.Encoding.Unicode.GetString(bytes.ToArray());
    public static string get_string_from_wchar(List<byte> bytes) => System.Text.Encoding.UTF32.GetString(bytes.ToArray());

    /// <summary>Object.get(name) on a plain C# object: its field or property of that name, as a Variant.</summary>
    private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        | BindingFlags.Static | BindingFlags.FlattenHierarchy;

    /// <summary>obj.get(name) as GDScript reads it: a C# field or property of that name first (static ones too, which
    /// GDScript reads through an instance, and lists the engine cannot see), then the engine's property.</summary>
    public static Variant get_member(object target, string name)
    {
        if (target is GdBox box && box.Value != null)
            target = box.Value;
        Type type = target.GetType();
        FieldInfo? field = type.GetField(name, MemberFlags);
        if (field != null)
            return GdScriptRef.ToVariant(field.GetValue(field.IsStatic ? null : target));
        PropertyInfo? property = type.GetProperty(name, MemberFlags);
        if (property != null && property.GetIndexParameters().Length == 0)
            return GdScriptRef.ToVariant(property.GetValue(property.GetMethod!.IsStatic ? null : target));
        return target is GodotObject godot ? godot.Get(name) : default;
    }

    /// <summary>obj.set(name, value), the same way round: a C# field or property first, then the engine's property.</summary>
    public static void set_member(object target, string name, Variant value)
    {
        if (target is GdBox box && box.Value != null)
            target = box.Value;
        Type type = target.GetType();
        FieldInfo? field = type.GetField(name, MemberFlags);
        if (field != null)
        {
            field.SetValue(field.IsStatic ? null : target, GdScriptRef.FromVariant(value, field.FieldType));
            return;
        }
        PropertyInfo? property = type.GetProperty(name, MemberFlags);
        if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
        {
            property.SetValue(property.SetMethod!.IsStatic ? null : target, GdScriptRef.FromVariant(value, property.PropertyType));
            return;
        }
        if (target is GodotObject godot)
            godot.Set(name, value);
    }
}
