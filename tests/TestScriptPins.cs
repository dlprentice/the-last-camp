using System;
using System.Linq;
using Godot;

namespace LastCamp.Tests;

/// <summary>Every RefCounted game type keeps its CSharpScript pinned (godotengine/godot#109711).</summary>
public partial class TestScriptPins : TestCase
{
    public void test_every_refcounted_game_type_is_pinned()
    {
        // The Game autoload pins at construction; when it is not loaded, pin the same way here.
        if (Game.Instance == null)
            ScriptPins.Pin();
        string[] scanned = typeof(ScriptPins).Assembly.GetTypes()
            .Where(t => typeof(RefCounted).IsAssignableFrom(t) && !t.IsAbstract && !t.ContainsGenericParameters)
            .Where(t => t.Namespace != "LastCamp.Tests" && t.Namespace != "LastCamp.Tools")
            .Select(t => t.FullName!).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        string[] listed = ScriptPins.Types.Select(t => t.FullName!).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        assert_eq(string.Join(", ", listed), string.Join(", ", scanned), "ScriptPins.Types matches the RefCounted game types");
        foreach (Type type in ScriptPins.Types)
        {
            Script? held = ScriptPins.HeldScript(type);
            assert_true(held != null && GodotObject.IsInstanceValid(held), $"{type.FullName} script is held");
            if (held == null)
                continue;
            using var instance = (RefCounted)Activator.CreateInstance(type)!;
            Script script = instance.GetScript().As<Script>();
            assert_eq(script.GetInstanceId(), held.GetInstanceId(), $"{type.FullName} instances use the held script");
        }
    }
}
