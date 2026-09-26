namespace GdRuntime;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Godot;

/// <summary>What GDScript tests did with a loaded script object (script.get(name), script.set(name, value),
/// get_script_constant_map()), for a C# class: its static fields and properties by name, through reflection.</summary>
public sealed class GdScriptRef
{
    private const BindingFlags Statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
    private readonly Type? _type;
    private readonly string? _enumNamespace;

    public GdScriptRef(Type type) => _type = type;

    private GdScriptRef(string enumNamespace) => _enumNamespace = enumNamespace;

    /// <summary>An enum container whose enums are top-level C# enums in a namespace.</summary>
    public static GdScriptRef Enums(string ns) => new(ns);

    public Variant Get(string name)
    {
        MemberInfo member = Find(name);
        object? value = member is FieldInfo f ? f.GetValue(null) : ((PropertyInfo)member).GetValue(null);
        return ToVariant(value);
    }

    public void Set(string name, Variant value)
    {
        MemberInfo member = Find(name);
        Type target = member is FieldInfo f ? f.FieldType : ((PropertyInfo)member).PropertyType;
        object? converted = FromVariant(value, target);
        if (member is FieldInfo field)
            field.SetValue(null, converted);
        else
            ((PropertyInfo)member).SetValue(null, converted);
    }

    /// <summary>Fills a static list or array in place (GDScript: script.get(name).fill(value)).</summary>
    public void Fill(string name, Variant value)
    {
        MemberInfo member = Find(name);
        object? current = member is FieldInfo f ? f.GetValue(null) : ((PropertyInfo)member).GetValue(null);
        if (current is not IList list)
            throw new InvalidOperationException($"{name} is not a list");
        Type element = current.GetType().IsArray ? current.GetType().GetElementType()! : current.GetType().GetGenericArguments()[0];
        object? item = FromVariant(value, element);
        for (int i = 0; i < list.Count; i++)
            list[i] = item;
    }

    public Godot.Collections.Dictionary GetScriptConstantMap()
    {
        var result = new Godot.Collections.Dictionary();
        IEnumerable<Type> enums = _enumNamespace != null
            ? Array.FindAll(typeof(GdScriptRef).Assembly.GetTypes(), t => t.IsEnum && !t.IsNested && t.Namespace == _enumNamespace)
            : Array.FindAll(_type!.GetNestedTypes(), t => t.IsEnum);
        foreach (Type e in enums)
        {
            var members = new Godot.Collections.Dictionary();
            foreach (string n in Enum.GetNames(e))
                members[n] = Convert.ToInt64(Enum.Parse(e, n));
            result[e.Name] = members;
        }
        if (_type != null)
            foreach (FieldInfo field in _type.GetFields(Statics))
                if (field.IsLiteral || field.IsInitOnly)
                    result[field.Name] = ToVariant(field.GetValue(null));
        return result;
    }

    private MemberInfo Find(string name)
    {
        if (_type == null)
            throw new InvalidOperationException("an enum namespace has no members");
        for (Type? t = _type; t != null; t = t.BaseType)
        {
            FieldInfo? field = t.GetField(name, Statics);
            if (field != null)
                return field;
            PropertyInfo? property = t.GetProperty(name, Statics);
            if (property != null)
                return property;
        }
        throw new MissingMemberException(_type.Name, name);
    }

