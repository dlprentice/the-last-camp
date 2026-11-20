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
            string[] userArgs = OS.GetCmdlineUserArgs();
            string? dumpPath = null;
            bool writeFixture = false;
            foreach (string arg in userArgs)
            {
                if (arg == "--write") writeFixture = true;
                else if (arg.StartsWith("--dump=", StringComparison.Ordinal)) dumpPath = arg["--dump=".Length..];
            }
            if (dumpPath != null)
            {
                using var f = FileAccess.Open(dumpPath, FileAccess.ModeFlags.Write);
                f.StoreString(Json.Stringify(probe.result, "  ", true));
                GD.Print($"CONTENT_DUMP path={dumpPath} count={probe.result.Count}");
            }
            if (writeFixture)
            {
                using var f = FileAccess.Open("res://tests/fixtures/content.json", FileAccess.ModeFlags.Write);
                f.StoreString(Json.Stringify(probe.result, "  ", true) + "\n");
                GD.Print($"CONTENT_WROTE count={probe.result.Count}");
            }
            using var json = new Json();
            if (json.Parse(FileAccess.GetFileAsString("res://tests/fixtures/content.json")) != Error.Ok)
                throw new InvalidOperationException("Invalid content fingerprint fixture");
            var expected = json.Data.AsGodotDictionary();
            if (expected.Count != probe.result.Count)
                throw new InvalidOperationException($"Fingerprint set changed (expected {expected.Count}, got {probe.result.Count})");
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
