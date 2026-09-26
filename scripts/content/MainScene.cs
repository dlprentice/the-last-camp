using Godot;

namespace LastCamp.Construction;

/// <summary>The complete entry scene, built in the original child-ready order.</summary>
public static class MainScene
{
    public static void BuildInto(Node root)
    {
        Node[] children =
        {
            new WorldController { Name = "World" },
            new Camp { Name = "Camp" },
            new TerrainFieldDirector { Name = "TerrainFieldDirector" },
            new BiomeDressing { Name = "BiomeDressing" },
            new ForestFloorDressing { Name = "ForestFloorDressing" },
            new Player { Name = "Player" },
            new IntroDolly { Name = "Intro" },
            new Hud { Name = "HUD" },
            new LoadingScreen { Name = "Loading" },
        };
        foreach (Node child in children)
        {
            root.AddChild(child);
            child.Owner = root;
        }
    }
}
