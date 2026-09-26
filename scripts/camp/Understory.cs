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

/// Grass blades (chunked MultiMeshes), ferns under the canopy, shrubs from the
/// scene plan and wildflowers through the clearing.
public partial class Understory : Node3D
{
    public const long FERN_COUNT = 2600;
    /// Plants are batched per cell so culling works on the parts of the woodland in
    /// view, and only cells near the camera cast sun shadows (distant plant shadows
    /// are invisible but cost a full pass per cascade).
    public const double FERN_CELL = 16.0;
    public const double FERN_SHADOW_RANGE = 40.0;
    public const double SHRUB_SHADOW_RANGE = 60.0;
    /// Card bushes near the camp were replaced by photoscanned shrubs (see
    /// ScannedDressing._place_plan_shrubs); flip to compare the old look.
    public static readonly bool CARD_SHRUBS_NEAR = false;
    public const long FLOWER_COUNT = 1500;

    public TerrainField field;
    public ScenePlan plan;
    public ShaderMaterial grass_material;
    public ShaderMaterial distant_grass_material;
    public ShaderMaterial tussock_material;
    public ShaderMaterial fern_material;
    public ShaderMaterial shrub_material;
    public ShaderMaterial flower_material;
    public ShaderMaterial reed_material;
    public Godot.Collections.Array<MultiMeshInstance3D> grass_chunks = new Godot.Collections.Array<MultiMeshInstance3D>();
    public Godot.Collections.Array<MultiMeshInstance3D> fern_cells = new Godot.Collections.Array<MultiMeshInstance3D>();
    public Godot.Collections.Array<MultiMeshInstance3D> meadow_cells = new Godot.Collections.Array<MultiMeshInstance3D>();
    /// Twin cell pairs: {near = caster, far = non-caster, half = bounds half-diagonal}.
    public Godot.Collections.Array<Godot.Collections.Dictionary> shadow_cells = new Godot.Collections.Array<Godot.Collections.Dictionary>();
    public ArrayMesh shrub_mesh;
    public long blade_count = 0;
    public long distant_clump_count = 0;
    public long distant_shrub_count = 0;
    public RandomNumberGenerator _rng = new RandomNumberGenerator();
    public double _grass_density = 1.0;
    public double _grass_distance = 70.0;
    public double _foliage_distance = 1.0;
    public GodotThread _replant_thread;
    public bool _replant_dirty = false;

    public Understory(TerrainField p_field, ScenePlan p_plan)
    {
        field = p_field;
        plan = p_plan;
        Name = "Understory";
        _rng.Seed = unchecked((ulong)(5150));
    }

    public Understory()
    {
    }

    public void build_plants()
    {
        /// Ferns, shrubs and flowers (fast enough for the main thread).
        _grass_density = Quality.Instance.current.grass_density;
        _grass_distance = Quality.Instance.current.grass_distance;
        _build_ferns();
        _build_shrubs();
        _build_flowers();
        _build_reeds();
        _build_distant_shrubs();
        Quality.Instance.Connect(Quality.SignalName.preset_changed, new Callable(this, Understory.MethodName.apply_quality));
        apply_quality(Quality.Instance.current);
    }

    public void add_grass(GrassPlanter planter)
    {
        /// Uploads a planned grass layout (planning itself is threadable).
        foreach (MultiMeshInstance3D c in grass_chunks)
        {
            c.QueueFree();
        }
        grass_chunks.Clear();
        blade_count = 0;
        if (grass_material == null)
        {
            grass_material = new ShaderMaterial();
            grass_material.Shader = Content.Load<Shader>("res://shaders/grass.gdshader");
            apply_quality(Quality.Instance.current);
        }
        ArrayMesh blade = GrassPlanter.clump_mesh();
        _upload_grass(planter.chunks, blade, grass_material, "Grass");
        if (distant_grass_material == null)
        {
            distant_grass_material = new ShaderMaterial();
            distant_grass_material.Shader = grass_material.Shader;
            // The terrain's physical edge ends this vegetation, not a camera ring.
            distant_grass_material.SetShaderParameter("fade_start", 730.0);
            distant_grass_material.SetShaderParameter("fade_end", 800.0);
        }
        ArrayMesh distant_blade = GrassPlanter.clump_mesh(5, 1, 0.055, 901);
        _upload_grass(planter.distant_chunks, distant_blade, distant_grass_material, "HillGrass");
        blade_count = planter.total_blades;
        distant_clump_count = planter.distant_clumps;
        G.print(G.format("Groundcover hills: %d clumps in %d batches, %d shrubs, extent %.0f m", new Godot.Collections.Array { distant_clump_count, (long)planter.distant_chunks.Count, distant_shrub_count, GrassPlanter.DISTANT_EXTENT }));
    }

    public void _upload_grass(Godot.Collections.Array<GrassPlanter.Chunk> chunks, ArrayMesh mesh, ShaderMaterial material, string prefix)
    {
        foreach (GrassPlanter.Chunk chunk in chunks)
        {
            MultiMesh mm = new MultiMesh();
            mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
            mm.UseColors = true;
            mm.UseCustomData = true;
            mm.Mesh = mesh;
            mm.InstanceCount = (int)chunk.count;
            mm.Buffer = chunk.buffer.ToArray();
            mm.CustomAabb = chunk.aabb;
            MultiMeshInstance3D mmi = new MultiMeshInstance3D();
            mmi.Name = G.format("%s_%d_%d", new Godot.Collections.Array { prefix, (long)chunk.origin.X, (long)chunk.origin.Y });
            mmi.Multimesh = mm;
            mmi.MaterialOverride = material;
            mmi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            mmi.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
            mmi.Layers = unchecked((uint)(Pond.GRASS_LAYER));
            _set_range(mmi, _grass_distance);
            AddChild(mmi);
            grass_chunks.Add(mmi);
        }
    }

