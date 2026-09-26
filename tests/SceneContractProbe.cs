using System;
using Godot;
using GdRuntime;

namespace LastCamp.Tests;

/// <summary>Compares the code-built scene to the canonical original, without entering the tree.</summary>
public partial class SceneContractProbe : SceneTree
{
    public override async void _Initialize()
    {
        await ToSignal(this, SignalName.ProcessFrame);
        int result = 1;
        try
        {
            using var scene = GD.Load<PackedScene>("res://scenes/main.tscn");
            string actual = SceneCanon.Scene(scene);
            string expected = FileAccess.GetFileAsString("res://tests/fixtures/main.scene.txt");
            using var output = FileAccess.Open("res://local-data/main.scene.txt", FileAccess.ModeFlags.Write);
            output.StoreString(actual);
            result = actual == expected ? 0 : 1;
            GD.Print($"SCENE_CONTRACT {(result == 0 ? "PASS" : "FAIL")} {SceneCanon.Hash(actual)}");
        }
        catch (Exception error) { GD.PushError(error); }
        Quit(result);
    }
    public override void _Finalize() => G.drain_finalizers();
}
