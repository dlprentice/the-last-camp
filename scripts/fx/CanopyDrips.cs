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

/// Secondary water shedding from the generated tree crowns after rain. Drops
/// are placed deterministically from the actual procedural crown bounds and end
/// on the same TerrainField/water level used by the rest of the world. The GPU
/// animates the fall; no per-drop physics or raycasts run during gameplay.
public partial class CanopyDrips : Node3D
{
    public static Shader DRIP_SHADER => _DRIP_SHADER_cache ??= _preload_canopy_drip;
    private static Shader _DRIP_SHADER_cache;
    public const long MAX_DROPS = 3200;
    public const double MAX_TREE_RADIUS = 104.0;
    public const double FALL_RANGE = 32.0;

    public Camp _camp;
    public WorldController _world;
    public MultiMeshInstance3D _mesh;
    public ShaderMaterial _material;
    public bool _built = false;
    public double _clock = 0.0;
    public double _strength = 0.0;

    public override void _Ready()
    {
        Name = "CanopyDrips";
        SetProcess(false);
    }

    public static double post_rain_strength(double wetness, double rainfall)
    {
        double wet = clampf(wetness, 0.0, 1.0);
        double rain_suppression = 1.0 - smoothstep(0.05, 0.62, clampf(rainfall, 0.0, 1.0));
        return wet * rain_suppression;
    }

    public override void _Process(double delta)
    {
        if (!_built)
        {
            return;
        }
        if (!GodotObject.IsInstanceValid(_world) || _world.weather == null)
        {
            return;
        }
        _clock += delta;
        double target = post_rain_strength(_world.weather.wet, _world.weather.rain);
        // Canopy shedding rises promptly when rain stops but decays with retained
        // scene wetness rather than switching off as one synchronized event.
        double rate = target > _strength ? 2.8 : 0.75;
        _strength = lerpf(_strength, target, 1.0 - exp(-delta * rate));
        if (_material != null)
        {
            _material.SetShaderParameter("drip_clock", _clock);
            _material.SetShaderParameter("drip_strength", _strength);
        }
        if (_mesh != null)
        {
            _mesh.Visible = _strength > 0.008;
        }
    }

    public void setup(Camp camp, WorldController world)
    {
        if (_built)
        {
            return;
        }
        _camp = camp;
        _world = world;
        _build();
        _built = true;
        SetProcess(true);
    }

    public void _build()
    {
        Godot.Collections.Array<Transform3D> transforms = new Godot.Collections.Array<Transform3D>();
        Godot.Collections.Array<Color> customs = new Godot.Collections.Array<Color>();
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(420917));
        foreach (ScenePlan.TreeEntry entry in _camp.plan.near_trees())
        {
            if ((long)transforms.Count >= MAX_DROPS)
            {
                break;
            }
            if (entry.position.Length() > MAX_TREE_RADIUS)
            {
                continue;
            }
            TreeSpecies species = TreeSpecies.by_kind(entry.kind);
            if (!species.has_leaves())
            {
                continue;
            }
            Godot.Collections.Array variants = _camp.forest.variants[(long)entry.kind].AsGodotArray();
            if ((variants.Count == 0))
            {
                continue;
            }
            TreeGenerator.Result result = variants[(int)(absi(entry.seed_value) % (long)variants.Count)].As<TreeGenerator.Result>();
            double scale = entry.scale;
            double base_y = _camp.field.surface_height(entry.position.X, entry.position.Y) - 0.12 * scale;
            double crown_radius = result.crown_radius * scale;
            double crown_y = base_y + result.crown_center.Y * scale;
            Vector3 offset = new Basis(Vector3.Up, (float)entry.rotation) * result.crown_center * (float)scale;
            Vector2 crown_xz = entry.position + new Vector2(offset.X, offset.Z);
            // Mature crowns shed more drops, while small saplings remain a sparse
            // accent. Count is capped globally and later quality-gated in the shader.
            long count = clampi((long)round(crown_radius * 2.2), 4, 14);
            for (long i = 0; i < count; i++)
            {
                if ((long)transforms.Count >= MAX_DROPS)
                {
                    break;
                }
                double angle = rng.Randf() * TAU;
                double radius = sqrt(rng.Randf()) * crown_radius * rng.RandfRange(0.35f, 0.92f);
                Vector2 xz = crown_xz + new Vector2((float)cos(angle), (float)sin(angle)) * (float)radius;
                // Outer branches are lower; random crown depth prevents one horizontal
                // plane of droplets from betraying the procedural placement.
                double radial = radius / maxf(crown_radius, 0.01);
                double source_y = crown_y + crown_radius * rng.RandfRange(-0.20f, 0.34f);
                source_y -= crown_radius * radial * radial * rng.RandfRange(0.08f, 0.26f);
                double ground_y = _camp.field.height_fast(xz.X, xz.Y);
                if (absf(xz.X) > TerrainBuilder.INNER_UNIFORM_HALF || absf(xz.Y) > TerrainBuilder.INNER_UNIFORM_HALF)
                {
                    ground_y = _camp.field.surface_height(xz.X, xz.Y);
                }
                ground_y = maxf(ground_y, TerrainField.WATER_LEVEL + 0.006);
                double fall_height = clampf(source_y - ground_y, 0.35, FALL_RANGE);
                if (fall_height <= 0.36)
                {
                    continue;
                }
                transforms.Add(new Transform3D(Basis.Identity, new Vector3(xz.X, (float)source_y, xz.Y)));
                customs.Add(new Color((float)(fall_height / FALL_RANGE), rng.Randf(), rng.Randf(), rng.Randf()));
            }
        }