    public void _build_distant_shrubs()
    {
        /// Low, irregular woodland undergrowth continues the existing shrub species.
        /// Reuse its reviewed mesh and a separate RNG so the campsite plants stay put.
        ArrayMesh mesh = shrub_mesh;
        ShaderMaterial material = shrub_material.Duplicate() as ShaderMaterial;
        material.SetShaderParameter("fade_start", 730.0);
        material.SetShaderParameter("fade_end", 800.0);
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(73109));
        Godot.Collections.Dictionary groups = new Godot.Collections.Dictionary();
        for (long attempt = 0; attempt < 4200; attempt++)
        {
            double angle = rng.Randf() * TAU;
            double radius = sqrt(lerpf(130.0 * 130.0, 650.0 * 650.0, rng.Randf()));
            Vector2 p = new Vector2((float)cos(angle), (float)sin(angle)) * (float)radius;
            double cover = field.woodland_cover(p.X, p.Y);
            if (rng.Randf() > cover * 0.70 * smoothstep(130.0, 190.0, radius))
            {
                continue;
            }
            double y = field.surface_height(p.X, p.Y);
            if (y < TerrainField.WATER_LEVEL + 0.2 || field.surface_slope(p.X, p.Y) > 0.68)
            {
                continue;
            }
            Vector2I key = new Vector2I((int)floori(p.X / 64.0), (int)floori(p.Y / 64.0));
            if (!groups.ContainsKey(key))
            {
                groups[key] = new Godot.Collections.Dictionary { { (StringName)"transforms", new Godot.Collections.Array() }, { (StringName)"customs", new Godot.Collections.Array() } };
            }
            double size = rng.RandfRange(0.9f, 1.65f);
            // Preserve the seeded draw order: scale before rotation.
            Vector3 shrubScale = new Vector3((float)size, (float)(size * rng.RandfRange(0.75f, 1.15f)), (float)size);
            Basis basis = new Basis(Vector3.Up, (float)(rng.Randf() * TAU)).Scaled(shrubScale);
            G.Call(G.Index(groups[key], "transforms"), "append", new Transform3D(basis, new Vector3(p.X, (float)(y - 0.06), p.Y)));
            Color tint = new Color(0.88f, 0.98f, 0.82f).Lerp(new Color(1.0f, 0.95f, 0.78f), (float)(rng.Randf() * 0.55));
            G.Call(G.Index(groups[key], "customs"), "append", new Color(rng.Randf(), tint.R, tint.G, tint.B));
            distant_shrub_count += 1;
        }
        foreach (Variant key_key in groups.Keys)
        {
            Vector2I key2 = key_key.AsVector2I();
            Godot.Collections.Array<Transform3D> transforms = new Godot.Collections.Array<Transform3D>();
            Godot.Collections.Array<Color> customs = new Godot.Collections.Array<Color>();
            G.assign(transforms, G.Index(groups[key2], "transforms").AsGodotArray<Transform3D>());
            G.assign(customs, G.Index(groups[key2], "customs").AsGodotArray<Color>());
            MultiMeshInstance3D node = _add_multimesh(G.format("HillShrubs_%d_%d", new Godot.Collections.Array { key2.X, key2.Y }), mesh, material, transforms, customs);
            node.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            Aabb bounds = transforms[0] * mesh.GetAabb().Grow(0.45f);
            foreach (Transform3D transform in transforms)
            {
                bounds = bounds.Merge(transform * mesh.GetAabb().Grow(0.45f));
            }
            node.Multimesh.CustomAabb = bounds;
        }
    }

    public void _build_ferns()
    {
        // --------------------------------------------------------------------- ferns
        fern_material = _card_material("fern", 0.42, 0.85, 0.55);
        ArrayMesh mesh = _fern_mesh();
        Godot.Collections.Array<Transform3D> transforms = new Godot.Collections.Array<Transform3D>();
        Godot.Collections.Array<Color> customs = new Godot.Collections.Array<Color>();
        long attempts = 0;
        while ((long)transforms.Count < FERN_COUNT && attempts < FERN_COUNT * 12)
        {
            attempts += 1;
            double a = _rng.Randf() * TAU;
            double r = lerpf(14.0, 78.0, sqrt(_rng.Randf()));
            Vector2 p = new Vector2((float)(cos(a) * r), (float)(sin(a) * r));
            double cover = field.canopy.sample(p.X, p.Y);
            if (_rng.Randf() > cover * 1.3 - 0.1)
            {
                continue;
            }
            if (!_plantable(p, 1.2))
            {
                continue;
            }
            double y = field.height(p.X, p.Y);
            double s = _rng.RandfRange(0.55f, 1.35f) * lerpf(0.8, 1.15, cover);
            transforms.Add(new Transform3D(new Basis(Vector3.Up, (float)(_rng.Randf() * TAU)).Scaled(Vector3.One * (float)s), new Vector3(p.X, (float)(y - 0.03), p.Y)));
            Color tint = new Color(0.9f, 1.0f, 0.85f).Lerp(new Color(1.0f, 0.95f, 0.7f), (float)(_rng.Randf() * 0.35));
            customs.Add(new Color(_rng.Randf(), tint.R, tint.G, tint.B));
        }
        _add_cells("Ferns", mesh, fern_material, transforms, customs, FERN_SHADOW_RANGE);
    }

    public ArrayMesh _fern_mesh()
    {
        /// A rosette of arching fronds, each a curved strip mapped to one atlas cell.
        MeshBuilder mb = new MeshBuilder();
        long fronds = 7;
        for (long f = 0; f < fronds; f++)
        {
            double yaw = (double)f / (double)fronds * TAU + _rng.RandfRange(-0.2f, 0.2f);
            Vector3 dir = new Vector3((float)cos(yaw), 0.0f, (float)sin(yaw));
            double length = _rng.RandfRange(0.75f, 1.05f);
            double width = length * 0.42;
            double tilt = _rng.RandfRange(0.0f, 0.25f);
            Vector2I cell = new Vector2I((int)((long)_rng.Randi() % 2), (int)((long)_rng.Randi() % 2));
            Rect2 rect = new Rect2((float)((double)cell.X * 0.5), (float)((double)cell.Y * 0.5), 0.5f, 0.5f);
            Vector3 side = dir.Cross(Vector3.Up).Normalized() * (float)(width * 0.5);
            // The atlas frond grows from a real rachis, connected to the crown.
            Godot.Collections.Array<Vector3> stem_points = new Godot.Collections.Array<Vector3> { new Vector3(0, 0.006f, 0) };
            Godot.Collections.Array<double> stem_radii = new Godot.Collections.Array<double> { 0.004 };
            for (long step = 0; step < 13; step++)
            {
                double t = (double)step / 12.0;
                stem_points.Add(dir * (float)length * (float)(0.15 + 0.85 * t) + Vector3.Up * (float)(length * (t - 0.72 * t * t) + tilt + 0.05));
                stem_radii.Add(lerpf(0.003, 0.0006, t));
            }
            long stem_start = mb.vertex_count();
            mb.add_tube(stem_points, stem_radii, 5, new Color(0.4f, 0.5f, 0, 1), 1, 1, 0, true);
            for (long vertex = stem_start, vertex_end = mb.vertex_count(); vertex < vertex_end; vertex++)
            {
                mb.uvs[(int)vertex] = new Vector2(-1, -1);
                Color _t1 = mb.colors[(int)vertex];
                _t1.R = (float)clampf(mb.vertices[(int)vertex].Length() / length, 0, 1);
                mb.colors[(int)vertex] = _t1;
            }
            long segments = 8;
            long prev_l = -1;
            long prev_r = -1;
            for (long i = 0, i_end = segments + 1; i < i_end; i++)
            {
                double t2 = (double)i / (double)segments;
                double horiz = length * (0.15 + 0.85 * t2);
                double vert = length * (1.0 * t2 - 0.72 * t2 * t2) + tilt + 0.05;
                Vector3 p = dir * (float)horiz + new Vector3(0.0f, (float)vert, 0.0f);
                double v = rect.Position.Y + rect.Size.Y * (1.0 - t2);
                Vector3 n = Vector3.Up;
                Vector4 tangent = new Vector4(side.Normalized().X, side.Normalized().Y, side.Normalized().Z, 1.0f);
                Color color = new Color((float)t2, _rng.Randf(), 0.0f, 1.0f);
                long l = mb.add_vertex(p - side, n, new Vector2(rect.Position.X, (float)v), color, tangent);
                long r = mb.add_vertex(p + side, n, new Vector2(rect.End.X, (float)v), color, tangent);
                if (prev_l >= 0)
                {
                    // Viewed from above with the frond growing away: prev_l, prev_r
                    // near, l, r far. Clockwise from above: prev_l -> l -> r -> prev_r.
                    mb.add_triangle(prev_l, l, r);
                    mb.add_triangle(prev_l, r, prev_r);
                }
                prev_l = l;
                prev_r = r;
            }
        }
        mb.recompute_normals();
        return mb.commit();
    }

    public void _build_shrubs()
    {
        // -------------------------------------------------------------------- shrubs
        // Bush cards are pale; against a low sun a strong backlight turned them
        // into glowing white leaves in the after-rain shot, so they transmit less.
        shrub_material = _card_material("bush", 0.45, 0.18, 0.6);
        ArrayMesh mesh = _shrub_mesh();
        Godot.Collections.Array<Transform3D> transforms = new Godot.Collections.Array<Transform3D>();
        Godot.Collections.Array<Color> customs = new Godot.Collections.Array<Color>();
        foreach (ScenePlan.ShrubEntry s in plan.shrubs)
        {
            double y = field.height(s.position.X, s.position.Y);
            transforms.Add(new Transform3D(new Basis(Vector3.Up, (float)s.rotation).Scaled(Vector3.One * (float)s.scale), new Vector3(s.position.X, (float)(y - 0.05), s.position.Y)));
            // The bush atlas is pale; a darker, greener tint keeps the cards from
            // reading as bleached against the sky.
            Color tint = new Color(0.62f, 0.72f, 0.56f).Lerp(new Color(0.70f, 0.66f, 0.48f), (float)(_rng.Randf() * 0.4));
            customs.Add(new Color(_rng.Randf(), tint.R, tint.G, tint.B));
        }
        shrub_mesh = mesh;
        // The planned bushes around the camp are placed as photoscanned shrubs by
        // ScannedDressing; the card mesh and material remain for the distant hills.
        if (CARD_SHRUBS_NEAR)
        {
            _add_cells("Shrubs", mesh, shrub_material, transforms, customs, SHRUB_SHADOW_RANGE, true);
        }
    }

    public ArrayMesh _shrub_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        long cards = 21;
        for (long c = 0; c < cards; c++)
        {
            double yaw = (double)c / (double)cards * TAU + _rng.RandfRange(-0.3f, 0.3f);
            Vector3 @out = new Vector3((float)cos(yaw), 0.0f, (float)sin(yaw));
            Vector3 pos = @out * _rng.RandfRange(0.15f, 0.5f) + new Vector3(0.0f, _rng.RandfRange(0.08f, 0.40f), 0.0f);
            double size = _rng.RandfRange(0.62f, 0.86f);
            Vector3 up = (Vector3.Up + @out * _rng.RandfRange(-0.2f, 0.35f)).Normalized();
            Vector3 right = up.Cross(@out);
            if (right.LengthSquared() < 1e-4)
            {
                right = Vector3.Right;
            }
            right = right.Normalized();
            Vector2I cell = new Vector2I((int)((long)_rng.Randi() % 2), (int)((long)_rng.Randi() % 2));
            Rect2 rect = new Rect2((float)((double)cell.X * 0.5), (float)((double)cell.Y * 0.5), 0.5f, 0.5f);
            double w = size * 0.9;
            Vector3 p0 = pos - right * (float)(w * 0.5);
            Vector3 p1 = pos + right * (float)(w * 0.5);
            Color color = new Color(0.15f, _rng.Randf(), 0.0f, 1.0f);
            Color color_top = new Color(1.0f, color.G, 0.0f, 1.0f);
            Vector3 n = (p1 - p0).Cross(up).Normalized();
            Vector4 tangent = new Vector4(right.X, right.Y, right.Z, 1.0f);
            long a = mb.add_vertex(p0, n, new Vector2(rect.Position.X, rect.End.Y), color, tangent);
            long b = mb.add_vertex(p1, n, new Vector2(rect.End.X, rect.End.Y), color, tangent);
            long cc = mb.add_vertex(p1 + up * (float)size, n, new Vector2(rect.End.X, rect.Position.Y), color_top, tangent);
            long d = mb.add_vertex(p0 + up * (float)size, n, new Vector2(rect.Position.X, rect.Position.Y), color_top, tangent);
            mb.add_quad_indices(a, b, cc, d);
        }
        return mb.commit();
    }

    public void _build_flowers()
    {
        // ------------------------------------------------------------------- flowers
        flower_material = new ShaderMaterial();
        flower_material.Shader = Content.Load<Shader>("res://shaders/meadow_plant.gdshader");
        Godot.Collections.Array<Godot.Collections.Array> groups = new Godot.Collections.Array<Godot.Collections.Array>();
        Godot.Collections.Array<Godot.Collections.Array> phases = new Godot.Collections.Array<Godot.Collections.Array>();
        for (long i = 0; i < 12; i++)
        {
            groups.Add(new Godot.Collections.Array());
            phases.Add(new Godot.Collections.Array());
        }
        Godot.Collections.Array<Godot.Collections.Dictionary> clusters = new Godot.Collections.Array<Godot.Collections.Dictionary> { new Godot.Collections.Dictionary { { (StringName)"centre", new Vector2(5.5f, 8.0f) }, { (StringName)"species", (long)MeadowPlants.Kind.YARROW }, { (StringName)"radius", 1.3 } }, new Godot.Collections.Dictionary { { (StringName)"centre", new Vector2(7.2f, 6.4f) }, { (StringName)"species", (long)MeadowPlants.Kind.BUTTERCUP }, { (StringName)"radius", 1.6 } }, new Godot.Collections.Dictionary { { (StringName)"centre", new Vector2(-8.0f, 6.6f) }, { (StringName)"species", (long)MeadowPlants.Kind.DAISY }, { (StringName)"radius", 1.5 } } };
        for (long i2 = 0; i2 < 52; i2++)
        {
            double a = _rng.Randf() * TAU;
            double r = lerpf(4.0, 39.0, sqrt(_rng.Randf()));
            clusters.Add(new Godot.Collections.Dictionary { { (StringName)"centre", new Vector2((float)(cos(a) * r), (float)(sin(a) * r)) }, { (StringName)"species", (long)_rng.Randi() % 4 }, { (StringName)"radius", _rng.RandfRange(1.0f, 2.8f) } });
        }
        long count = 0;
        for (long attempt = 0, attempt_end = FLOWER_COUNT * 10; attempt < attempt_end; attempt++)
        {
            if (count >= FLOWER_COUNT)
            {
                break;
            }
            Godot.Collections.Dictionary cluster = clusters[(int)((long)_rng.Randi() % (long)clusters.Count)];
            Vector2 p = G.op("+", cluster["centre"], G.op("*", new Vector2(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1)), cluster["radius"])).AsVector2();
            if (!_plantable(p, 0.45) || field.canopy.sample(p.X, p.Y) > 0.3)
            {
                continue;
            }
            if (field.grass_suitability(p.X, p.Y) < 0.6)
            {
                continue;
            }
            long species = cluster["species"].AsInt64();
            // Clover stays low in shorter sward; the taller flowers rise above it.
            if (species == (long)MeadowPlants.Kind.CLOVER && field.meadow_height(p.X, p.Y) > 0.37)
            {
                continue;
            }
            double scale_h = _rng.RandfRange(0.80f, 1.22f);
            long group = species * 3 + (long)_rng.Randi() % 3;
            groups[(int)group].Add(new Transform3D(new Basis(Vector3.Up, (float)(_rng.Randf() * TAU)).Scaled(new Vector3(1, (float)scale_h, 1)), new Vector3(p.X, (float)(field.height_fast(p.X, p.Y) - 0.008), p.Y)));
            phases[(int)group].Add(new Color(_rng.Randf(), 1, 1, 1));
            count += 1;
        }
        for (long group2 = 0; group2 < 12; group2++)
        {
            long species2 = group2 / 3;
            long variant = group2 % 3;
            Godot.Collections.Array<Transform3D> transforms = new Godot.Collections.Array<Transform3D>();
            Godot.Collections.Array<Color> customs = new Godot.Collections.Array<Color>();
            G.assign(transforms, new Godot.Collections.Array<Transform3D>(groups[(int)group2]));
            G.assign(customs, new Godot.Collections.Array<Color>(phases[(int)group2]));
            string node_name = G.format("Meadow_%d", species2) + (variant == 0 ? "" : G.format("_variant_%d", variant));
            _add_multimesh(node_name, MeadowPlants.flower(species2, 904 + species2 + variant * 311), flower_material, transforms, customs);
        }
        _build_meadow_grasses();
    }

    public void _build_meadow_grasses()
    {
        /// Basal tufts and fine seed stems share their habitat and root positions.
        /// Hairgrass-like open panicles favour the green moist stands; compact seed
        /// heads and older straw are more common in the drier openings. These are
        /// geometric growth forms, rather than a claim to identify the reference species.
        tussock_material = new ShaderMaterial();
        tussock_material.Shader = Content.Load<Shader>("res://shaders/grass.gdshader");
        tussock_material.SetShaderParameter("sheen", 0.12);
        // Broad radial tufts already have their real footprint; only the tiny base
        // sward needs distance widening to maintain apparent blade coverage.
        tussock_material.SetShaderParameter("distance_widen", 0.0);
        Godot.Collections.Array<ArrayMesh> tufts = new Godot.Collections.Array<ArrayMesh> { GrassPlanter.clump_mesh(24, 5, 0.011, 4021, 3.0, 2.15, 0.18), GrassPlanter.clump_mesh(18, 5, 0.020, 4059, 3.2, 2.5, 0.26) };
        Godot.Collections.Array<ArrayMesh> heads = new Godot.Collections.Array<ArrayMesh> { MeadowPlants.seed_heads(0, 4021), MeadowPlants.seed_heads(1, 4059) };
        Godot.Collections.Dictionary groups = _plan_meadow_grasses();
        foreach (Variant key_key in groups.Keys)
        {
            Vector3I key = key_key.AsVector3I();
            Godot.Collections.Dictionary group = groups[key].AsGodotDictionary();
            Godot.Collections.Array<Transform3D> transforms = new Godot.Collections.Array<Transform3D>();
            Godot.Collections.Array<Color> customs = new Godot.Collections.Array<Color>();
            Godot.Collections.Array<Color> colors = new Godot.Collections.Array<Color>();
            G.assign(transforms, group["transforms"].AsGodotArray<Transform3D>());
            G.assign(customs, group["customs"].AsGodotArray<Color>());
            G.assign(colors, group["colors"].AsGodotArray<Color>());
            string suffix = G.format("%d_%d_%d", new Godot.Collections.Array { key.X, key.Y, key.Z });
            MultiMeshInstance3D tuft = _add_multimesh("MeadowTussocks_" + suffix, tufts[key.Z], tussock_material, transforms, customs, false, colors);
            tuft.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            _set_meadow_bounds(tuft, transforms);
            _set_range(tuft, _grass_distance);
            meadow_cells.Add(tuft);
            G.assign(transforms, group["seed_transforms"].AsGodotArray<Transform3D>());
            G.assign(customs, group["seed_customs"].AsGodotArray<Color>());
            if (!(transforms.Count == 0))
            {
                MultiMeshInstance3D seed_stems = _add_multimesh("MeadowSeedHeads_" + suffix, heads[key.Z], flower_material, transforms, customs);
                seed_stems.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                _set_meadow_bounds(seed_stems, transforms);
                _set_range(seed_stems, 110.0 * _foliage_distance);
                fern_cells.Add(seed_stems);
            }
        }
    }

    public Godot.Collections.Dictionary _plan_meadow_grasses(long attempts = 26000, double stand_extent = 96.0)
    {
        /// Pure placement stage, also exercised by the existing groundcover tests.
        /// Independent RNG does not couple tuft density to the flower/fern layout.
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(82317));
        FastNoiseLite patches = GrassPlanter.meadow_patch_noise();
        Godot.Collections.Dictionary groups = new Godot.Collections.Dictionary();
        for (long i = 0; i < attempts; i++)
        {
            Vector2 p = new Vector2(rng.RandfRange((float)-stand_extent, (float)stand_extent), rng.RandfRange((float)-stand_extent, (float)stand_extent));
            double radius = p.Length();
            if (radius > stand_extent || !_plantable(p, 0.65))
            {
                continue;
            }
            double cover = field.woodland_cover(p.X, p.Y);
            double patch = patches.GetNoise2D(p.X, p.Y);
            double colony = lerpf(0.30, 1.0, smoothstep(-0.40, 0.25, patch));
            double edge = 1.0 - smoothstep(stand_extent * 0.72, stand_extent, radius);
            if (rng.Randf() > colony * lerpf(0.95, 0.08, cover) * edge)
            {
                continue;
            }
            double width = rng.RandfRange(0.68f, 1.08f);
            // The widest arched mesh reaches 0.93 m from its root at unit scale.
            // Include room for the shader's ordinary wind deformation at the path.
            if (!_meadow_footprint_clear(p, width * 0.96 + 0.12))
            {
                continue;
            }
            double dry = field.dryness(p.X, p.Y);
            double mature = GrassPlanter.meadow_senescence(dry, cover, patch);
            // Contiguous plant communities, with limited overlap at their edges.
            long form = patch + dry * 0.45 < 0.12 ? 0 : 1;
            double h = rng.RandfRange(0.40f, 0.88f) * lerpf(1.0, 0.80, cover);
            double yaw = rng.Randf() * TAU;
            Vector3 at = new Vector3(p.X, (float)(field.height_fast(p.X, p.Y) - 0.018), p.Y);
            Transform3D transform = new Transform3D(new Basis(Vector3.Up, (float)yaw).Scaled(new Vector3((float)width, (float)h, (float)width)), at);
            Vector3I key = new Vector3I((int)floori(p.X / 16.0), (int)floori(p.Y / 16.0), (int)form);
            if (!groups.ContainsKey(key))
            {
                groups[key] = new Godot.Collections.Dictionary { { (StringName)"transforms", new Godot.Collections.Array() }, { (StringName)"customs", new Godot.Collections.Array() }, { (StringName)"colors", new Godot.Collections.Array() }, { (StringName)"seed_transforms", new Godot.Collections.Array() }, { (StringName)"seed_customs", new Godot.Collections.Array() } };
            }
            double phase = rng.Randf();
            G.Call(G.Index(groups[key], "transforms"), "append", transform);
            G.Call(G.Index(groups[key], "customs"), "append", new Color((float)phase, (float)(cos(yaw) * 0.5 + 0.5), (float)(sin(yaw) * 0.5 + 0.5), rng.RandfRange(0.10f, 0.34f)));
            // Basal tufts retain younger green leaves above the older short sward.
            G.Call(G.Index(groups[key], "colors"), "append", GrassPlanter._blade_tint(maxf(mature - 0.22, 0.0), cover, rng.Randf()));
            if (rng.Randf() < lerpf(0.38, 0.80, mature) * (1.0 - cover * 0.65))
            {
                Vector3 seed_scale = new Vector3((float)width, rng.RandfRange(0.55f, 0.92f), (float)width);
                G.Call(G.Index(groups[key], "seed_transforms"), "append", new Transform3D(new Basis(Vector3.Up, (float)(yaw + 0.4)).Scaled(seed_scale), at));
                Color tint = new Color(0.85f, 1.0f, 0.82f).Lerp(new Color(1.03f, 0.91f, 0.69f), (float)mature);
                G.Call(G.Index(groups[key], "seed_customs"), "append", new Color((float)phase, tint.R, tint.G, tint.B));
            }
        }
        return groups;
    }

    public bool _meadow_footprint_clear(Vector2 p, double radius)
    {
        // Distance to the path is 1-Lipschitz, so this protects the whole circular
        // footprint, including points between the sampled habitat boundary checks.
        if (field.walking_distance(p) < radius + 0.56 || TerrainField.camp_wear(p) > 0.08)
        {
            return false;
        }
        for (long side = 0; side < 16; side++)
        {
            double angle = (double)side * TAU / 16.0;
            Vector2 q = p + new Vector2((float)cos(angle), (float)sin(angle)) * (float)radius;
            if (field.walking_distance(q) < 0.56 || TerrainField.camp_wear(q) > 0.08)
            {
                return false;
            }
            if (field.height_fast(q.X, q.Y) < TerrainField.WATER_LEVEL + 0.10)
            {
                return false;
            }
        }
        return true;
    }

    public void _add_cells(string prefix, ArrayMesh mesh, Material material, Godot.Collections.Array<Transform3D> transforms, Godot.Collections.Array<Color> customs, double shadow_range, bool reflects = false)
    {
        /// Upload one placement set as 16 m cells. Each cell exists twice with the same
        /// instances: a shadow caster visible while the camera is within shadow_range and
        /// a non-caster visible from there to the shader fade, so the geometry is drawn
        /// once at any distance and only nearby plants enter the shadow cascades.
        Godot.Collections.Dictionary cells = new Godot.Collections.Dictionary();
        for (long i = 0, i_end = (long)transforms.Count; i < i_end; i++)
        {
            Vector3 origin = transforms[(int)i].Origin;
            Vector2I key = new Vector2I((int)floori(origin.X / FERN_CELL), (int)floori(origin.Z / FERN_CELL));
            if (!cells.ContainsKey(key))
            {
                cells[key] = new Godot.Collections.Dictionary { { (StringName)"transforms", new Godot.Collections.Array() }, { (StringName)"customs", new Godot.Collections.Array() } };
            }
            G.Call(G.Index(cells[key], "transforms"), "append", transforms[(int)i]);
            G.Call(G.Index(cells[key], "customs"), "append", customs[(int)i]);
        }
        foreach (Variant key_key in cells.Keys)
        {
            Vector2I key2 = key_key.AsVector2I();
            Godot.Collections.Array<Transform3D> cell_transforms = new Godot.Collections.Array<Transform3D>();
            Godot.Collections.Array<Color> cell_customs = new Godot.Collections.Array<Color>();
            G.assign(cell_transforms, G.Index(cells[key2], "transforms").AsGodotArray<Transform3D>());
            G.assign(cell_customs, G.Index(cells[key2], "customs").AsGodotArray<Color>());
            MultiMeshInstance3D near = _add_multimesh(G.format("%s_%d_%d", new Godot.Collections.Array { prefix, key2.X, key2.Y }), mesh, material, cell_transforms, cell_customs, reflects);
            _set_meadow_bounds(near, cell_transforms);
            double half = near.Multimesh.CustomAabb.Size.Length() * 0.5 + 2.0;
            near.VisibilityRangeEnd = (float)(shadow_range + half);
            MultiMeshInstance3D far = _add_multimesh(G.format("%sFar_%d_%d", new Godot.Collections.Array { prefix, key2.X, key2.Y }), mesh, material, cell_transforms, cell_customs, reflects);
            far.Multimesh.CustomAabb = near.Multimesh.CustomAabb;
            far.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            // A slight overlap draws both twins for one frame rather than neither.
            far.VisibilityRangeBegin = (float)(shadow_range + half - 0.5);
            far.VisibilityRangeEnd = (float)(110.0 * _foliage_distance + half);
            shadow_cells.Add(new Godot.Collections.Dictionary { { (StringName)"near", near }, { (StringName)"far", far }, { (StringName)"half", half } });
        }
    }

    public static void _set_range(MultiMeshInstance3D node, double fade_end)
    {
        /// Stop drawing a batch once the shader has dissolved all of it. The renderer
        /// measures the range to the batch bounds' centre, so the cull distance adds
        /// half the bounds' diagonal; the fade already finished well before that.
        Aabb bounds = node.Multimesh.CustomAabb;
        if (bounds.Size == Vector3.Zero)
        {
            bounds = node.Multimesh.Mesh.GetAabb();
        }
        node.VisibilityRangeEnd = (float)(fade_end + bounds.Size.Length() * 0.5 + 2.0);
        node.VisibilityRangeEndMargin = 4.0f;
    }

    public void _set_meadow_bounds(MultiMeshInstance3D node, Godot.Collections.Array<Transform3D> transforms)
    {
        Aabb local_bounds = node.Multimesh.Mesh.GetAabb().Grow(0.28f);
        Aabb bounds = transforms[0] * local_bounds;
        foreach (Transform3D transform in transforms)
        {
            bounds = bounds.Merge(transform * local_bounds);
        }
        node.Multimesh.CustomAabb = bounds;
    }

    // --------------------------------------------------------------------- reeds
    public const long REED_CLUMPS = 30;

    public void _build_reeds()
    {
        /// Cattails and tall reeds in stands along the shallows, skipping the dock.
        reed_material = new ShaderMaterial();
        reed_material.Shader = Content.Load<Shader>("res://shaders/grass.gdshader");
        reed_material.SetShaderParameter("root_color", new Color(0.09f, 0.13f, 0.05f));
        reed_material.SetShaderParameter("tip_color", new Color(0.4f, 0.47f, 0.18f));
        reed_material.SetShaderParameter("dry_tip_color", new Color(0.52f, 0.44f, 0.2f));
        reed_material.SetShaderParameter("sheen", 0.2);
        reed_material.SetShaderParameter("trample_radius", 0.7);
        reed_material.SetShaderParameter("fade_start", 90.0);
        reed_material.SetShaderParameter("fade_end", 130.0);
        ArrayMesh mesh = GrassPlanter.reed_mesh();
        Godot.Collections.Array<Transform3D> transforms = new Godot.Collections.Array<Transform3D>();
        Godot.Collections.Array<Color> customs = new Godot.Collections.Array<Color>();
        Godot.Collections.Array<Color> colors = new Godot.Collections.Array<Color>();
        Vector2 dock_dir = (TerrainField.POND_CENTRE - TerrainField.DOCK_START).Normalized();
        for (long c = 0; c < REED_CLUMPS; c++)
        {
            double angle = _rng.Randf() * TAU;
            Vector2 centre = TerrainField.shore_point(angle);
            // Keep the dock approach and the beach by the trail clear.
            Vector2 to_dock = centre - TerrainField.DOCK_START;
            if (to_dock.Length() < 5.0 || absf(to_dock.Dot(new Vector2(-dock_dir.Y, dock_dir.X))) < 2.2 && to_dock.Dot(dock_dir) > -1.0 && to_dock.Dot(dock_dir) < 10.0)
            {
                continue;
            }
            double radius = _rng.RandfRange(1.4f, 3.2f);
            long count = (long)(radius * radius * _rng.RandfRange(2.2f, 3.4f));
            for (long i = 0; i < count; i++)
            {
                Vector2 p = centre + new Vector2(_rng.RandfRange(-1.0f, 1.0f), _rng.RandfRange(-1.0f, 1.0f)) * (float)radius;
                double y = field.height_fast(p.X, p.Y);
                double depth = TerrainField.WATER_LEVEL - y;
                if (depth > 0.42 || depth < -0.18)
                {
                    continue;
                }
                double s = _rng.RandfRange(0.85f, 1.15f);
                double h = _rng.RandfRange(1.15f, 1.7f) * (1.0 - clampf(depth, 0.0, 0.4) * 0.5);
                transforms.Add(new Transform3D(new Basis(Vector3.Up, (float)(_rng.Randf() * TAU)).Scaled(new Vector3((float)s, (float)h, (float)s)), new Vector3(p.X, (float)(y - 0.02), p.Y)));
                double lean = _rng.Randf() * TAU;
                customs.Add(new Color(_rng.Randf(), (float)(cos(lean) * 0.5 + 0.5), (float)(sin(lean) * 0.5 + 0.5), _rng.RandfRange(0.05f, 0.2f)));
                double dry = _rng.RandfRange(0.0f, 0.6f);
                colors.Add(new Color(_rng.RandfRange(0.85f, 1.05f), _rng.RandfRange(0.9f, 1.05f), _rng.RandfRange(0.8f, 1.0f), (float)dry));
            }
        }
        if ((transforms.Count == 0))
        {
            return;
        }
        MultiMesh mm = new MultiMesh();
        mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        mm.UseColors = true;
        mm.UseCustomData = true;
        mm.Mesh = mesh;
        mm.InstanceCount = (int)(long)transforms.Count;
        for (long i2 = 0, i_end = (long)transforms.Count; i2 < i_end; i2++)
        {
            mm.SetInstanceTransform((int)i2, transforms[(int)i2]);
            mm.SetInstanceColor((int)i2, colors[(int)i2]);
            mm.SetInstanceCustomData((int)i2, customs[(int)i2]);
        }
        MultiMeshInstance3D mmi = new MultiMeshInstance3D();
        mmi.Name = "Reeds";
        mmi.Multimesh = mm;
        mmi.MaterialOverride = reed_material;
        mmi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        mmi.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        AddChild(mmi);
    }

    public bool _plantable(Vector2 p, double clearance)
    {
        // ------------------------------------------------------------------- helpers
        if (field.is_underwater(p.X, p.Y) || field.height(p.X, p.Y) < TerrainField.WATER_LEVEL + 0.25)
        {
            return false;
        }
        if (field.walking_distance(p) < clearance + 0.55)
        {
            return false;
        }
        if (TerrainField.camp_wear(p) > 0.08)
        {
            return false;
        }
        return true;
    }

    public ShaderMaterial _card_material(string atlas, double cutoff, double translucency, double roughness)
    {
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>("res://shaders/undergrowth.gdshader");
        Camp.bind_texture(mat, "albedo_tex", G.format("res://textures/%s.png", atlas));
        Camp.bind_texture(mat, "normal_trans_tex", G.format("res://textures/%s_nt.png", atlas));
        mat.SetShaderParameter("alpha_cutoff", cutoff);
        mat.SetShaderParameter("translucency", translucency);
        mat.SetShaderParameter("roughness", roughness);
        return mat;
    }

    public MultiMeshInstance3D _add_multimesh(string node_name, ArrayMesh mesh, Material material, Godot.Collections.Array<Transform3D> transforms, Godot.Collections.Array<Color> customs, bool reflects = false, Godot.Collections.Array<Color> colors = null)
    {
        colors ??= new Godot.Collections.Array<Color>();
        MultiMesh mm = new MultiMesh();
        mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
        mm.UseCustomData = true;
        mm.UseColors = !(colors.Count == 0);
        mm.Mesh = mesh;
        mm.InstanceCount = (int)(long)transforms.Count;
        for (long i = 0, i_end = (long)transforms.Count; i < i_end; i++)
        {
            mm.SetInstanceTransform((int)i, transforms[(int)i]);
            mm.SetInstanceCustomData((int)i, customs[(int)i]);
            if (mm.UseColors)
            {
                mm.SetInstanceColor((int)i, colors[(int)i]);
            }
        }
        MultiMeshInstance3D mmi = new MultiMeshInstance3D();
        mmi.Name = node_name;
        mmi.Multimesh = mm;
        mmi.MaterialOverride = material;
        mmi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        mmi.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        mmi.Layers = unchecked((uint)(reflects ? 1 : Pond.GRASS_LAYER));
        AddChild(mmi);
        return mmi;
    }

    public void apply_quality(QualityPreset p)
    {
        _foliage_distance = p.foliage_distance;
        foreach (Variant mat in new Godot.Collections.Array { grass_material, tussock_material })
        {
            if (mat.VariantType != Variant.Type.Nil)
            {
                G.Call(mat, "set_shader_parameter", "fade_start", p.grass_distance * 0.72);
                G.Call(mat, "set_shader_parameter", "fade_end", p.grass_distance);
            }
        }
        foreach (Variant mat2 in new Godot.Collections.Array { fern_material, shrub_material, flower_material })
        {
            if (mat2.VariantType != Variant.Type.Nil)
            {
                G.Call(mat2, "set_shader_parameter", "fade_start", 70.0 * p.foliage_distance);
                G.Call(mat2, "set_shader_parameter", "fade_end", 110.0 * p.foliage_distance);
            }
        }
        foreach (MultiMeshInstance3D node in grass_chunks)
        {
            _set_range(node, p.grass_distance);
        }
        foreach (MultiMeshInstance3D node2 in meadow_cells)
        {
            _set_range(node2, p.grass_distance);
        }
        foreach (MultiMeshInstance3D node3 in fern_cells)
        {
            _set_range(node3, 110.0 * p.foliage_distance);
        }
        foreach (Godot.Collections.Dictionary pair in shadow_cells)
        {
            MultiMeshInstance3D far = pair["far"].As<MultiMeshInstance3D>();
            far.VisibilityRangeEnd = G.op("+", 110.0 * p.foliage_distance, pair["half"]).AsSingle();
        }
        if (!is_equal_approx(p.grass_density, _grass_density) || !is_equal_approx(p.grass_distance, _grass_distance))
        {
            _grass_density = p.grass_density;
            _grass_distance = p.grass_distance;
            _replant_grass_async();
        }
    }

    public async void _replant_grass_async()
    {
        /// Quality changes re-plan the grass on a worker thread and swap it in. Only
        /// one plan runs at a time; changes made meanwhile queue a single re-run.
        if (_replant_thread != null)
        {
            _replant_dirty = true;
            return;
        }
        GrassPlanter planter = new GrassPlanter(field, _grass_distance + GrassPlanter.CHUNK_SIZE, _grass_density);
        _replant_thread = new GodotThread();
        _replant_thread.Start(Callable.From(() => planter.plan()));
        while (_replant_thread != null && _replant_thread.IsAlive())
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (_replant_thread == null || !IsInsideTree())
        {
            return;
        }
        _replant_thread.WaitToFinish();
        _replant_thread = null;
        add_grass(planter);
        if (_replant_dirty)
        {
            _replant_dirty = false;
            _replant_grass_async();
        }
    }

    public override void _ExitTree()
    {
        /// A plan still running when the scene is torn down must be joined, or its
        /// detached thread keeps touching freed memory during shutdown.
        if (_replant_thread != null && _replant_thread.IsStarted())
        {
            _replant_thread.WaitToFinish();
        }
        _replant_thread = null;
    }
}
