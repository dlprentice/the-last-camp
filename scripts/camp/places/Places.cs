#nullable disable
using System;
using System.Collections.Generic;
using Godot;

namespace LastCamp;

/// The micro cut's three places (trailhead post, old fire ring, Tom's Rock) and
/// Tom's blazes along his route. Builds them on the shared terrain field and
/// exposes the story's hooks as plain events and methods. Nothing here plays a
/// line, shows a prompt or marks anything: the story decides what each hook does.
public partial class Places : Node3D
{
    public const string TRAILHEAD = "trailhead";
    public const string OLD_RING = "old_ring";
    public const string TOMS_ROCK = "toms_rock";
    /// A blaze counts as in view within this distance, inside this cone of the
    /// look direction, facing the viewer and with nothing in between.
    public const double BLAZE_VIEW_DISTANCE = 25.0;
    public const double BLAZE_VIEW_CONE_DEG = 28.0;
    public const double BLAZE_POLL_SECONDS = 0.2;
    public const uint PLAYER_LAYER = 1u << 2;

    /// The player entered a place's arrival volume (TRAILHEAD, OLD_RING, TOMS_ROCK).
    public event Action<string> Arrived;
    /// The story seated the player on Tom's bench.
    public event Action BenchSat;
    /// A blaze came into the player's view for the first time (0 is the notch on
    /// the old ring's log, then the trees along Tom's route in order).
    public event Action<int> BlazeInView;
    /// The loose twentieth step was lifted and the tin under it is exposed.
    public event Action StepLifted;

    public struct Blaze
    {
        public Vector3 position;
        /// Unit direction the notch's face looks.
        public Vector3 face;
    }

    public TerrainField field;
    public ScenePlan plan;
    public Forest forest;
    public TrailheadPost post;
    public OldFireRing ring;
    public TomsRock rock;
    public readonly List<Blaze> blazes = new();
    public readonly HashSet<int> blazes_seen = new();
    public readonly Dictionary<string, Area3D> arrivals = new();
    public bool bench_sat = false;
    double _poll;

    public Places(TerrainField p_field, ScenePlan p_plan, Forest p_forest)
    {
        field = p_field;
        plan = p_plan;
        forest = p_forest;
        Name = "Places";
    }

    public Places()
    {
    }

    public void build()
    {
        post = new TrailheadPost(field);
        AddChild(post);
        post.build(PlacesLayout.TRAILHEAD_POST, PlacesLayout.TRAILHEAD_FACE_TOWARDS);
        ring = new OldFireRing(field);
        AddChild(ring);
        ring.build();
        rock = new TomsRock(field);
        AddChild(rock);
        rock.build();
        blazes.Add(new Blaze { position = ring.notch_world, face = ring.notch_face });
        _build_tree_blazes();
        _add_arrival(TRAILHEAD, PlacesLayout.TRAILHEAD_POST, PlacesLayout.ARRIVE_TRAILHEAD);
        _add_arrival(OLD_RING, PlacesLayout.OLD_RING, PlacesLayout.ARRIVE_OLD_RING);
        _add_arrival(TOMS_ROCK, PlacesLayout.TOMS_ROCK, PlacesLayout.ARRIVE_TOMS_ROCK);
    }

    public void _build_tree_blazes()
    {
        if (forest == null || plan == null) return;
        PlacesLayout.BlazeSite[] sites = PlacesLayout.blaze_sites();
        var cut = BlazeNotch.cut_material(true);
        for (int i = 0; i < plan.blaze_trees.Count && i < sites.Length; i++)
        {
            ScenePlan.TreeEntry entry = plan.blaze_trees[i];
            Godot.Collections.Dictionary record = null;
            foreach (Godot.Collections.Dictionary r in forest.near_records)
            {
                if (ReferenceEquals(r["entry"].As<ScenePlan.TreeEntry>(), entry)) { record = r; break; }
            }
            if (record == null) continue;
            var bark = record["bark"].As<MeshInstance3D>();
            Transform3D xform = record["xform"].AsTransform3D();
            var mesh = (ArrayMesh)bark.Mesh;
            float scale = xform.Basis.Y.Length();
            double ground = field.height(entry.position.X, entry.position.Y);
            double local_y = (ground + PlacesLayout.BLAZE_HEIGHT - xform.Origin.Y) / scale;
            Vector3 axis = BlazeNotch.trunk_axis(mesh, local_y);
            Basis inv = xform.Basis.Inverse();
            Vector3 face = new Vector3(sites[i].face.X, 0, sites[i].face.Y);
            // The notch is cut into this tree's own copy of its trunk; the faces
            // take the cut's material and everything else keeps the bark's.
            BlazeNotch.Carved notch = BlazeNotch.carve(mesh, 0, axis, (inv * face).Normalized(), (inv * Vector3.Up).Normalized(), scale, (ulong)(i + 1) * 7919UL);
            if (notch == null) continue;
            Material bark_material = bark.MaterialOverride;
            bark.Mesh = notch.mesh;
            bark.MaterialOverride = null;
            for (int s = 0; s < notch.mesh.GetSurfaceCount(); s++) bark.SetSurfaceOverrideMaterial(s, s == notch.cut_surface ? cut : bark_material);
            // Keep the notched trunk at full detail where the notch can be seen.
            bark.LodBias = Math.Max(bark.LodBias, 4.0f);
            blazes.Add(new Blaze { position = xform * notch.surface_centre, face = face });
        }
    }

