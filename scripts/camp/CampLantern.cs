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

/// A hurricane lantern: iron base and cage, a glass globe with a live flame,
/// an omni light and a few moths. Mounted on a shepherd's-hook post, hung from
/// an iron bracket, or simply hung from wherever the parent puts it. Interact
/// toggles the flame.
public partial class CampLantern : Node3D
{
    [Signal]
    public delegate void toggledEventHandler(bool on);

    public enum Mount
    {
        HOOK_POST,
        BRACKET,
        HANGING,
    }

    public static readonly Color LIGHT_COLOR = new Color(1.0f, 0.7f, 0.36f);
    public const double LIGHT_ENERGY = 1.6;
    public const double LIGHT_RANGE = 8.0;
    public const double POST_HEIGHT = 1.75;
    public const double FLAME_HEIGHT = 0.2;
    public const long GLASS_LAYER = 1L << 16;

    public bool lit = true;
    public bool attracts_moths = true;
    public OmniLight3D light;
    public Node3D housing;
    public MeshInstance3D glass_mesh;
    public MeshInstance3D flame_mesh;
    public GpuParticles3D moths;
    public Interactable body;
    public StandardMaterial3D _glass_mat;
    public ShaderMaterial _flame_mat;
    public double _time = 0.0;
    public double _phase = 0.0;
    public long _seed = 0;

    public CampLantern()
    {
        Name = "Lantern";
    }

    public void build(bool posted, bool shadows, bool bracket = false, Vector3? hang_from_opt = null)
    {
        Vector3 hang_from = hang_from_opt ?? Vector3.Zero;
        /// `posted` mounts the lantern on a hook post standing at this node's origin;
        /// otherwise the housing hangs with its base at the origin. `bracket` adds an
        /// iron arm above the origin (for the dock pile) and hangs the lantern from it.
        _seed = hash((StringName)Name) + (long)(Position.X * 13.0) + (long)(Position.Z * 7.0);
        _phase = (double)(_seed % 1000) / 1000.0 * TAU;
        Vector3 hang_at = hang_from;
        if (posted)
        {
            hang_at = _add_post();
        }
        else if (bracket)
        {
            hang_at = _add_bracket();
        }
        _add_housing(hang_at);
        if (hang_at != Vector3.Zero)
        {
            _add_link(hang_at);
        }
        _add_light(shadows);
        if (attracts_moths)
        {
            _add_moths();
        }
        _apply_lit();
    }

    public string prompt()
    {
        return lit ? "Douse lantern" : "Light lantern";
    }

    public void interact(Node _player)
    {
        set_lit(!lit);
        if (Game.Instance.audio != null && Game.Instance.audio.HasMethod("play_interact"))
        {
            Game.Instance.audio.play_interact("lantern_toggle");
        }
    }

    public void set_lit(bool value)
    {
        if (lit == value)
        {
            return;
        }
        lit = value;
        _apply_lit();
        EmitSignal(SignalName.toggled, lit);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        // The same air moves the canopy, linen, smoke and hanging lanterns,
        // including an unlit lantern before sunset.
        if (housing != null && G.truthy(housing.GetMeta("swings", false)))
        {
            double wind = Game.Instance.world != null ? Game.Instance.world.wind_strength() : 0.4;
            Vector3 _t1 = housing.Rotation;
            _t1.Z = (float)(0.03 * sin(_time * 1.3 + _phase) * wind);
            housing.Rotation = _t1;
            Vector3 _t2 = housing.Rotation;
            _t2.X = (float)(0.02 * sin(_time * 0.9 + _phase * 2.0) * wind);
            housing.Rotation = _t2;
        }
        if (!lit || light == null)
        {
            return;
        }
        double flicker = 1.0 + 0.05 * sin(_time * 7.3 + _phase) + 0.035 * sin(_time * 13.1 + _phase * 1.7);
        light.LightEnergy = (float)(LIGHT_ENERGY * flicker);
        if (_flame_mat != null)
        {
            _flame_mat.SetShaderParameter("heat", 1.7 * flicker);
        }
    }

