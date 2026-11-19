using System;
using System.Collections.Generic;
using Godot;
using GdRuntime;

namespace LastCamp;

/// <summary>Holds the CSharpScript of every RefCounted-derived game type for the whole session.</summary>
/// <remarks>
/// Works around godotengine/godot#109711: when the .NET finalizer frees the last live instance of a C#
/// RefCounted type, the type's CSharpScript is destroyed on the finalizer thread while the main thread may
/// be creating a new instance, which aborts with <c>Condition "script.is_null()" is true</c>. A script that
/// is referenced here is never destroyed, so that race cannot happen. Nested types have no script path, so
/// each script is taken from one short-lived instance rather than loaded by path. Every type is pinned: it
/// costs one small object per type at startup. Tests check this list against a reflection scan.
/// </remarks>
public static class ScriptPins
{
    public static readonly Type[] Types =
    {
        typeof(AudioDirector.FootstepSet),
        typeof(AudioDirector.SmoothedValue),
        typeof(GdBox),
        typeof(GrassPlanter.Chunk),
        typeof(MeshBuilder),
        typeof(PondSimulation),
        typeof(QualityPreset),
        typeof(ScannedDressing.ScanVariant),
        typeof(ScenePlan.TreeEntry),
    };

    private static readonly Dictionary<Type, Script> Held = new();

    /// <summary>Pins every listed script. Idempotent; call on the main thread.</summary>
    public static void Pin()
    {
        foreach (Type type in Types)
        {
            if (Held.ContainsKey(type))
                continue;
            using var instance = (RefCounted)Activator.CreateInstance(type)!;
            Held[type] = instance.GetScript().As<Script>();
        }
    }

    /// <summary>The pinned script for a type, or null when it is not held.</summary>
    public static Script? HeldScript(Type type) => Held.TryGetValue(type, out Script? script) ? script : null;
}
