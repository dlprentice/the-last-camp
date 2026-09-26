namespace GdRuntime;

using System;
using Godot;
using Godot.NativeInterop;

/// <summary>Callables that take any number of arguments, as a GDScript function with default parameters does: the
/// target reads what it was given and fills the rest with its defaults. Callable.From checks the count exactly.</summary>
public static partial class G
{
    public static unsafe Callable Variadic(Func<Variant[], Variant> target) =>
        Callable.CreateWithUnsafeTrampoline(target, &VariadicTrampoline);

    public static unsafe Callable VariadicAction(Action<Variant[]> target) =>
        Callable.CreateWithUnsafeTrampoline(target, &VariadicActionTrampoline);

    private static void VariadicTrampoline(object delegateObj, NativeVariantPtrArgs args, out godot_variant ret)
    {
        var values = new Variant[args.Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = Variant.CreateCopyingBorrowed(args[i]);
        ret = ((Func<Variant[], Variant>)delegateObj)(values).CopyNativeVariant();
    }

    private static void VariadicActionTrampoline(object delegateObj, NativeVariantPtrArgs args, out godot_variant ret)
    {
        var values = new Variant[args.Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = Variant.CreateCopyingBorrowed(args[i]);
        ((Action<Variant[]>)delegateObj)(values);
        ret = default;
    }
}