    public Vector3 _add_post()
    {
        /// Hook post standing at the origin; returns the point the lantern hangs from.
        MeshInstance3D post = new MeshInstance3D();
        post.Name = "Post";
        post.Mesh = PropMeshes.hook_post_mesh(POST_HEIGHT, _seed);
        post.MaterialOverride = PropMaterials.wood(new Color(0.5f, 0.4f, 0.3f), 0.5, 0.0, 1.0);
        post.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        post.GIMode = GeometryInstance3D.GIModeEnum.Static;
        AddChild(post);
        body = new Interactable();
        body.CollisionLayer = unchecked((uint)(1 | 1L << 1));
        body.CollisionMask = unchecked((uint)(0));
        body.SetMeta("surface", (StringName)"wood");
        body.on_interact = new Callable(this, CampLantern.MethodName.interact);
        CollisionShape3D collider = new CollisionShape3D();
        CylinderShape3D cyl = new CylinderShape3D();
        cyl.Radius = 0.05f;
        cyl.Height = (float)POST_HEIGHT;
        collider.Shape = cyl;
        Vector3 _t1 = collider.Position;
        _t1.Y = (float)(POST_HEIGHT * 0.5);
        collider.Position = _t1;
        body.AddChild(collider);
        AddChild(body);
        return PropMeshes.hook_point(POST_HEIGHT);
    }

