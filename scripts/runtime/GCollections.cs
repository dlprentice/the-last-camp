namespace GdRuntime;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>GDScript's Array and Dictionary methods for .NET lists and ordered dictionaries (typed GDScript
/// collections) and for Godot collections (untyped ones), with GDScript's results: empty pops return the default,
/// sorts use Godot's algorithm, shuffles and picks draw from the engine's global generator.</summary>
public static partial class G
{
    // -- conversions between .NET and Godot collections
    public static Variant ToVariant<[MustBeVariant] T>(List<T> list) => ToGodotArray(list);
    public static Variant ToVariant<[MustBeVariant] TKey, [MustBeVariant] TValue>(OrderedDict<TKey, TValue> dict) where TKey : notnull => ToGodotDictionary(dict);

    public static Godot.Collections.Array ToGodotArray<[MustBeVariant] T>(IEnumerable<T> items)
    {
        var array = new Godot.Collections.Array();
        foreach (T item in items)
            array.Add(Variant.From(item));
        return array;
    }

    public static Godot.Collections.Array<T> ToTypedArray<[MustBeVariant] T>(IEnumerable<T> items) => new(items);

    public static Godot.Collections.Dictionary ToGodotDictionary<[MustBeVariant] TKey, [MustBeVariant] TValue>(IEnumerable<KeyValuePair<TKey, TValue>> items)
    {
        var dict = new Godot.Collections.Dictionary();
        foreach (KeyValuePair<TKey, TValue> kv in items)
            dict[Variant.From(kv.Key)] = Variant.From(kv.Value);
        return dict;
    }

    public static List<T> ToList<T>(IEnumerable<T> items) => new(items);
    public static List<T> ToList<[MustBeVariant] T>(Godot.Collections.Array array)
    {
        var list = new List<T>(array.Count);
        foreach (Variant v in array)
            list.Add(v.As<T>());
        return list;
    }
    /// <summary>A GDScript array held in a Variant (a Godot array or a packed array) as a typed list.</summary>
    public static List<T> ListFromVariant<[MustBeVariant] T>(Variant value) => value.VariantType switch
    {
        Variant.Type.Nil => new List<T>(),
        Variant.Type.Array => ToList<T>(value.AsGodotArray()),
        Variant.Type.PackedByteArray or Variant.Type.PackedInt32Array or Variant.Type.PackedInt64Array or Variant.Type.PackedFloat32Array
            or Variant.Type.PackedFloat64Array or Variant.Type.PackedStringArray or Variant.Type.PackedVector2Array
            or Variant.Type.PackedVector3Array or Variant.Type.PackedColorArray or Variant.Type.PackedVector4Array
            when value.VariantType == PackedTypeOf<T>() => PackedToList<T>(value),
        _ => ToList<T>(Eval("Array(a)", value).AsGodotArray()),
    };

    private static List<T> PackedToList<T>(Variant value)
    {
        object list = value.VariantType switch
        {
            Variant.Type.PackedByteArray => new List<byte>(value.AsByteArray()),
            Variant.Type.PackedInt32Array => new List<int>(value.AsInt32Array()),
            Variant.Type.PackedInt64Array => new List<long>(value.AsInt64Array()),
            Variant.Type.PackedFloat32Array => new List<float>(value.AsFloat32Array()),
            Variant.Type.PackedFloat64Array => new List<double>(value.AsFloat64Array()),
            Variant.Type.PackedStringArray => new List<string>(value.AsStringArray()),
            Variant.Type.PackedVector2Array => new List<Vector2>(value.AsVector2Array()),
            Variant.Type.PackedVector3Array => new List<Vector3>(value.AsVector3Array()),
            Variant.Type.PackedColorArray => new List<Color>(value.AsColorArray()),
            Variant.Type.PackedVector4Array => new List<Vector4>(value.AsVector4Array()),
            _ => new List<T>(),
        };
        return (List<T>)list;
    }

