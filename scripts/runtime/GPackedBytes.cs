using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GdRuntime;

public static partial class G
{
    /// <summary>Reinterpret native packed bytes, as PackedByteArray.to_float32_array does.</summary>
    public static List<float> to_float32_array(List<byte> bytes)
    {
        ReadOnlySpan<byte> data = CollectionsMarshal.AsSpan(bytes);
        return new List<float>(MemoryMarshal.Cast<byte, float>(data[..(data.Length / 4 * 4)]).ToArray());
    }
}
