using System;
using System.Collections.Generic;
using Godot;

namespace LastCamp;

/// Only nearby ridge trunks need physics bodies. Their positions and dimensions
/// come from the rendered instances, while distant trees remain render data.
public partial class WoodlandCollision : Node3D
{
    private const float CellSize = 32;
    private readonly record struct Trunk(Vector3 Base, float Radius, float Height);
    private readonly Dictionary<Vector2I, List<Trunk>> _cells = new();
    private readonly Dictionary<Vector2I, List<StaticBody3D>> _active = new();
    private readonly Stack<StaticBody3D> _pool = new();
    private Vector2I _last = new(int.MaxValue, int.MaxValue);
    public int ActiveBodies { get; private set; }

    public WoodlandCollision() { Name = "WoodlandCollision"; }
    public WoodlandCollision(Forest forest) : this()
    {
        foreach (var group in forest.ridge_groups)
            foreach (Transform3D transform in group.Transforms)
            {
                Vector2I cell = CellAt(transform.Origin);
                if (!_cells.TryGetValue(cell, out var trunks)) _cells[cell] = trunks = new();
                float radius = (float)group.Mesh.trunk_radius * Math.Max(transform.Basis.X.Length(), transform.Basis.Z.Length()) * 1.15f;
                float height = Math.Min((float)Forest.TRUNK_COLLISION_HEIGHT, (float)group.Mesh.height * transform.Basis.Y.Length());
                trunks.Add(new Trunk(transform.Origin, Math.Max(radius, 0.12f), height));
            }
    }

    public static Vector2I CellAt(Vector3 position) => new(Mathf.FloorToInt(position.X / CellSize), Mathf.FloorToInt(position.Z / CellSize));

    public override void _PhysicsProcess(double delta)
    {
        if (Game.Instance?.player is not { enabled: true } player || Game.Instance.mode != Game.Mode.PLAY) return;
        Refresh(player.GlobalPosition);
    }

    public void Refresh(Vector3 position)
    {
        Vector2I centre = CellAt(position);
        if (centre == _last) return;
        _last = centre;
        var old = new List<Vector2I>();
        foreach (var (cell, bodies) in _active)
        {
            if (Math.Abs(cell.X - centre.X) <= 1 && Math.Abs(cell.Y - centre.Y) <= 1) continue;
            old.Add(cell);
            foreach (var body in bodies)
            {
                body.CollisionLayer = 0;
                _pool.Push(body);
                ActiveBodies--;
            }
        }
        foreach (var cell in old) _active.Remove(cell);
        for (int z = -1; z <= 1; z++)
            for (int x = -1; x <= 1; x++)
            {
                Vector2I cell = centre + new Vector2I(x, z);
                if (_active.ContainsKey(cell) || !_cells.TryGetValue(cell, out var trunks)) continue;
                var bodies = new List<StaticBody3D>(trunks.Count);
                _active.Add(cell, bodies);
                foreach (var trunk in trunks)
                {
                    StaticBody3D body;
                    if (_pool.Count > 0) body = _pool.Pop();
                    else
                    {
                        body = new StaticBody3D { CollisionMask = 0 };
                        body.SetMeta("surface", "wood");
                        body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D() });
                        AddChild(body);
                    }
                    var collider = body.GetChild<CollisionShape3D>(0);
                    var shape = (CylinderShape3D)collider.Shape;
                    shape.Radius = trunk.Radius;
                    shape.Height = trunk.Height;
                    collider.Position = Vector3.Up * (trunk.Height * 0.5f);
                    body.Position = trunk.Base;
                    body.CollisionLayer = 1;
                    bodies.Add(body);
                    ActiveBodies++;
                }
            }
    }
}
