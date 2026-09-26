using System;
using System.Linq;
using Godot;
using GdRuntime;

namespace LastCamp.Tests;

/// <summary>Runs every TestCase with the original assertions and alphabetical ordering.</summary>
public partial class RunTests : SceneTree
{
    public override async void _Initialize()
    {
        await ToSignal(this, SignalName.ProcessFrame);
        int passed = 0, failed = 0;
        var cases = typeof(TestCase).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(TestCase)) && !t.IsAbstract)
            .OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
        foreach (Type type in cases)
        {
            ulong start = Time.GetTicksMsec();
            try
            {
                using var test = (TestCase)Activator.CreateInstance(type)!;
                using var result = test.run();
                int p = result["passed"].AsInt32(), f = result["failed"].AsInt32();
                passed += p;
                failed += f;
                GD.Print($"{type.Name}: {p} passed, {f} failed ({Time.GetTicksMsec() - start} ms)");
                foreach (Variant failure in result["failures"].AsGodotArray())
                    GD.Print("  FAIL ", failure.AsString());
            }
            catch (Exception error)
            {
                failed++;
                GD.PushError(type.Name, ": ", error);
            }
        }
        if (cases.Length == 0 || passed + failed == 0)
        {
            failed++;
            GD.PushError("No tests discovered");
        }
        GD.Print($"{(failed == 0 ? "OK" : "FAILED")}: {passed} passed, {failed} failed");
        await ToSignal(CreateTimer(0.25), SceneTreeTimer.SignalName.Timeout);
        Quit(failed == 0 ? 0 : 1);
    }

    public override void _Finalize() => G.drain_finalizers();
}
