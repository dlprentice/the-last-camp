#nullable enable
using System.Collections.Concurrent;
using Godot;

namespace LastCamp;

/// Long-lived arguments for native calls. Since Godot 4.8 beta2 the C# bindings keep only
/// `this` alive across a call (godotengine/godot#125198): a StringName converted from a
/// string literal, or a default collection argument, can be finalized while the engine reads
/// it. Hot paths take their names and empty collections from here instead.
public static class NativeArgs
{
    private static readonly ConcurrentDictionary<string, StringName> Names = new();

    /// An empty, read-only exclude list for PhysicsRayQueryParameters3D.Create.
    public static readonly Godot.Collections.Array<Rid> NoRids = ReadOnlyRids();

    /// The interned StringName for a name, created once and never released.
    public static StringName Name(string name) => Names.GetOrAdd(name, static n => new StringName(n));

    private static Godot.Collections.Array<Rid> ReadOnlyRids()
    {
        var rids = new Godot.Collections.Array<Rid>();
        rids.MakeReadOnly();
        return rids;
    }
}
