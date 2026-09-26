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

/// Ecological composition pass layered on top of the established generated scene.
/// It does not replace the terrain/forest planners. It adds visual mass where the
/// current composition reads sparse, and gives the pond a moisture-driven margin.
/// All placement is deterministic and uses existing meshes/materials.
public partial class BiomeDressing : Node3D
{
    public const long CANOPY_TARGET = 190;
    public const long CANOPY_ATTEMPTS = 4200;
    public const double CANOPY_MIN_RADIUS = 82.0;
    public const double CANOPY_MAX_RADIUS = 205.0;
    public const double CANOPY_MIN_SPACING = 4.2;
    public const long SEDGE_ATTEMPTS = 3600;
    public const long RUSH_ATTEMPTS = 1500;
    public const long WET_SHRUB_ATTEMPTS = 650;

    public Camp _camp;
    public TerrainField _field;
    public Forest _forest;
    public Understory _understory;
    public bool _built = false;

    public override void _Ready()
    {
        Name = "BiomeDressing";
        SetProcess(false);
    }

    public void setup(Camp camp)
    {
        if (_built)
        {
            return;
        }
        _camp = camp;
        _field = camp.field;
        _forest = camp.forest;
        _understory = camp.understory;
        _build();
        _built = true;
    }

    public void _build()
    {
        _build_sedge_margin();
        _build_rush_pockets();
        _build_wet_shrubs();
    }