    private static Variant.Type PackedTypeOf<T>() => typeof(T) == typeof(byte) ? Variant.Type.PackedByteArray
        : typeof(T) == typeof(int) ? Variant.Type.PackedInt32Array : typeof(T) == typeof(long) ? Variant.Type.PackedInt64Array
        : typeof(T) == typeof(float) ? Variant.Type.PackedFloat32Array : typeof(T) == typeof(double) ? Variant.Type.PackedFloat64Array
        : typeof(T) == typeof(string) ? Variant.Type.PackedStringArray : typeof(T) == typeof(Vector2) ? Variant.Type.PackedVector2Array
        : typeof(T) == typeof(Vector3) ? Variant.Type.PackedVector3Array : typeof(T) == typeof(Color) ? Variant.Type.PackedColorArray
        : typeof(T) == typeof(Vector4) ? Variant.Type.PackedVector4Array : Variant.Type.Nil;

    public static Godot.Collections.Array<T> ToTypedArray<[MustBeVariant] T>(Godot.Collections.Array items)
    {
        var array = new Godot.Collections.Array<T>();
        foreach (Variant item in items)
            array.Add(item.As<T>());
        return array;
    }
    public static List<long> ToLongList(IEnumerable<int> items) => new(System.Linq.Enumerable.Select(items, x => (long)x));
    public static List<int> ToIntList(IEnumerable<long> items) => new(System.Linq.Enumerable.Select(items, x => (int)x));
    public static List<double> ToDoubleList(IEnumerable<float> items) => new(System.Linq.Enumerable.Select(items, x => (double)x));
    public static List<float> ToFloatList(IEnumerable<double> items) => new(System.Linq.Enumerable.Select(items, x => (float)x));

    public static T[] ToArray<T>(IEnumerable<T> items) => new List<T>(items).ToArray();
    public static T[] ToArray<[MustBeVariant] T>(Godot.Collections.Array array) => ToList<T>(array).ToArray();