    public void _add_arrival(string id, Vector2 at, double radius)
    {
        var area = new Area3D { Name = $"Arrive_{id}", CollisionLayer = 0, CollisionMask = PLAYER_LAYER, Monitorable = false };
        double ground = field.height(at.X, at.Y);
        area.Position = new Vector3(at.X, (float)ground, at.Y);
        var shape_125 = new CylinderShape3D { Radius = (float)radius, Height = 4.0f };
        area.AddChild(new CollisionShape3D { Shape = shape_125 });
        GC.KeepAlive(shape_125);
        area.BodyEntered += body =>
        {
            if (body is Player) Arrived?.Invoke(id);
        };
        AddChild(area);
        arrivals[id] = area;
    }

    // ------------------------------------------------------------------ hooks

    /// The story calls this when it seats the player on Tom's bench.
    public void SitOnBench()
    {
        bench_sat = true;
        BenchSat?.Invoke();
    }

    /// Where someone sits on the bench and which way they look.
    public static Vector3 BenchSeat(TerrainField field)
    {
        Vector2 b = PlacesLayout.BENCH;
        return new Vector3(b.X, (float)(field.height(b.X, b.Y) + PlacesLayout.BENCH_SEAT_HEIGHT), b.Y);
    }

    /// True while the player stands on the loose twentieth step.
    public bool IsPlayerOnLooseStep()
    {
        Player player = Game.Instance?.player;
        return player != null && player.IsOnFloor() && rock != null && rock.is_on_loose_step(player.GlobalPosition);
    }

    /// True if `feet` stand on the loose twentieth step.
    public bool IsOnLooseStep(Vector3 feet) => rock != null && rock.is_on_loose_step(feet);

    public bool StepIsLifted => rock != null && rock.step_lifted;

    /// The loose step's node (its pose can be animated) and the tin under it.
    public Node3D LooseStep => rock?.loose_step;
    public Node3D Tin => rock?.tin;

    /// Lifts the loose step aside and exposes the tin. Once only.
    public void LiftStep()
    {
        if (rock == null || rock.step_lifted) return;
        rock.lift_step();
        StepLifted?.Invoke();
    }

    /// The blaze nearest the centre of view that `eye` sees looking along
    /// `forward`, or -1. Terrain hides blazes; nothing else is tested.
    public int BlazeVisibleFrom(Vector3 eye, Vector3 forward)
    {
        int best = -1;
        double best_angle = double.PositiveInfinity;
        forward = forward.Normalized();
        for (int i = 0; i < blazes.Count; i++)
        {
            Vector3 to = blazes[i].position - eye;
            double d = to.Length();
            if (d > BLAZE_VIEW_DISTANCE || d < 0.2) continue;
            Vector3 dir = to / (float)d;
            double angle = Mathf.RadToDeg(Math.Acos(Math.Clamp(dir.Dot(forward), -1.0f, 1.0f)));
            if (angle > BLAZE_VIEW_CONE_DEG || angle >= best_angle) continue;
            // The notch faces the viewer, at least obliquely.
            if (blazes[i].face.Dot(-dir) < 0.2f) continue;
            if (!_terrain_clear(eye, blazes[i].position)) continue;
            best = i;
            best_angle = angle;
        }
        return best;
    }

    bool _terrain_clear(Vector3 from, Vector3 to)
    {
        double length = from.DistanceTo(to);
        for (double t = 0.5; t < length - 0.5; t += 0.5)
        {
            Vector3 q = from.Lerp(to, (float)(t / length));
            if (field.height(q.X, q.Z) > q.Y) return false;
        }
        return true;
    }

    /// The player has stood on the loose step at least once (it then offers itself).
    public bool loose_step_stood_on = false;
    bool _on_loose;

    /// The player stepped onto the loose step (each time, not only the first).
    public event Action LooseStepPressed;

    public override void _PhysicsProcess(double delta)
    {
        bool on = IsPlayerOnLooseStep();
        rock?.update_rock(on, delta);
        if (on && !_on_loose)
        {
            loose_step_stood_on = true;
            LooseStepPressed?.Invoke();
        }
        _on_loose = on;
        if (BlazeInView == null || blazes.Count == 0) return;
        _poll -= delta;
        if (_poll > 0.0) return;
        _poll = BLAZE_POLL_SECONDS;
        Player player = Game.Instance?.player;
        if (player == null || player.camera == null || !player.camera.Current) return;
        Vector3 eye = player.camera.GlobalPosition;
        int seen = BlazeVisibleFrom(eye, -player.camera.GlobalBasis.Z);
        if (seen >= 0 && blazes_seen.Add(seen)) BlazeInView?.Invoke(seen);
    }
}