    public void _build_sedge_margin()
    {
        // ---------------------------------------------------------- shoreline zoning
        // Low fountain-form sedges occupy saturated soil from just below the water
        // line to the damp bank. This broad layer is intentionally denser than reeds.
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(88031));
        Godot.Collections.Array<Transform3D> transforms = new Godot.Collections.Array<Transform3D>();
        Godot.Collections.Array<Color> customs = new Godot.Collections.Array<Color>();
        Godot.Collections.Array<Color> colors = new Godot.Collections.Array<Color>();
        for (long attempt = 0; attempt < SEDGE_ATTEMPTS; attempt++)
        {
            double angle = rng.Randf() * TAU;
            Vector2 shore = TerrainField.shore_point(angle);
            Vector2 radial = (shore - TerrainField.POND_CENTRE).Normalized();
            Vector2 tangent = new Vector2(-radial.Y, radial.X);
            Vector2 p = shore + radial * rng.RandfRange(-0.30f, 2.45f) + tangent * rng.RandfRange(-1.3f, 1.3f);
            if (_shore_navigation_blocked(p, 1.3))
            {
                continue;
            }
            double y = _field.height_fast(p.X, p.Y);
            double depth = TerrainField.WATER_LEVEL - y;
            if (depth > 0.20 || depth < -0.58)
            {
                continue;
            }
            double moisture = smoothstep(-0.58, -0.05, depth);
            if (rng.Randf() > lerpf(0.42, 0.92, moisture))
            {
                continue;
            }
            double width = rng.RandfRange(0.42f, 0.95f);
            double height = rng.RandfRange(0.42f, 0.92f) * lerpf(1.08, 0.78, maxf(depth, 0.0) / 0.20);
            double yaw = rng.Randf() * TAU;
            transforms.Add(new Transform3D(new Basis(Vector3.Up, (float)yaw).Scaled(new Vector3((float)width, (float)height, (float)width)), new Vector3(p.X, (float)(y - 0.025), p.Y)));
            customs.Add(new Color(rng.Randf(), (float)(cos(yaw) * 0.5 + 0.5), (float)(sin(yaw) * 0.5 + 0.5), rng.RandfRange(0.08f, 0.24f)));
            colors.Add(new Color(rng.RandfRange(0.78f, 0.96f), rng.RandfRange(0.92f, 1.08f), rng.RandfRange(0.72f, 0.92f), rng.RandfRange(0.0f, 0.30f)));
        }
        _add_margin_multimesh("WetSedges", GrassPlanter.clump_mesh(9, 2, 0.035, 917), _wet_grass_material(false), transforms, customs, colors);
    }

    public void _build_rush_pockets()
    {
        // Taller emergents occur as broken colonies, not an even ring around the pond.
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(88067));
        Godot.Collections.Array<Transform3D> transforms = new Godot.Collections.Array<Transform3D>();
        Godot.Collections.Array<Color> customs = new Godot.Collections.Array<Color>();
        Godot.Collections.Array<Color> colors = new Godot.Collections.Array<Color>();
        for (long attempt = 0; attempt < RUSH_ATTEMPTS; attempt++)
        {
            double angle = rng.Randf() * TAU;
            double colony = 0.5 + 0.5 * sin(angle * 5.0 + 1.7) * sin(angle * 2.0 - 0.8);
            if (rng.Randf() > smoothstep(0.35, 0.76, colony))
            {
                continue;
            }
            Vector2 shore = TerrainField.shore_point(angle);
            Vector2 radial = (shore - TerrainField.POND_CENTRE).Normalized();
            Vector2 tangent = new Vector2(-radial.Y, radial.X);
            Vector2 p = shore + radial * rng.RandfRange(-0.42f, 0.72f) + tangent * rng.RandfRange(-1.8f, 1.8f);
            if (_shore_navigation_blocked(p, 1.8))
            {
                continue;
            }
            double y = _field.height_fast(p.X, p.Y);
            double depth = TerrainField.WATER_LEVEL - y;
            if (depth < -0.12 || depth > 0.38)
            {
                continue;
            }
            double width = rng.RandfRange(0.62f, 1.08f);
            double height = rng.RandfRange(0.95f, 1.65f);
            double yaw = rng.Randf() * TAU;
            transforms.Add(new Transform3D(new Basis(Vector3.Up, (float)yaw).Scaled(new Vector3((float)width, (float)height, (float)width)), new Vector3(p.X, (float)(y - 0.03), p.Y)));
            customs.Add(new Color(rng.Randf(), (float)(cos(yaw) * 0.5 + 0.5), (float)(sin(yaw) * 0.5 + 0.5), rng.RandfRange(0.06f, 0.18f)));
            colors.Add(new Color(rng.RandfRange(0.80f, 0.98f), rng.RandfRange(0.90f, 1.06f), rng.RandfRange(0.72f, 0.92f), rng.RandfRange(0.04f, 0.38f)));
        }
        _add_margin_multimesh("WetRushes", GrassPlanter.reed_mesh(), _wet_grass_material(true), transforms, customs, colors);
    }

    public void _build_wet_shrubs()
    {
        // Broadleaf shrubs sit one band uphill from emergents, where roots stay wet
        // but crowns remain terrestrial. Reuse the established shrub asset/material.
        if (_understory.shrub_mesh == null || _understory.shrub_material == null)
        {
            return;
        }
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(88111));
        Godot.Collections.Array<Transform3D> transforms = new Godot.Collections.Array<Transform3D>();
        Godot.Collections.Array<Color> customs = new Godot.Collections.Array<Color>();
        for (long attempt = 0; attempt < WET_SHRUB_ATTEMPTS; attempt++)
        {
            double angle = rng.Randf() * TAU;
            Vector2 shore = TerrainField.shore_point(angle);
            Vector2 radial = (shore - TerrainField.POND_CENTRE).Normalized();
            Vector2 tangent = new Vector2(-radial.Y, radial.X);
            Vector2 p = shore + radial * rng.RandfRange(1.2f, 5.8f) + tangent * rng.RandfRange(-2.0f, 2.0f);
            if (_shore_navigation_blocked(p, 2.4) || rng.Randf() > 0.28)
            {
                continue;
            }
            double y = _field.height_fast(p.X, p.Y);
            if (y < TerrainField.WATER_LEVEL - 0.02 || y > TerrainField.WATER_LEVEL + 1.35)
            {
                continue;
            }
            double scale = rng.RandfRange(0.72f, 1.35f);
            // Preserve the seeded draw order: scale before rotation.
            Vector3 shrubScale = new Vector3((float)scale, (float)(scale * rng.RandfRange(0.72f, 1.18f)), (float)scale);
            transforms.Add(new Transform3D(new Basis(Vector3.Up, (float)(rng.Randf() * TAU)).Scaled(shrubScale), new Vector3(p.X, (float)(y - 0.05), p.Y)));
            Color tint = new Color(0.82f, 1.0f, 0.78f).Lerp(new Color(0.96f, 0.92f, 0.68f), (float)(rng.Randf() * 0.22));
            customs.Add(new Color(rng.Randf(), tint.R, tint.G, tint.B));
        }
        if ((transforms.Count == 0))
        {
            return;
        }
        MultiMeshInstance3D node = _add_simple_multimesh("WetBankShrubs", _understory.shrub_mesh, _understory.shrub_material, transforms, customs);
        node.Layers = unchecked((uint)(1));
    }

    public ShaderMaterial _wet_grass_material(bool tall)
    {
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>("res://shaders/grass.gdshader");
        mat.SetShaderParameter("root_color", tall ? new Color(0.055f, 0.10f, 0.035f) : new Color(0.07f, 0.13f, 0.045f));
        mat.SetShaderParameter("tip_color", tall ? new Color(0.34f, 0.48f, 0.18f) : new Color(0.28f, 0.47f, 0.17f));
        mat.SetShaderParameter("dry_tip_color", new Color(0.48f, 0.43f, 0.22f));
        mat.SetShaderParameter("sheen", tall ? 0.28 : 0.36);
        mat.SetShaderParameter("trample_radius", 0.6);
        mat.SetShaderParameter("fade_start", 95.0);
        mat.SetShaderParameter("fade_end", 145.0);
        return mat;
    }

    public void _add_margin_multimesh(string node_name, ArrayMesh mesh, Material material, Godot.Collections.Array<Transform3D> transforms, Godot.Collections.Array<Color> customs, Godot.Collections.Array<Color> colors)
    {
        if ((transforms.Count == 0))
        {
            return;
        }
        MultiMesh mm = new MultiMesh();
        mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        mm.UseCustomData = true;
        mm.UseColors = true;
        mm.Mesh = mesh;
        mm.InstanceCount = (int)(long)transforms.Count;
        for (long i = 0, i_end = (long)transforms.Count; i < i_end; i++)
        {
            mm.SetInstanceTransform((int)i, transforms[(int)i]);
            mm.SetInstanceCustomData((int)i, customs[(int)i]);
            mm.SetInstanceColor((int)i, colors[(int)i]);
        }
        MultiMeshInstance3D node = new MultiMeshInstance3D();
        node.Name = node_name;
        node.Multimesh = mm;
        node.MaterialOverride = material;
        node.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        node.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        node.Layers = unchecked((uint)(1));
        AddChild(node);
    }

    public MultiMeshInstance3D _add_simple_multimesh(string node_name, ArrayMesh mesh, Material material, Godot.Collections.Array<Transform3D> transforms, Godot.Collections.Array<Color> customs)
    {
        MultiMesh mm = new MultiMesh();
        mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        mm.UseCustomData = true;
        mm.Mesh = mesh;
        mm.InstanceCount = (int)(long)transforms.Count;
        for (long i = 0, i_end = (long)transforms.Count; i < i_end; i++)
        {
            mm.SetInstanceTransform((int)i, transforms[(int)i]);
            mm.SetInstanceCustomData((int)i, customs[(int)i]);
        }
        MultiMeshInstance3D node = new MultiMeshInstance3D();
        node.Name = node_name;
        node.Multimesh = mm;
        node.MaterialOverride = material;
        node.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        node.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        AddChild(node);
        return node;
    }

    public bool _shore_navigation_blocked(Vector2 p, double extra)
    {
        if (p.DistanceTo(TerrainField.DOCK_START) < 4.0 + extra)
        {
            return true;
        }
        if (_field.walking_distance(p) < 0.55 + extra * 0.35)
        {
            return true;
        }
        return false;
    }

    public static double _shore_offset(Vector2 pos)
    {
        Vector2 delta = pos - TerrainField.POND_CENTRE;
        return delta.Length() - TerrainField.pond_radius_at(atan2(delta.Y, delta.X));
    }
}