    public static OrderedDict<TKey, TValue> ToOrderedDict<[MustBeVariant] TKey, [MustBeVariant] TValue>(Godot.Collections.Dictionary dict) where TKey : notnull
    {
        var result = new OrderedDict<TKey, TValue>();
        foreach (KeyValuePair<Variant, Variant> kv in dict)
            result[kv.Key.As<TKey>()] = kv.Value.As<TValue>();
        return result;
    }
    public static OrderedDict<TKey, TValue> DictFromVariant<[MustBeVariant] TKey, [MustBeVariant] TValue>(Variant value) where TKey : notnull =>
        value.VariantType == Variant.Type.Nil ? new() : ToOrderedDict<TKey, TValue>(value.AsGodotDictionary());
    public static OrderedDict<TKey, TValue> ToOrderedDict<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> items) where TKey : notnull => new(items);

    /// <summary>Edits a fixed C# array (an exported or engine-returned packed array) as a list; returns the new array.</summary>
    public static T[] Mutate<T>(T[] array, Action<List<T>> edit)
    {
        var list = new List<T>(array ?? System.Array.Empty<T>());
        edit(list);
        return list.ToArray();
    }

    public static Godot.Collections.Dictionary<TKey, TValue> ToTypedDictionary<[MustBeVariant] TKey, [MustBeVariant] TValue>(IEnumerable<KeyValuePair<TKey, TValue>> items)
    {
        var dict = new Godot.Collections.Dictionary<TKey, TValue>();
        foreach (KeyValuePair<TKey, TValue> kv in items)
            dict[kv.Key] = kv.Value;
        return dict;
    }

    /// <summary>A GDScript enum used as a Dictionary: member names to values, in declaration order.</summary>
    public static Godot.Collections.Dictionary enum_dict<TEnum>() where TEnum : struct, Enum
    {
        var dict = new Godot.Collections.Dictionary();
        foreach (TEnum value in Enum.GetValues<TEnum>())
            dict[Enum.GetName(value)!] = Convert.ToInt64(value);
        return dict;
    }

    // -- Array methods
    public static T pop_back<T>(IList<T> list)
    {
        if (list.Count == 0)
            return default!;
        T value = list[list.Count - 1];
        list.RemoveAt(list.Count - 1);
        return value;
    }

    public static T pop_front<T>(IList<T> list)
    {
        if (list.Count == 0)
            return default!;
        T value = list[0];
        list.RemoveAt(0);
        return value;
    }

    public static T pop_at<T>(IList<T> list, long index)
    {
        if (index < 0)
            index += list.Count;
        if (index < 0 || index >= list.Count)
            return default!;
        T value = list[(int)index];
        list.RemoveAt((int)index);
        return value;
    }

    public static T back<T>(IList<T> list) => list.Count == 0 ? default! : list[list.Count - 1];
    public static T front<T>(IList<T> list) => list.Count == 0 ? default! : list[0];

    /// <summary>Array.pick_random(): one draw from the engine's global generator, as Math::rand() % size.</summary>
    public static T pick_random<T>(IList<T> list) => list.Count == 0 ? default! : list[(int)((long)GD.Randi() % list.Count)];

    public static T max<T>(IList<T> list) where T : IComparable<T>
    {
        if (list.Count == 0)
            return default!;
        T best = list[0];
        for (int i = 1; i < list.Count; i++)
            if (best.CompareTo(list[i]) < 0)
                best = list[i];
        return best;
    }

    public static T min<T>(IList<T> list) where T : IComparable<T>
    {
        if (list.Count == 0)
            return default!;
        T best = list[0];
        for (int i = 1; i < list.Count; i++)
            if (list[i].CompareTo(best) < 0)
                best = list[i];
        return best;
    }

    public static void resize<T>(List<T> list, int size)
    {
        if (size < list.Count)
            list.RemoveRange(size, list.Count - size);
        else
            while (list.Count < size)
                list.Add(TypedDefault<T>.Make());
    }

    /// <summary>What a GDScript typed array's resize() puts in new slots: the type's empty value ("", an empty
    /// StringName or NodePath, a new empty array or dictionary), not null. Objects stay null.</summary>
    private static class TypedDefault<T>
    {
        public static readonly Func<T> Make = Build();

        private static Func<T> Build()
        {
            Type type = typeof(T);
            if (type == typeof(string))
                return () => (T)(object)"";
            if (type == typeof(StringName))
                return () => (T)(object)new StringName();
            if (type == typeof(NodePath))
                return () => (T)(object)new NodePath();
            bool collection = type == typeof(Godot.Collections.Array) || type == typeof(Godot.Collections.Dictionary)
                || (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>)
                    || type.GetGenericTypeDefinition() == typeof(Godot.Collections.Array<>)
                    || type.GetGenericTypeDefinition() == typeof(Godot.Collections.Dictionary<,>)
                    || type.GetGenericTypeDefinition() == typeof(OrderedDict<,>)));
            if (collection)
                return () => (T)Activator.CreateInstance(type)!;
            return () => default!;
        }
    }
    public static void resize(Godot.Collections.Array array, int size) => array.Resize(size);
    public static void resize<[MustBeVariant] T>(Godot.Collections.Array<T> array, int size) => array.Resize(size);
    public static void resize<T>(T[] array, int size) => throw new InvalidOperationException("resize() on a fixed array");

    public static void fill<T>(IList<T> list, T value)
    {
        for (int i = 0; i < list.Count; i++)
            list[i] = value;
    }

    public static void sort(List<long> list) => GdSort.Sort(list, static (a, b) => a < b);
    public static void sort(List<int> list) => GdSort.Sort(list, static (a, b) => a < b);
    public static void sort(List<double> list) => GdSort.Sort(list, static (a, b) => a < b);
    public static void sort(List<float> list) => GdSort.Sort(list, static (a, b) => a < b);
    public static void sort(List<string> list) => GdSort.Sort(list, static (a, b) => string.CompareOrdinal(a, b) < 0);
    public static void sort(List<StringName> list) => GdSort.Sort(list, static (a, b) => string.CompareOrdinal(a.ToString(), b.ToString()) < 0);
    public static void sort(List<Vector2> list) => GdSort.Sort(list, static (a, b) => a < b);
    public static void sort(List<Vector3> list) => GdSort.Sort(list, static (a, b) => a < b);
    public static void sort(List<Vector2I> list) => GdSort.Sort(list, static (a, b) => a < b);
    public static void sort(Godot.Collections.Array array) => array.Sort();
    public static void sort<[MustBeVariant] T>(Godot.Collections.Array<T> array) => array.Sort();

    public static void sort_custom<T>(IList<T> list, Func<T, T, bool> less) => GdSort.Sort(list, less);

    /// <summary>Array.shuffle(): Fisher-Yates from the end, j = Math::rand() % (i + 1).</summary>
    public static void shuffle<T>(IList<T> list)
    {
        int n = list.Count;
        if (n < 2)
            return;
        for (int i = n - 1; i >= 1; i--)
        {
            int j = (int)((long)GD.Randi() % (i + 1));
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public static long count<T>(IList<T> list, T value)
    {
        long n = 0;
        EqualityComparer<T> eq = EqualityComparer<T>.Default;
        foreach (T item in list)
            if (eq.Equals(item, value))
                n++;
        return n;
    }

    public static long find<T>(IList<T> list, T value, long from = 0)
    {
        if (from < 0)
            from += list.Count;
        EqualityComparer<T> eq = EqualityComparer<T>.Default;
        for (int i = (int)Math.Max(0, from); i < list.Count; i++)
            if (eq.Equals(list[i], value))
                return i;
        return -1;
    }

    /// <summary>Array.slice(begin, end, step): negative indices count from the end, end is exclusive.</summary>
    public static List<T> slice<T>(IList<T> list, long begin, long end = int.MaxValue, long step = 1, bool deep = false)
    {
        var result = new List<T>();
        int size = list.Count;
        if (size == 0 || step == 0)
            return result;
        if (begin < 0)
            begin += size;
        if (end < 0)
            end += size;
        if (step > 0)
        {
            begin = Math.Clamp(begin, 0, size);
            end = Math.Clamp(end, 0, size);
            for (long i = begin; i < end; i += step)
                result.Add(list[(int)i]);
        }
        else
        {
            begin = Math.Clamp(begin, -1, size - 1);
            end = Math.Clamp(end, -1, size - 1);
            for (long i = begin; i > end; i += step)
                result.Add(list[(int)i]);
        }
        return result;
    }
    public static Godot.Collections.Array slice(Godot.Collections.Array array, long begin, long end = int.MaxValue, long step = 1, bool deep = false) =>
        array.GetSliceRange((int)begin, (int)Math.Min(end, int.MaxValue), (int)step, deep);
    public static Godot.Collections.Array<T> slice<[MustBeVariant] T>(Godot.Collections.Array<T> array, long begin, long end = int.MaxValue, long step = 1, bool deep = false) =>
        array.GetSliceRange((int)begin, (int)Math.Min(end, int.MaxValue), (int)step, deep);

    public static List<T> filter<T>(IEnumerable<T> items, Func<T, bool> keep)
    {
        var result = new List<T>();
        foreach (T item in items)
            if (keep(item))
                result.Add(item);
        return result;
    }
    public static Godot.Collections.Array<T> filter<[MustBeVariant] T>(Godot.Collections.Array<T> items, Func<T, bool> keep)
    {
        var result = new Godot.Collections.Array<T>();
        foreach (T item in items)
            if (keep(item))
                result.Add(item);
        return result;
    }
    public static Godot.Collections.Array filter(Godot.Collections.Array items, Func<Variant, bool> keep)
    {
        var result = new Godot.Collections.Array();
        foreach (Variant item in items)
            if (keep(item))
                result.Add(item);
        return result;
    }

    public static Godot.Collections.Array map<T>(IEnumerable<T> items, Func<T, Variant> f)
    {
        var result = new Godot.Collections.Array();
        foreach (T item in items)
            result.Add(f(item));
        return result;
    }

    public static TAcc reduce<T, TAcc>(IEnumerable<T> items, Func<TAcc, T, TAcc> f, TAcc accumulator)
    {
        foreach (T item in items)
            accumulator = f(accumulator, item);
        return accumulator;
    }

    public static T reduce<T>(IList<T> items, Func<T, T, T> f)
    {
        if (items.Count == 0)
            return default!;
        T accumulator = items[0];
        for (int i = 1; i < items.Count; i++)
            accumulator = f(accumulator, items[i]);
        return accumulator;
    }

    public static bool any<T>(IEnumerable<T> items, Func<T, bool> test)
    {
        foreach (T item in items)
            if (test(item))
                return true;
        return false;
    }

    public static bool all<T>(IEnumerable<T> items, Func<T, bool> test)
    {
        foreach (T item in items)
            if (!test(item))
                return false;
        return true;
    }

    public static long bsearch<T>(IList<T> list, T value, bool before = true) where T : IComparable<T>
    {
        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            bool go_right = before ? list[mid].CompareTo(value) < 0 : !(value.CompareTo(list[mid]) < 0);
            if (go_right)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    public static long hash_array<[MustBeVariant] T>(IEnumerable<T> items) => (uint)GD.Hash(ToGodotArray(items));
    public static long hash_array(Godot.Collections.Array items) => (uint)GD.Hash(items);

    public static void assign<T>(IList<T> target, IEnumerable<T> source)
    {
        var copy = new List<T>(source);
        target.Clear();
        foreach (T item in copy)
            target.Add(item);
    }

    public static List<T> concat<T>(IEnumerable<T> a, IEnumerable<T> b)
    {
        var result = new List<T>(a);
        result.AddRange(b);
        return result;
    }

    public static bool ArrayEquals<T>(IList<T> a, IList<T> b)
    {
        if (a.Count != b.Count)
            return false;
        EqualityComparer<T> eq = EqualityComparer<T>.Default;
        for (int i = 0; i < a.Count; i++)
            if (!eq.Equals(a[i], b[i]))
                return false;
        return true;
    }
    public static bool ArrayEquals(Godot.Collections.Array a, Godot.Collections.Array b) => Eval("a == b", a, b).AsBool();
    public static bool DictEquals(Godot.Collections.Dictionary a, Godot.Collections.Dictionary b) => Eval("a == b", a, b).AsBool();
    public static bool DictEquals<[MustBeVariant] TKey, [MustBeVariant] TValue>(Godot.Collections.Dictionary<TKey, TValue> a, Godot.Collections.Dictionary<TKey, TValue> b) =>
        Eval("a == b", a, b).AsBool();
    public static bool DictEquals<TKey, TValue>(OrderedDict<TKey, TValue> a, OrderedDict<TKey, TValue> b) where TKey : notnull
    {
        if (a.Count != b.Count)
            return false;
        EqualityComparer<TValue> eq = EqualityComparer<TValue>.Default;
        foreach (KeyValuePair<TKey, TValue> kv in a)
            if (!b.TryGetValue(kv.Key, out TValue? other) || !eq.Equals(kv.Value, other))
                return false;
        return true;
    }

    public static IEnumerable<long> range(long end)
    {
        for (long i = 0; i < end; i++)
            yield return i;
    }
    public static IEnumerable<long> range(long start, long end)
    {
        for (long i = start; i < end; i++)
            yield return i;
    }
    public static IEnumerable<long> range(long start, long end, long step)
    {
        if (step > 0)
            for (long i = start; i < end; i += step)
                yield return i;
        else if (step < 0)
            for (long i = start; i > end; i += step)
                yield return i;
    }
    public static Godot.Collections.Array range_array(long end) => ToGodotArray(range(end));
    public static Godot.Collections.Array range_array(long start, long end) => ToGodotArray(range(start, end));
    public static Godot.Collections.Array range_array(long start, long end, long step) => ToGodotArray(range(start, end, step));

    // -- Dictionary methods
    /// <summary>Dictionary.get(key) seen as a Variant: null for a missing key, as GDScript returns, not the value
    /// type's default.</summary>
    public static Variant get_variant<TKey, [MustBeVariant] TValue>(IDictionary<TKey, TValue> dict, TKey key) =>
        dict.TryGetValue(key, out TValue? value) ? Variant.From(value) : default;

    public static TValue get<TKey, TValue>(IDictionary<TKey, TValue> dict, TKey key, TValue fallback = default!) =>
        dict.TryGetValue(key, out TValue? value) ? value : fallback;
    public static TValue get<TKey, TValue>(OrderedDict<TKey, TValue> dict, TKey key, TValue fallback = default!) where TKey : notnull =>
        dict.TryGetValue(key, out TValue value) ? value : fallback;
    public static Variant get(Godot.Collections.Dictionary dict, Variant key, Variant fallback = default) =>
        dict.TryGetValue(key, out Variant value) ? value : fallback;

    public static TValue get_or_add<TKey, TValue>(IDictionary<TKey, TValue> dict, TKey key, TValue fallback)
    {
        if (dict.TryGetValue(key, out TValue? value))
            return value;
        dict[key] = fallback;
        return fallback;
    }

    public static bool has_all<TKey, TValue>(IDictionary<TKey, TValue> dict, IEnumerable<TKey> keys)
    {
        foreach (TKey key in keys)
            if (!dict.ContainsKey(key))
                return false;
        return true;
    }
    public static bool has_all(Godot.Collections.Dictionary dict, Godot.Collections.Array keys)
    {
        foreach (Variant key in keys)
            if (!dict.ContainsKey(key))
                return false;
        return true;
    }

    public static void merge<TKey, TValue>(IDictionary<TKey, TValue> dict, IEnumerable<KeyValuePair<TKey, TValue>> other, bool overwrite = false)
    {
        foreach (KeyValuePair<TKey, TValue> kv in new List<KeyValuePair<TKey, TValue>>(other))
            if (overwrite || !dict.ContainsKey(kv.Key))
                dict[kv.Key] = kv.Value;
    }
    public static void merge(Godot.Collections.Dictionary dict, Godot.Collections.Dictionary other, bool overwrite = false) => dict.Merge(other, overwrite);

    public static OrderedDict<TKey, TValue> merged<TKey, TValue>(OrderedDict<TKey, TValue> dict, IEnumerable<KeyValuePair<TKey, TValue>> other, bool overwrite = false) where TKey : notnull
    {
        var result = new OrderedDict<TKey, TValue>(dict);
        merge(result, other, overwrite);
        return result;
    }
    public static Godot.Collections.Dictionary merged(Godot.Collections.Dictionary dict, Godot.Collections.Dictionary other, bool overwrite = false)
    {
        var result = dict.Duplicate();
        result.Merge(other, overwrite);
        return result;
    }

    public static TKey find_key<TKey, TValue>(IDictionary<TKey, TValue> dict, TValue value)
    {
        EqualityComparer<TValue> eq = EqualityComparer<TValue>.Default;
        foreach (KeyValuePair<TKey, TValue> kv in dict)
            if (eq.Equals(kv.Value, value))
                return kv.Key;
        return default!;
    }

    public static long hash_dict(Godot.Collections.Dictionary dict) => (uint)GD.Hash(dict);
    public static long hash_dict<[MustBeVariant] TKey, [MustBeVariant] TValue>(OrderedDict<TKey, TValue> dict) where TKey : notnull => (uint)GD.Hash(ToGodotDictionary(dict));

    // -- enums as GDScript dictionaries
    public static List<string> enum_keys<TEnum>() where TEnum : struct, Enum => new(Enum.GetNames<TEnum>());
    public static List<long> enum_values<TEnum>() where TEnum : struct, Enum
    {
        var result = new List<long>();
        foreach (TEnum value in Enum.GetValues<TEnum>())
            result.Add(Convert.ToInt64(value));
        return result;
    }
}