    public Vector3 _add_bracket()
    {
        /// Iron bracket rising from the origin (a pile head) with an arm to hang from.
        MeshBuilder mb = new MeshBuilder();
        Vector3 rise = new Vector3(0.0f, 0.34f, 0.0f);
        Vector3 arm_end = rise + new Vector3(0.24f, 0.0f, 0.0f);
        mb.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(0.0f, -0.06f, 0.0f), rise }, new Godot.Collections.Array<double> { 0.011, 0.011 }, 6, Colors.White, 1.0, 4.0, 0.0, true);
        mb.add_tube(new Godot.Collections.Array<Vector3> { rise + new Vector3(-0.02f, 0.0f, 0.0f), arm_end }, new Godot.Collections.Array<double> { 0.01, 0.009 }, 6, Colors.White, 1.0, 4.0, 0.0, true);
        mb.add_tube(new Godot.Collections.Array<Vector3> { rise + new Vector3(0.0f, -0.12f, 0.0f), rise + new Vector3(0.14f, -0.005f, 0.0f) }, new Godot.Collections.Array<double> { 0.007, 0.007 }, 5, Colors.White, 1.0, 4.0, 0.0, true);
        MeshInstance3D bracket = new MeshInstance3D();
        bracket.Name = "Bracket";
        bracket.Mesh = mb.commit();
        bracket.MaterialOverride = PropMaterials.iron();
        bracket.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        AddChild(bracket);
        return arm_end + new Vector3(0.0f, -0.01f, 0.0f);
    }

    public void _add_link(Vector3 hang_at)
    {
        /// The visible link between the hook and the bail: a short wire loop.
        MeshBuilder mb = new MeshBuilder();
        Vector3 bail_top = hang_at - new Vector3(0.0f, 0.03f, 0.0f);
        Godot.Collections.Array<Vector3> link_r = new Godot.Collections.Array<Vector3>();
        Godot.Collections.Array<double> link_rad = new Godot.Collections.Array<double>();
        for (long i = 0; i < 9; i++)
        {
            double a = (double)i / 8.0 * TAU;
            link_r.Add(hang_at + new Vector3(0.0f, (float)(-0.015 + cos(a) * 0.016), (float)(sin(a) * 0.012)));
            link_rad.Add(0.004);
        }
        mb.add_tube(link_r, link_rad, 5, Colors.White, 1.0, 4.0, 0.0, false);
        MeshInstance3D link = new MeshInstance3D();
        link.Name = "Link";
        link.Mesh = mb.commit();
        link.MaterialOverride = PropMaterials.iron();
        link.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(link);
        // Hang the housing so the bail's top sits inside the link.
        housing.Position = bail_top - new Vector3(0.0f, (float)PropMeshes.HURRICANE_HEIGHT, 0.0f);
    }

    public void _add_housing(Vector3 hang_at)
    {
        housing = new Node3D();
        housing.Name = "Housing";
        bool hung = hang_at != Vector3.Zero;
        housing.Position = hung ? hang_at - new Vector3(0.0f, (float)PropMeshes.HURRICANE_HEIGHT, 0.0f) : Vector3.Zero;
        housing.SetMeta("swings", hung);
        AddChild(housing);

        MeshInstance3D metal = new MeshInstance3D();
        metal.Name = "Metal";
        metal.Mesh = PropMeshes.hurricane_metal();
        metal.MaterialOverride = PropMaterials.iron(new Color(0.2f, 0.17f, 0.14f));
        metal.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        housing.AddChild(metal);

        glass_mesh = new MeshInstance3D();
        glass_mesh.Name = "Glass";
        glass_mesh.Mesh = PropMeshes.hurricane_glass();
        glass_mesh.Layers = unchecked((uint)(GLASS_LAYER));
        _glass_mat = new StandardMaterial3D();
        _glass_mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _glass_mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        _glass_mat.AlbedoColor = new Color(1.0f, 0.9f, 0.7f, 0.16f);
        _glass_mat.Roughness = 0.05f;
        _glass_mat.Metallic = 0.0f;
        _glass_mat.MetallicSpecular = 0.9f;
        _glass_mat.EmissionEnabled = true;
        _glass_mat.Emission = new Color(1.0f, 0.62f, 0.25f);
        _glass_mat.EmissionEnergyMultiplier = 0.22f;
        glass_mesh.MaterialOverride = _glass_mat;
        glass_mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        housing.AddChild(glass_mesh);
        // The wick flame: a small camera-facing flame card (the fire shader at
        // candle scale) standing on the wick tube.

        flame_mesh = new MeshInstance3D();
        flame_mesh.Name = "Flame";
        QuadMesh flame = new QuadMesh();
        flame.Size = new Vector2(0.03f, 0.05f);
        flame.CenterOffset = new Vector3(0.0f, 0.02f, 0.0f);
        flame_mesh.Mesh = flame;
        _flame_mat = new ShaderMaterial();
        _flame_mat.Shader = Content.Load<Shader>("res://shaders/fire.gdshader");
        _flame_mat.SetShaderParameter("heat", 1.7);
        _flame_mat.SetShaderParameter("opacity", 0.95);
        _flame_mat.SetShaderParameter("wind_lean", 0.0);
        flame_mesh.MaterialOverride = _flame_mat;
        Vector3 _t1 = flame_mesh.Position;
        _t1.Y = (float)(FLAME_HEIGHT - 0.03);
        flame_mesh.Position = _t1;
        flame_mesh.CustomAabb = new Aabb(new Vector3(-0.05f, -0.02f, -0.05f), new Vector3(0.1f, 0.1f, 0.1f));
        flame_mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        housing.AddChild(flame_mesh);

        if (body == null)
        {
            body = new Interactable();
            body.CollisionLayer = unchecked((uint)(1 | 1L << 1));
            body.CollisionMask = unchecked((uint)(0));
            body.on_interact = new Callable(this, CampLantern.MethodName.interact);
            CollisionShape3D collider = new CollisionShape3D();
            BoxShape3D box = new BoxShape3D();
            box.Size = new Vector3(0.16f, 0.4f, 0.16f);
            collider.Shape = box;
            Vector3 _t2 = collider.Position;
            _t2.Y = 0.2f;
            collider.Position = _t2;
            body.AddChild(collider);
            housing.AddChild(body);
        }
    }

    public void _add_light(bool shadows)
    {
        light = new OmniLight3D();
        light.Name = "Light";
        light.LightColor = LIGHT_COLOR;
        light.LightEnergy = (float)LIGHT_ENERGY;
        // The point emitter is centimetres from the globe. Its diffuse lighting
        // turns transparent glass into a clipped white shell. The globe instead
        // carries a restrained warm emission and still reflects external lights.
        light.LightCullMask = unchecked((uint)(0xFFFFF & ~GLASS_LAYER));
        light.OmniRange = (float)LIGHT_RANGE;
        light.OmniAttenuation = 1.45f;
        light.LightVolumetricFogEnergy = 0.18f;
        light.LightSize = 0.05f;
        light.ShadowEnabled = shadows;
        light.OmniShadowMode = OmniLight3D.ShadowMode.DualParaboloid;
        light.ShadowBlur = 2.0f;
        light.ShadowBias = 0.04f;
        light.DistanceFadeEnabled = true;
        light.DistanceFadeBegin = 30.0f;
        light.DistanceFadeLength = 14.0f;
        light.Position = new Vector3(0.0f, (float)(FLAME_HEIGHT + 0.02), 0.0f);
        housing.AddChild(light);
    }

    public void _add_moths()
    {
        moths = new GpuParticles3D();
        moths.Name = "Moths";
        moths.Amount = 4;
        moths.Lifetime = 5.0;
        moths.Preprocess = 3.0;
        moths.VisibilityAabb = new Aabb(new Vector3(-1.2f, -0.4f, -1.2f), new Vector3(2.4f, 1.6f, 2.4f));
        moths.Position = new Vector3(0.0f, (float)(FLAME_HEIGHT + 0.1), 0.0f);
        ParticleProcessMaterial process = new ParticleProcessMaterial();
        process.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        process.EmissionSphereRadius = 0.3f;
        process.Direction = new Vector3(0.0f, 1.0f, 0.0f);
        process.Spread = 180.0f;
        process.InitialVelocityMin = 0.15f;
        process.InitialVelocityMax = 0.45f;
        process.Gravity = new Vector3(0.0f, 0.02f, 0.0f);
        process.DampingMin = 0.4f;
        process.DampingMax = 0.8f;
        process.ScaleMin = 0.01f;
        process.ScaleMax = 0.024f;
        process.Color = new Color(0.55f, 0.45f, 0.28f);
        process.TurbulenceEnabled = true;
        process.TurbulenceNoiseStrength = 1.5f;
        process.TurbulenceNoiseScale = 2.5f;
        moths.ProcessMaterial = process;
        moths.DrawPass1 = PropMeshes.sphere_mesh(0.5, 4, 6);
        // Moths reflect the lantern; they are not self-lit fireflies. An opaque,
        // lit surface also lets the tent canvas occlude them normally.
        StandardMaterial3D mat = new StandardMaterial3D();
        mat.VertexColorUseAsAlbedo = true;
        mat.Roughness = 0.92f;
        mat.MetallicSpecular = 0.15f;
        moths.MaterialOverride = mat;
        housing.AddChild(moths);
    }

    public void _apply_lit()
    {
        if (light != null)
        {
            light.Visible = lit;
        }
        if (moths != null)
        {
            moths.Emitting = lit;
        }
        if (flame_mesh != null)
        {
            flame_mesh.Visible = lit;
        }
        if (_glass_mat != null)
        {
            _glass_mat.EmissionEnergyMultiplier = (float)(lit ? 0.22 : 0.0);
            Color _t1 = _glass_mat.AlbedoColor;
            _t1.A = (float)(lit ? 0.16 : 0.14);
            _glass_mat.AlbedoColor = _t1;
        }
        if (body != null)
        {
            body.prompt_text = prompt();
        }
    }
}
