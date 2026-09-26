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

/// Life below the pond surface: sun shafts leaning along the refracted sun
/// and a slow cloud of suspended silt. Both live on the water layer so the
/// mirror cameras never see them, and both only draw while the active lens
/// is under the surface (the world controller's underwater blend).
public partial class UnderwaterFX : Node3D
{
    public const long SHAFT_COUNT = 56;
    public const long SILT_COUNT = 700;

    public MultiMeshInstance3D shafts;
    public GpuParticles3D silt;
    public ShaderMaterial _shaft_material;
    public ShaderMaterial _silt_material;
    public TerrainField _field;
    public double _shown = -1.0;

    public UnderwaterFX(TerrainField field)
    {
        _field = field;
        Name = "UnderwaterFX";
    }

    public UnderwaterFX()
    {
    }

    public override void _Ready()
    {
        _build_shafts();
        _build_silt();
        Quality.Instance.Connect(Quality.SignalName.preset_changed, new Callable(this, UnderwaterFX.MethodName.apply_quality));
        apply_quality(Quality.Instance.current);
        _apply_visibility(0.0);
    }

    public override void _Process(double _delta)
    {
        WorldController world = Game.Instance.world;
        _apply_visibility(world != null ? world.underwater_blend : 0.0);
    }

    public void _apply_visibility(double amount)
    {
        if (is_equal_approx(amount, _shown))
        {
            return;
        }
        _shown = amount;
        Visible = amount > 0.001;
        _shaft_material.SetShaderParameter("visibility", amount);
        _silt_material.SetShaderParameter("visibility", amount);
    }

    public void apply_quality(QualityPreset p)
    {
        if (silt != null)
        {
            silt.Amount = (int)maxi((long)round(SILT_COUNT * p.particle_scale), 120);
        }
    }

    public void _build_shafts()
    {
        /// Shafts hang from the mean surface over the basin, densest where the
        /// water is deep enough for a beam to have somewhere to go.
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(4171));
        QuadMesh mesh = new QuadMesh();
        mesh.Size = new Vector2(1.0f, 1.0f);
        mesh.CenterOffset = new Vector3(0.0f, -0.5f, 0.0f);
        MultiMesh mm = new MultiMesh();
        mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        mm.UseCustomData = true;
        mm.Mesh = mesh;
        mm.InstanceCount = (int)SHAFT_COUNT;
        Vector2 centre = TerrainField.POND_CENTRE;
        long placed = 0;
        long attempts = 0;
        while (placed < SHAFT_COUNT && attempts < SHAFT_COUNT * 40)
        {
            attempts += 1;
            double angle = rng.RandfRange(0.0f, (float)TAU);
            double radius = sqrt(rng.Randf()) * TerrainField.POND_RADIUS * 0.82;
            Vector2 p = centre + new Vector2((float)cos(angle), (float)sin(angle)) * (float)radius;
            double depth = _field.water_depth(p.X, p.Y);
            if (depth < 1.2)
            {
                continue;
            }
            double length = clampf(depth * 1.15, 1.4, 4.2);
            mm.SetInstanceTransform((int)placed, new Transform3D(Basis.Identity, new Vector3(p.X, (float)TerrainField.WATER_LEVEL, p.Y)));
            // Mostly faint wide sheets with a few brighter ribbons among them.
            double gain = 0.12 + 0.88 * pow(rng.Randf(), 2.2);
            mm.SetInstanceCustomData((int)placed, new Color(rng.Randf(), rng.RandfRange(0.35f, 1.5f), (float)length, (float)gain));
            placed += 1;
        }
        mm.VisibleInstanceCount = (int)placed;
        shafts = new MultiMeshInstance3D();
        shafts.Name = "SunShafts";
        shafts.Multimesh = mm;
        shafts.Layers = unchecked((uint)(Pond.WATER_LAYER));
        shafts.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        shafts.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        shafts.CustomAabb = new Aabb(new Vector3((float)(centre.X - 24.0), (float)(TerrainField.WATER_LEVEL - 6.0), (float)(centre.Y - 24.0)), new Vector3(48.0f, 8.0f, 48.0f));
        _shaft_material = new ShaderMaterial();
        _shaft_material.Shader = Content.Load<Shader>("res://shaders/sun_shaft.gdshader");
        _shaft_material.RenderPriority = 2;
        shafts.MaterialOverride = _shaft_material;
        AddChild(shafts);
    }

    public void _build_silt()
    {
        Vector2 centre = TerrainField.POND_CENTRE;
        silt = new GpuParticles3D();
        silt.Name = "Silt";
        silt.Amount = (int)SILT_COUNT;
        silt.Lifetime = 18.0;
        silt.Preprocess = 14.0;
        silt.Randomness = 0.6f;
        silt.LocalCoords = false;
        silt.VisibilityAabb = new Aabb(new Vector3(-16.0f, -4.5f, -16.0f), new Vector3(32.0f, 6.0f, 32.0f));
        silt.Position = new Vector3(centre.X, (float)(TerrainField.WATER_LEVEL - 1.5), centre.Y);
        silt.Layers = unchecked((uint)(Pond.WATER_LAYER));
        silt.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        ParticleProcessMaterial process = new ParticleProcessMaterial();
        process.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
        process.EmissionBoxExtents = new Vector3(11.5f, 1.25f, 11.5f);
        process.Direction = new Vector3(0.0f, 0.0f, 0.0f);
        process.Spread = 180.0f;
        process.InitialVelocityMin = 0.01f;
        process.InitialVelocityMax = 0.05f;
        process.Gravity = new Vector3(0.0f, -0.004f, 0.0f);
        process.DampingMin = 0.1f;
        process.DampingMax = 0.3f;
        process.ScaleMin = 0.005f;
        process.ScaleMax = 0.014f;
        process.TurbulenceEnabled = true;
        process.TurbulenceNoiseStrength = 0.35f;
        process.TurbulenceNoiseScale = 2.2f;
        process.TurbulenceNoiseSpeed = new Vector3(0.05f, 0.03f, 0.05f);
        process.TurbulenceInfluenceMin = 0.02f;
        process.TurbulenceInfluenceMax = 0.08f;
        Gradient ramp = new Gradient();
        ramp.SetColor(0, new Color(0.88f, 0.96f, 0.84f, 0.0f));
        ramp.SetColor(1, new Color(0.88f, 0.96f, 0.84f, 0.0f));
        ramp.AddPoint(0.15f, new Color(0.88f, 0.96f, 0.84f, 0.75f));
        ramp.AddPoint(0.85f, new Color(0.88f, 0.96f, 0.84f, 0.75f));
        GradientTexture1D ramp_tex = new GradientTexture1D();
        ramp_tex.Gradient = ramp;
        process.ColorRamp = ramp_tex;
        silt.ProcessMaterial = process;
        silt.DrawPass1 = PropMeshes.sphere_mesh(0.5, 4, 5);
        _silt_material = new ShaderMaterial();
        _silt_material.Shader = Content.Load<Shader>("res://shaders/silt.gdshader");
        silt.MaterialOverride = _silt_material;
        AddChild(silt);
    }
}