    public static Variant ToVariant(object? value) => value switch
    {
        null => default,
        Variant v => v,
        bool b => b,
        long l => l,
        int i => i,
        double d => d,
        float fl => fl,
        string s => s,
        StringName sn => sn,
        NodePath np => np,
        Vector2 v2 => v2,
        Vector3 v3 => v3,
        Vector2I v2i => v2i,
        Vector3I v3i => v3i,
        Color c => c,
        Transform3D t => t,
        Basis bs => bs,
        Quaternion q => q,
        GodotObject o => o,
        Enum e => Convert.ToInt64(e),
        Godot.Collections.Array a => a,
        Godot.Collections.Dictionary dict => dict,
        List<int> li => li.ToArray(),
        List<long> ll => ll.ToArray(),
        List<float> lf => lf.ToArray(),
        List<double> ld => ld.ToArray(),
        List<byte> lb => lb.ToArray(),
        List<string> ls => ls.ToArray(),
        List<Vector2> lv2 => lv2.ToArray(),
        List<Vector3> lv3 => lv3.ToArray(),
        List<Color> lc => lc.ToArray(),
        _ when value.GetType().IsGenericType && value.GetType().Namespace == "Godot.Collections" => (Variant)value.GetType().GetMethod("op_Implicit", new[] { value.GetType() })!.Invoke(null, new[] { value })!,
        _ when DictionaryPairs(value) is { } pairs => ToDictionary(pairs),
        IList list => ToArray(list),
        _ => new GdBox(value),
    };

    private static Variant ToArray(IList list)
    {
        var array = new Godot.Collections.Array();
        foreach (object? item in list)
            array.Add(ToVariant(item));
        return array;
    }

    private static Variant ToDictionary(IEnumerable<(object? Key, object? Value)> pairs)
    {
        var dict = new Godot.Collections.Dictionary();
        foreach ((object? key, object? item) in pairs)
            dict[ToVariant(key)] = ToVariant(item);
        return dict;
    }

    /// <summary>The pairs of any generic dictionary (Dictionary, OrderedDict), in its own order; null otherwise.</summary>
    private static IEnumerable<(object? Key, object? Value)>? DictionaryPairs(object value)
    {
        bool generic = false;
        foreach (Type iface in value.GetType().GetInterfaces())
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                generic = true;
        if (!generic || value is not IEnumerable entries)
            return null;
        var pairs = new List<(object?, object?)>();
        foreach (object? entry in entries)
        {
            Type entryType = entry!.GetType();
            pairs.Add((entryType.GetProperty("Key")!.GetValue(entry), entryType.GetProperty("Value")!.GetValue(entry)));
        }
        return pairs;
    }

    public static object? FromVariant(Variant value, Type target)
    {
        if (target == typeof(Variant))
            return value;
        if (target == typeof(long))
            return value.AsInt64();
        if (target == typeof(int))
            return value.AsInt32();
        if (target == typeof(double))
            return value.AsDouble();
        if (target == typeof(float))
            return value.AsSingle();
        if (target == typeof(bool))
            return value.AsBool();
        if (target == typeof(string))
            return value.AsString();
        if (target == typeof(StringName))
            return value.AsStringName();
        if (target == typeof(Vector3))
            return value.AsVector3();
        if (target == typeof(Vector2))
            return value.AsVector2();
        if (target == typeof(Color))
            return value.AsColor();
        if (target.IsEnum)
            return Enum.ToObject(target, value.AsInt64());
        if (target == typeof(List<int>))
            return new List<int>(value.AsInt32Array());
        if (target == typeof(List<long>))
            return new List<long>(value.AsInt64Array());
        if (target == typeof(List<float>))
            return new List<float>(value.AsFloat32Array());
        if (target == typeof(List<double>))
            return new List<double>(value.AsFloat64Array());
        if (target == typeof(List<byte>))
            return new List<byte>(value.AsByteArray());
        if (target == typeof(List<string>))
            return new List<string>(value.AsStringArray());
        if (target == typeof(List<Vector3>))
            return new List<Vector3>(value.AsVector3Array());
        if (target == typeof(Godot.Collections.Array))
            return value.AsGodotArray();
        if (target == typeof(Godot.Collections.Dictionary))
            return value.AsGodotDictionary();
        if (typeof(GodotObject).IsAssignableFrom(target))
            return value.AsGodotObject();
        if (target.IsGenericType && target.Namespace == "Godot.Collections")
            return Activator.CreateInstance(target, value.VariantType == Variant.Type.Dictionary ? value.AsGodotDictionary() : value.AsGodotArray());
        return value.Obj;
    }
}
