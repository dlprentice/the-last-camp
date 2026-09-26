namespace GdRuntime;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>Operations on values of unknown type (GDScript's untyped variables and container elements): indexing,
/// operators, calls and iteration with GDScript's rules. Numbers take a C# fast path; everything else is evaluated by
/// the engine's own Variant operators.</summary>
public static partial class G
{
    public static Variant op(string op, Variant a, Variant b)
    {
        Variant.Type ta = a.VariantType, tb = b.VariantType;
        if (ta == Variant.Type.Int && tb == Variant.Type.Int)
        {
            long x = a.AsInt64(), y = b.AsInt64();
            switch (op)
            {
                case "+": return x + y;
                case "-": return x - y;
                case "*": return x * y;
                case "<": return x < y;
                case ">": return x > y;
                case "<=": return x <= y;
                case ">=": return x >= y;
                case "==": return x == y;
                case "!=": return x != y;
            }
        }
        else if ((ta == Variant.Type.Float || ta == Variant.Type.Int) && (tb == Variant.Type.Float || tb == Variant.Type.Int))
        {
            double x = a.AsDouble(), y = b.AsDouble();
            switch (op)
            {
                case "+": return x + y;
                case "-": return x - y;
                case "*": return x * y;
                case "/": return x / y;
                case "<": return x < y;
                case ">": return x > y;
                case "<=": return x <= y;
                case ">=": return x >= y;
                case "==": return x == y;
                case "!=": return x != y;
            }
        }
        return Eval("a " + op + " b", a, b);
    }

    public static bool eq(Variant a, Variant b)
    {
        Variant.Type ta = a.VariantType, tb = b.VariantType;
        if (ta == Variant.Type.Object && !GodotObject.IsInstanceValid(a.AsGodotObject())) ta = Variant.Type.Nil;
        if (tb == Variant.Type.Object && !GodotObject.IsInstanceValid(b.AsGodotObject())) tb = Variant.Type.Nil;
        if (ta == Variant.Type.Nil || tb == Variant.Type.Nil)
            return ta == tb;
        if (ta == Variant.Type.Int && tb == Variant.Type.Int)
            return a.AsInt64() == b.AsInt64();
        if ((ta == Variant.Type.Float || ta == Variant.Type.Int) && (tb == Variant.Type.Float || tb == Variant.Type.Int))
            return a.AsDouble() == b.AsDouble();
        if (ta == Variant.Type.String && tb == Variant.Type.String)
            return a.AsString() == b.AsString();
        if (ta == Variant.Type.Object && tb == Variant.Type.Object)
            return a.AsGodotObject() == b.AsGodotObject();
        return Eval("a == b", a, b).AsBool();
    }

    public static Variant neg(Variant a) => a.VariantType switch
    {
        Variant.Type.Int => -a.AsInt64(),
        Variant.Type.Float => -a.AsDouble(),
        _ => Eval("-a", a),
    };

    /// <summary>container[key] for a container of unknown type (also obj.property on an untyped object).</summary>
    public static Variant Index(Variant container, Variant key)
    {
        switch (container.VariantType)
        {
            case Variant.Type.Dictionary:
                return container.AsGodotDictionary()[key];
            case Variant.Type.Array:
                {
                    var array = container.AsGodotArray();
                    int i = (int)key.AsInt64();
                    return array[i < 0 ? i + array.Count : i];
                }
            case Variant.Type.Object:
                GodotObject? obj = container.AsGodotObject();
                return obj != null ? get_member(obj, key.AsString()) : Eval("a[b]", container, key);
            case Variant.Type.Vector2 or Variant.Type.Vector3 or Variant.Type.Vector4 or Variant.Type.Color or Variant.Type.Quaternion:
                if (Component(container, key) is double component)
                    return component;
                break;
        }
        return Eval("a[b]", container, key);
    }

    /// <summary>A vector's, colour's or quaternion's component by name (x, y, z, w; r, g, b, a) or index, as a double
    /// (the float32 component widened, as GDScript reads it); null when the key is anything else.</summary>
    private static double? Component(Variant container, Variant key)
    {
        int index = -1;
        if (key.VariantType == Variant.Type.Int)
            index = (int)key.AsInt64();
        else if (key.VariantType is Variant.Type.String or Variant.Type.StringName)
        {
            string name = key.AsString();
            bool color = container.VariantType == Variant.Type.Color;
            index = name switch
            {
                "x" when !color => 0,
                "y" when !color => 1,
                "z" when !color => 2,
                "w" when !color => 3,
                "r" when color => 0,
                "g" when color => 1,
                "b" when color => 2,
                "a" when color => 3,
                _ => -1,
            };
        }
        switch (container.VariantType)
        {
            case Variant.Type.Vector2 when index is >= 0 and < 2:
                return container.AsVector2()[index];
            case Variant.Type.Vector3 when index is >= 0 and < 3:
                return container.AsVector3()[index];
            case Variant.Type.Vector4 when index is >= 0 and < 4:
                return container.AsVector4()[index];
            case Variant.Type.Color when index is >= 0 and < 4:
                return container.AsColor()[index];
            case Variant.Type.Quaternion when index is >= 0 and < 4:
                return container.AsQuaternion()[index];
        }
        return null;
    }

    /// <summary>container[key] = value; returns the container, which for packed arrays and vectors is a new copy the
    /// caller writes back.</summary>
    public static Variant SetIndex(Variant container, Variant key, Variant value)
    {
        switch (container.VariantType)
        {
            case Variant.Type.Dictionary:
                container.AsGodotDictionary()[key] = value;
                return container;
            case Variant.Type.Array:
                {
                    var array = container.AsGodotArray();
                    int i = (int)key.AsInt64();
                    array[i < 0 ? i + array.Count : i] = value;
                    return container;
                }
            case Variant.Type.Object:
                container.AsGodotObject().Set(key.AsStringName(), value);
                return container;
        }
        throw new NotSupportedException($"Indexed assignment is not supported for {container.VariantType}");
    }

    public static Variant Call(Variant target, string method, params Variant[] args)
    {
        if (target.VariantType == Variant.Type.Object)
        {
            GodotObject obj = target.AsGodotObject();
            if (!GodotObject.IsInstanceValid(obj))
                throw new InvalidOperationException($"Cannot call {method} on a freed or null object");
            if (method == "get" && args.Length == 1)
                return get_member(obj, args[0].AsString());
            if (method == "set" && args.Length == 2)
            {
                set_member(obj, args[0].AsString(), args[1]);
                return default;
            }
            return obj.Call(method, args);
        }
        if (target.VariantType == Variant.Type.Array)
        {
            var array = target.AsGodotArray();
            switch (method)
            {
                case "append" or "push_back" when args.Length == 1:
                    array.Add(args[0]);
                    return default;
                case "append_array" when args.Length == 1:
                    foreach (Variant item in Iter(args[0]))
                        array.Add(item);
                    return default;
                case "size" when args.Length == 0:
                    return array.Count;
                case "is_empty" when args.Length == 0:
                    return array.Count == 0;
                case "clear" when args.Length == 0:
                    array.Clear();
                    return default;
                case "has" when args.Length == 1:
                    return array.Contains(args[0]);
                case "erase" when args.Length == 1:
                    array.Remove(args[0]);
                    return default;
                case "remove_at" when args.Length == 1:
                    array.RemoveAt((int)args[0].AsInt64());
                    return default;
                case "insert" when args.Length == 2:
                    array.Insert((int)args[0].AsInt64(), args[1]);
                    return default;
                case "back" when args.Length == 0:
                    return array.Count == 0 ? default : array[array.Count - 1];
                case "front" when args.Length == 0:
                    return array.Count == 0 ? default : array[0];
            }
        }
        if (target.VariantType == Variant.Type.Dictionary)
        {
            var dict = target.AsGodotDictionary();
            switch (method)
            {
                case "has" when args.Length == 1:
                    return dict.ContainsKey(args[0]);
                case "get" when args.Length is 1 or 2:
                    return dict.TryGetValue(args[0], out Variant value) ? value : (args.Length == 2 ? args[1] : default);
                case "erase" when args.Length == 1:
                    return dict.Remove(args[0]);
                case "size" when args.Length == 0:
                    return dict.Count;
                case "is_empty" when args.Length == 0:
                    return dict.Count == 0;
                case "keys" when args.Length == 0:
                    return new Godot.Collections.Array(dict.Keys);
                case "values" when args.Length == 0:
                    return new Godot.Collections.Array(dict.Values);
                case "clear" when args.Length == 0:
                    dict.Clear();
                    return default;
            }
        }
        var inputs = new Variant[args.Length + 1];
        inputs[0] = target;
        var names = new System.Text.StringBuilder();
        for (int i = 0; i < args.Length; i++)
        {
            inputs[i + 1] = args[i];
            names.Append(i == 0 ? "" : ", ").Append(i + 1 < 3 ? ((char)('a' + i + 1)).ToString() : $"x{i + 1}");
        }
        return Eval($"a.{method}({names})", inputs);
    }

    public static IEnumerable<Variant> Iter(Variant value)
    {
        switch (value.VariantType)
        {
            case Variant.Type.Array:
                foreach (Variant v in value.AsGodotArray())
                    yield return v;
                yield break;
            case Variant.Type.Dictionary:
                foreach (Variant k in value.AsGodotDictionary().Keys)
                    yield return k;
                yield break;
            case Variant.Type.Int:
                for (long i = 0; i < value.AsInt64(); i++)
                    yield return i;
                yield break;
            case Variant.Type.String:
                foreach (char c in value.AsString())
                    yield return c.ToString();
                yield break;
        }
        foreach (Variant v in Eval("Array(a)", value).AsGodotArray())
            yield return v;
    }

    public static bool In(Variant item, Variant container) => container.VariantType switch
    {
        Variant.Type.Array => container.AsGodotArray().Contains(item),
        Variant.Type.Dictionary => container.AsGodotDictionary().ContainsKey(item),
        Variant.Type.String => container.AsString().Contains(item.AsString()),
        _ => Eval("a in b", item, container).AsBool(),
    };

    public static long len(Variant value) => value.VariantType switch
    {
        Variant.Type.Array => value.AsGodotArray().Count,
        Variant.Type.Dictionary => value.AsGodotDictionary().Count,
        Variant.Type.String or Variant.Type.StringName or Variant.Type.NodePath => value.AsString().Length,
        _ => Eval("a.size()", value).AsInt64(),
    };

    public static bool is_instance_of(Variant value, System.Type type) => value.Obj != null && type.IsInstanceOfType(value.Obj);

    // -- callables as C# delegates (an empty Callable becomes null, as the C# APIs expect for "none")
    public static Func<TResult>? Func<[MustBeVariant] TResult>(Callable c) => c.Target == null && c.Delegate == null ? null : () => c.Call().As<TResult>();
    public static Func<T1, TResult>? Func<[MustBeVariant] T1, [MustBeVariant] TResult>(Callable c) =>
        c.Target == null && c.Delegate == null ? null : (a) => c.Call(Variant.From(a)).As<TResult>();
    public static Func<T1, T2, TResult>? Func<[MustBeVariant] T1, [MustBeVariant] T2, [MustBeVariant] TResult>(Callable c) =>
        c.Target == null && c.Delegate == null ? null : (a, b) => c.Call(Variant.From(a), Variant.From(b)).As<TResult>();
    public static Action? Action(Callable c) => c.Target == null && c.Delegate == null ? null : () => c.Call();
    public static Action<T1>? Action<[MustBeVariant] T1>(Callable c) => c.Target == null && c.Delegate == null ? null : (a) => c.Call(Variant.From(a));
    public static Action<T1, T2>? Action<[MustBeVariant] T1, [MustBeVariant] T2>(Callable c) =>
        c.Target == null && c.Delegate == null ? null : (a, b) => c.Call(Variant.From(a), Variant.From(b));

    // -- callables
    public static Variant callv(Callable callable, Godot.Collections.Array args)
    {
        var list = new Variant[args.Count];
        for (int i = 0; i < list.Length; i++)
            list[i] = args[i];
        return callable.Call(list);
    }

    /// <summary>Callable.bind(): a C# callable that calls the target with the call's arguments, then the values. The
    /// engine's own bound callable cannot be used here: Godot marshals any custom callable but its own C# delegates
    /// into C# as a null Callable.</summary>
    public static Callable bind(Callable callable, params Variant[] values)
    {
        Variant[] bound = (Variant[])values.Clone();
        return Variadic(args =>
        {
            var all = new Variant[args.Length + bound.Length];
            args.CopyTo(all, 0);
            bound.CopyTo(all, args.Length);
            return callable.Call(all);
        });
    }

    /// <summary>Callable.unbind(): a C# callable that drops the call's last count arguments.</summary>
    public static Callable unbind(Callable callable, int count) =>
        Variadic(args => callable.Call(args[..Math.Max(0, args.Length - count)]));

    /// <summary>source.signal.connect(callable.bind(values), flags) through the engine, which keeps its own bound
    /// callable: a scene built in code and packed keeps the connection and its binds, as its .tscn did.</summary>
    public static Error connect_bound(GodotObject source, StringName signal, Callable callable, uint flags, params Variant[] values)
    {
        var inputs = new Variant[values.Length + 1];
        inputs[0] = callable;
        var args = new System.Text.StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            inputs[i + 1] = values[i];
            args.Append(i == 0 ? "" : ", ").Append(i + 1 < 3 ? ((char)('a' + i + 1)).ToString() : $"x{i + 1}");
        }
        Variant bound = Eval($"a.bind({args})", inputs);
        return (Error)source.Call(GodotObject.MethodName.Connect, signal, bound, flags).AsInt64();
    }
    public static bool is_valid(Callable callable) => Eval("a.is_valid()", callable).AsBool();
}
