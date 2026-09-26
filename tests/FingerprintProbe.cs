using System;
using Godot;
using GdRuntime;

namespace LastCamp.Tests;

/// <summary>CPU-only content and authored-route fingerprints. Does not enter the game scene.</summary>
public partial class FingerprintProbe : SceneTree
{
    public override async void _Initialize()
    {
        await ToSignal(this, SignalName.ProcessFrame);
        int exitCode = 0;
        try
        {
            var probe = new Fingerprint();
            probe.run();
            using var json = new Json();
            if (json.Parse(FileAccess.GetFileAsString("res://tests/fixtures/content.json")) != Error.Ok)
                throw new InvalidOperationException("Invalid content fingerprint fixture");
            var expected = json.Data.AsGodotDictionary();
            if (expected.Count != probe.result.Count)
                throw new InvalidOperationException("Fingerprint set changed");
            foreach (var key in expected.Keys)
            {
                var a = expected[key].AsGodotDictionary();
                var b = probe.result[key].AsGodotDictionary();
                if (a["bytes"].AsInt64() != b["bytes"].AsInt64() || a["sha256"].AsString() != b["sha256"].AsString())
                {
                    exitCode = 1;
                    GD.PushError($"Fingerprint mismatch: {key}");
                }
            }
            GD.Print($"CONTENT_PARITY count={expected.Count} result={(exitCode == 0 ? "PASS" : "FAIL")}");
        }
        catch (Exception error) { GD.PushError(error); exitCode = 1; }
        await ToSignal(CreateTimer(0.25), SceneTreeTimer.SignalName.Timeout);
        Quit(exitCode);
    }
    public override void _Finalize() => G.drain_finalizers();
}
