#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GdRuntime;
using static GdRuntime.G;
using Environment = Godot.Environment;
using Range = Godot.Range;
using LastCamp;
using LastCamp.Construction;

namespace LastCamp;

/// Instantiates the planned boulders. Large rocks are individual meshes with
/// collision; shore pebbles share one MultiMesh.
public partial class CampRocks : Node3D
{
    public const double COLLIDE_SCALE = 0.55;

    public TerrainField field;
    public ScenePlan plan;
    public Godot.Collections.Array<ArrayMesh> variants = new Godot.Collections.Array<ArrayMesh>();
    public ShaderMaterial material;

    public CampRocks(TerrainField p_field, ScenePlan p_plan)
    {
        field = p_field;
        plan = p_plan;
        Name = "Rocks";
    }

    public CampRocks()
    {
    }

    public void build()
    {
        material = new ShaderMaterial();
        material.Shader = Content.Load<Shader>("res://shaders/prop.gdshader");
        Camp.bind_prop_pbr(material, "rock");
        Camp.bind_texture(material, "noise_tex", "res://textures/noise_rgba.png");
        material.SetShaderParameter("tile", 0.45);
        material.SetShaderParameter("moss_amount", 0.4);
        material.SetShaderParameter("tint", new Color(0.92f, 0.9f, 0.86f));
        for (long i = 0; i < 6; i++)
        {
            variants.Add(PropMeshes.rock(1400 + i * 31, 1.0));
        }
        Godot.Collections.Array<Transform3D> pebbles = new Godot.Collections.Array<Transform3D>();
        foreach (ScenePlan.RockEntry entry in plan.rocks)
        {
            if (entry.scale < 0.45)
            {
                pebbles.Add(_xform(entry));
            }
            else
            {
                _place_boulder(entry);
            }
        }
        if (!(pebbles.Count == 0))
        {
            _add_pebbles(pebbles);
        }
    }

    public Transform3D _xform(ScenePlan.RockEntry entry)
    {
        double y = field.height(entry.position.X, entry.position.Y) - entry.sink * entry.scale * 0.35;
        Basis basis = new Basis(Vector3.Up, (float)entry.rotation).Scaled(Vector3.One * (float)entry.scale);
        return new Transform3D(basis, new Vector3(entry.position.X, (float)y, entry.position.Y));
    }

    public void _place_boulder(ScenePlan.RockEntry entry)
    {
        MeshInstance3D mesh = new MeshInstance3D();
        mesh.Mesh = variants[(int)(entry.variant % (long)variants.Count)];
        mesh.MaterialOverride = material;
        mesh.Transform = _xform(entry);
        mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        mesh.GIMode = GeometryInstance3D.GIModeEnum.Static;
        AddChild(mesh);
        StaticBody3D body = new StaticBody3D();
        body.CollisionLayer = unchecked((uint)(1));
        body.CollisionMask = unchecked((uint)(0));
        body.SetMeta("surface", (StringName)"rock");
        CollisionShape3D shape = new CollisionShape3D();
        SphereShape3D sphere = new SphereShape3D();
        sphere.Radius = (float)(entry.scale * COLLIDE_SCALE);
        shape.Shape = sphere;
        Vector3 _t1 = shape.Position;
        _t1.Y = (float)(entry.scale * 0.2);
        shape.Position = _t1;
        body.AddChild(shape);
        body.Position = mesh.Position;
        AddChild(body);
    }

    public void _add_pebbles(Godot.Collections.Array<Transform3D> transforms)
    {
        MultiMesh mm = new MultiMesh();
        mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        mm.Mesh = variants[0];
        mm.InstanceCount = (int)(long)transforms.Count;
        for (long i = 0, i_end = (long)transforms.Count; i < i_end; i++)
        {
            mm.SetInstanceTransform((int)i, transforms[(int)i]);
        }
        MultiMeshInstance3D mmi = new MultiMeshInstance3D();
        mmi.Name = "Pebbles";
        mmi.Multimesh = mm;
        mmi.MaterialOverride = material;
        mmi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        mmi.GIMode = GeometryInstance3D.GIModeEnum.Static;
        AddChild(mmi);
    }
}