        MultiMesh mm = new MultiMesh();
        mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        mm.UseCustomData = true;
        QuadMesh quad = new QuadMesh();
        quad.Size = Vector2.One;
        mm.Mesh = quad;
        mm.InstanceCount = (int)(long)transforms.Count;
        for (long i2 = 0, i_end = (long)transforms.Count; i2 < i_end; i2++)
        {
            mm.SetInstanceTransform((int)i2, transforms[(int)i2]);
            mm.SetInstanceCustomData((int)i2, customs[(int)i2]);
        }
        _mesh = new MultiMeshInstance3D();
        _mesh.Name = "CanopyDripField";
        _mesh.Multimesh = mm;
        _material = new ShaderMaterial();
        _material.Shader = DRIP_SHADER;
        _material.SetShaderParameter("fall_range", FALL_RANGE);
        // Keep drops out of shelter volumes. These are the same built prop frames
        // used by the rain system, not guessed world-space rectangles.
        _material.SetShaderParameter("tent_inverse", camp_tent_inverse());
        _material.SetShaderParameter("dock_inverse", _camp.campsite.dock.GlobalTransform.AffineInverse());
        _material.SetShaderParameter("dock_length", _camp.campsite.dock.total_length());
        _material.SetShaderParameter("density_scale", clampf(Quality.Instance.current.particle_scale, 0.0, 1.0));
        _mesh.MaterialOverride = _material;
        _mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        _mesh.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        _mesh.CustomAabb = new Aabb(new Vector3((float)(-MAX_TREE_RADIUS - 20.0), (float)(TerrainField.WATER_LEVEL - 0.2), (float)(-MAX_TREE_RADIUS - 20.0)), new Vector3((float)((MAX_TREE_RADIUS + 20.0) * 2.0), 48.0f, (float)((MAX_TREE_RADIUS + 20.0) * 2.0)));
        _mesh.Visible = false;
        AddChild(_mesh);
        Quality.Instance.Connect(Quality.SignalName.preset_changed, new Callable(this, CanopyDrips.MethodName._apply_quality));
        G.print(G.format("Canopy drips: %d deterministic crown samples", (long)transforms.Count));
    }

    public void _apply_quality(QualityPreset preset)
    {
        if (_material != null)
        {
            _material.SetShaderParameter("density_scale", clampf(preset.particle_scale, 0.0, 1.0));
        }
    }

    public override void _ExitTree()
    {
        if (Quality.Instance.IsConnected(Quality.SignalName.preset_changed, new Callable(this, CanopyDrips.MethodName._apply_quality)))
        {
            Quality.Instance.Disconnect(Quality.SignalName.preset_changed, new Callable(this, CanopyDrips.MethodName._apply_quality));
        }
    }

    public Transform3D camp_tent_inverse()
    {
        return _camp.campsite.tent.GlobalTransform.AffineInverse();
    }
    private static Shader _preload_canopy_drip => _preload_canopy_drip_cache ??= Content.Load<Shader>("res://shaders/canopy_drip.gdshader");
    private static Shader _preload_canopy_drip_cache;
}
