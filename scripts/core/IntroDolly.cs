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

/// Establishing shot: a slow crane down from above the treeline into the
/// clearing, ending at eye height on the trail where the player takes over.
public partial class IntroDolly : Node3D
{
    [Signal]
    public delegate void finishedEventHandler();

    public const double DURATION = 17.0;
    public const double SKIP_GRACE = 0.8;

    /// Crane in from the east above the meadow, sweeping past the treeline to
    /// eye height on the trail. Kept clear of trunks and crowns by
    /// test_intro_dolly_path_is_clear_of_trees.
    public static readonly Godot.Collections.Array<Vector3> PATH = new Godot.Collections.Array<Vector3> { new Vector3(16.3f, 8.0f, 52.0f), new Vector3(6.4f, 3.9f, 40.0f), new Vector3(7.9f, 4.2f, 28.0f), new Vector3(8.6f, 2.4f, 19.5f), new Vector3(3.2f, 1.72f, 15.0f) };
    public static readonly Godot.Collections.Array<Vector3> LOOK = new Godot.Collections.Array<Vector3> { new Vector3(-4.0f, 3.5f, 4.0f), new Vector3(-5.0f, 2.4f, 3.0f), new Vector3(-8.0f, 1.4f, 2.0f), new Vector3(-12.0f, 0.9f, 3.0f), new Vector3(-14.0f, 0.9f, 3.5f) };

    public static List<string> obstructions(TerrainField field, ScenePlan plan, long samples = 80)
    {
        /// Trees the path would fly through (trunk or crown), as readable strings;
        /// empty when the path is clear. Used by the tests.
        List<string> @out = new List<string>();
        Spline path = new Spline(PATH);
        for (long i = 0; i < samples; i++)
        {
            Vector3 p = path.sample((double)i / (double)(samples - 1));
            if (p.Y < field.height(p.X, p.Z) + 1.0)
            {
                @out.Add(G.format("ground at %s", p));
            }
            foreach (ScenePlan.TreeEntry t in plan.near_trees())
            {
                TreeSpecies species = TreeSpecies.by_kind(t.kind);
                double @base = field.height(t.position.X, t.position.Y);
                double top = @base + species.height.Y * t.scale + 1.0;
                double crown_base = @base + species.branch_start * species.height.X * t.scale - 0.6;
                double radius = ScenePlan.crown_footprint(t.kind) * t.scale + 0.5;
                double dxz = new Vector2(p.X, p.Z).DistanceTo(t.position);
                if (dxz < 1.0 && p.Y < top)
                {
                    @out.Add(G.format("trunk %s %s cam %s", new Godot.Collections.Array { species.name, t.position, p }));
                }
                else if (dxz < radius && p.Y > crown_base && p.Y < top)
                {
                    @out.Add(G.format("crown %s %s r=%.1f base=%.1f cam %s", new Godot.Collections.Array { species.name, t.position, radius, crown_base, p }));
                }
            }
        }
        return @out;
    }

    public Camera3D camera;
    public double elapsed = 0.0;
    public bool playing = false;
    public Player _player;
    public Spline _path = new Spline(PATH);
    public Spline _look = new Spline(LOOK);

    public override void _Ready()
    {
        camera = new Camera3D();
        camera.Name = "IntroCamera";
        camera.Fov = 40.0f;
        camera.Near = 0.05f;
        camera.Far = 1600.0f;
        camera.CullMask = unchecked((uint)(0xFFFFF & ~(Pond.REFLECTION_LAYER | Pond.UNDERWATER_REFLECTION_LAYER)));
        AddChild(camera);
        _apply(0.0);
    }

    public void play(Player player)
    {
        _player = player;
        playing = true;
        elapsed = 0.0;
        camera.MakeCurrent();
        if (Game.Instance.world != null)
        {
            camera.Attributes = Game.Instance.world.attributes;
            Game.Instance.world.attributes.DofBlurFarEnabled = Quality.Instance.current.dof;
        }
        Game.Instance.mode = Game.Mode.INTRO;
        _apply(0.0);
    }

    public override void _Process(double delta)
    {
        if (!playing)
        {
            return;
        }
        elapsed += delta;
        if (elapsed > SKIP_GRACE && Input.IsActionJustPressed("skip_intro"))
        {
            finish();
            return;
        }
        if (elapsed >= DURATION)
        {
            finish();
            return;
        }
        _apply(elapsed / DURATION);
    }

    public void _apply(double t)
    {
        double u = smoothstep(0.0, 1.0, t);
        u = lerpf(t, u, 0.75);
        Vector3 pos = _path.sample(u);
        Vector3 target = _look.sample(u);
        camera.GlobalPosition = pos;
        camera.LookAt(target, Vector3.Up);
        camera.Fov = (float)lerpf(38.0, 64.0, u);
        if (Game.Instance.world != null)
        {
            CameraAttributesPractical a = Game.Instance.world.attributes;
            a.DofBlurFarDistance = (float)(pos.DistanceTo(target) * 1.4);
            a.DofBlurFarTransition = (float)(pos.DistanceTo(target) * 2.5);
            a.DofBlurAmount = (float)lerpf(0.09, 0.03, u);
        }
    }

    public void finish()
    {
        if (!playing)
        {
            return;
        }
        playing = false;
        if (Game.Instance.world != null)
        {
            Game.Instance.world.attributes.DofBlurFarEnabled = false;
        }
        if (_player != null)
        {
            _player.GlobalPosition = new Vector3(camera.GlobalPosition.X, 0.0f, camera.GlobalPosition.Z);
            Vector3 _t1 = _player.GlobalPosition;
            _t1.Y = (float)(Game.Instance.camp.height_at(_player.GlobalPosition.X, _player.GlobalPosition.Z) + 0.1);
            _player.GlobalPosition = _t1;
            Vector3 forward = -camera.GlobalTransform.Basis.Z;
            _player.yaw = atan2(-forward.X, -forward.Z);
            _player.pitch = asin(clampf(forward.Y, -1.0, 1.0));
            _player.begin();
        }
        EmitSignal(SignalName.finished);
        QueueFree();
    }
}
