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

/// Plans individual grass blades into square chunks so each chunk can be
/// frustum-culled independently. Blade placement follows the terrain's grass
/// suitability (open, gentle, dry ground) and clumps around tuft centres.
/// Output is a MultiMesh buffer per chunk, ready to upload.
public partial class GrassPlanter
{
    public partial class Chunk : RefCounted
    {
        public Vector2 origin;
        public long count = 0;
        public List<float> buffer = new List<float>();
        public Aabb aabb = new Aabb();
    }

    public const double CHUNK_SIZE = 8.0;
    /// Stand density multiplier over the authored clump count: the meadow is
    /// meant to read as thick grass everywhere the camera can see.
    public const double NEAR_DENSITY_BOOST = 3.2;
    public const long FLOATS_PER_INSTANCE = 20;
    public const double SUITABILITY_RESOLUTION = 1.0;
    public const double CLUMPS_PER_SQUARE_METRE = 22.0;
    /// Retains the established three-worker placement budget. Retune only after
    /// profiling the C# export together with the renderer's own worker demand.
    public const long PLANNING_TASKS = 3;
    /// Small blade groups replace subpixel near clumps with a broad overlap.
    /// Larger spatial batches keep the distant hills cheap to cull and draw.
    public const double DISTANT_CHUNK_SIZE = 32.0;
    public const double DISTANT_EXTENT = 690.0;

    public TerrainField field;
    public double extent;
    public double density;
    public long seed_value = 424242;
    public ScalarField suitability;
    public Godot.Collections.Array<GrassPlanter.Chunk> chunks = new Godot.Collections.Array<GrassPlanter.Chunk>();
    public long total_blades = 0;
    public Godot.Collections.Array<GrassPlanter.Chunk> distant_chunks = new Godot.Collections.Array<GrassPlanter.Chunk>();
    public long distant_clumps = 0;
    public FastNoiseLite patches = meadow_patch_noise();

    public GrassPlanter(TerrainField p_field, double p_extent, double p_density, long p_seed = 424242)
    {
        field = p_field;
        extent = p_extent;
        density = p_density;
        seed_value = p_seed;
    }

    public void bake_suitability()
    {
        /// Bakes suitability once at 1 m so per-blade queries stay cheap.
        long res = (long)(extent * 2.0 / SUITABILITY_RESOLUTION) + 1;
        suitability = new ScalarField(res, extent * 2.0, 0.0);
        suitability.fill_with(Callable.From((double x, double z) =>
{
    if (new Vector2((float)x, (float)z).Length() > extent)
    {
        return 0.0;
    }
    return field.grass_suitability(x, z, field.height_fast(x, z));
}));
    }

    public void plan()
    {
        /// Plans every chunk. Chunks are independent, so they are distributed over the
        /// worker thread pool; each uses its own RNG seeded from the chunk index so the
        /// result is deterministic regardless of thread scheduling.
        if (suitability == null)
        {
            bake_suitability();
        }
        chunks.Clear();
        total_blades = 0;
        distant_chunks.Clear();
        distant_clumps = 0;
        Godot.Collections.Array<Vector2> origins = new Godot.Collections.Array<Vector2>();
        long half_chunks = (long)ceil(extent / CHUNK_SIZE);
        for (long cz = -half_chunks; cz < half_chunks; cz++)
        {
            for (long cx = -half_chunks; cx < half_chunks; cx++)
            {
                Vector2 origin = new Vector2((float)((double)cx * CHUNK_SIZE), (float)((double)cz * CHUNK_SIZE));
                Vector2 centre = origin + Vector2.One * (float)(CHUNK_SIZE * 0.5);
                if (centre.Length() > extent + CHUNK_SIZE)
                {
                    continue;
                }
                origins.Add(origin);
            }
        }
        Godot.Collections.Array results = new Godot.Collections.Array();
        G.resize(results, (int)(long)origins.Count);
        long base_seed = seed_value;
        Callable task = Callable.From((long index) =>
{
    RandomNumberGenerator local_rng = new RandomNumberGenerator();
    // Stable world-cell seeds retain the same plants when quality changes
    // the extent; indexing the origins array moved every clump instead.
    Vector2 cell = origins[(int)index] / (float)CHUNK_SIZE;
    local_rng.Seed = unchecked((ulong)(base_seed + (long)cell.X * 73856093 + (long)cell.Y * 19349663));
    results[(int)index] = _plan_chunk(origins[(int)index], local_rng);
});
        long group = WorkerThreadPool.AddGroupTask(task, (int)(long)origins.Count, (int)PLANNING_TASKS, false, "grass");
        WorkerThreadPool.WaitForGroupTaskCompletion(group);
        foreach (Variant r in results)
        {
            GrassPlanter.Chunk chunk = r.As<GrassPlanter.Chunk>();
            if (chunk != null && chunk.count > 0)
            {
                chunks.Add(chunk);
                total_blades += chunk.count;
            }
        }
        if (extent >= 200.0)
        {
            _plan_distant();
        }
    }

    public double _chunk_coverage(Vector2 origin)
    {
        /// Average suitability over the chunk decides how many candidates to try.
        double sum = 0.0;
        for (long iz = 0; iz < 4; iz++)
        {
            for (long ix = 0; ix < 4; ix++)
            {
                Vector2 p = origin + new Vector2((float)(((double)ix + 0.5) * CHUNK_SIZE / 4.0), (float)(((double)iz + 0.5) * CHUNK_SIZE / 4.0));
                sum += suitability.sample(p.X, p.Y);
            }
        }
        return sum / 16.0;
    }

    public GrassPlanter.Chunk _plan_chunk(Vector2 origin, RandomNumberGenerator rng)
    {
        GrassPlanter.Chunk chunk = new GrassPlanter.Chunk();
        chunk.origin = origin;
        double coverage = _chunk_coverage(origin);
        if (coverage < 0.02)
        {
            return chunk;
        }
        // Candidates are not scaled by the chunk's average cover: per-blade
        // rejection alone shapes the density, so chunk borders never show.
        double area = CHUNK_SIZE * CHUNK_SIZE;
        double range_from_camp = (origin + Vector2.One * (float)CHUNK_SIZE * 0.5f).Length();
        double distance_scale = lerpf(1.0, 0.12, smoothstep(32.0, 160.0, range_from_camp));
        distance_scale *= lerpf(1.0, 0.10, smoothstep(140.0, 300.0, range_from_camp));
        double footprint = 1.0 / sqrt(distance_scale);
        long candidates = (long)(area * density * CLUMPS_PER_SQUARE_METRE * NEAR_DENSITY_BOOST * distance_scale);
        List<float> floats = new List<float>();
        G.resize(floats, (int)(candidates * FLOATS_PER_INSTANCE));
        long count = 0;
        Vector3 min_pos = new Vector3((float)INF, (float)INF, (float)INF);
        Vector3 max_pos = new Vector3((float)-INF, (float)-INF, (float)-INF);
        // Tufts: blades cluster around a few dozen centres per chunk.

        List<Vector2> tufts = new List<Vector2>();
        long tuft_count = 26;
        for (long i = 0; i < tuft_count; i++)
        {
            tufts.Add(origin + new Vector2((float)(rng.Randf() * CHUNK_SIZE), (float)(rng.Randf() * CHUNK_SIZE)));
        }

        for (long i2 = 0; i2 < candidates; i2++)
        {
            Vector2 p = default;
            if (rng.Randf() < 0.22)
            {
                Vector2 t = tufts[(int)((long)rng.Randi() % tuft_count)];
                double r = rng.RandfRange(0.0f, 0.55f) * sqrt(rng.Randf());
                double a = rng.Randf() * TAU;
                p = t + new Vector2((float)cos(a), (float)sin(a)) * (float)r;
                if (p.X < origin.X || p.Y < origin.Y || p.X >= origin.X + CHUNK_SIZE || p.Y >= origin.Y + CHUNK_SIZE)
                {
                    continue;
                }
            }
            else
            {
                p = origin + new Vector2((float)(rng.Randf() * CHUNK_SIZE), (float)(rng.Randf() * CHUNK_SIZE));
            }
            double suit = suitability.sample(p.X, p.Y);
            if (rng.Randf() > suit)
            {
                continue;
            }
            // Exact clearance at a narrow walking route cannot be reconstructed from
            // the one-metre suitability grid alone.
            if (p.X > -19.0 && p.X < 12.0 && p.Y > -8.0 && p.Y < 73.0)
            {
                if (field.walking_distance(p) < 0.52 || TerrainField.camp_wear(p) > 0.94)
                {
                    continue;
                }
            }
            double y = field.height_fast(p.X, p.Y);
            if (maxf(absf(p.X), absf(p.Y)) > TerrainBuilder.INNER_UNIFORM_HALF)
            {
                y = field.surface_height(p.X, p.Y);
            }
            if (y < TerrainField.WATER_LEVEL + 0.05)
            {
                continue;
            }
            double dry = field.dryness(p.X, p.Y);
            double cover = field.woodland_cover(p.X, p.Y);
            double patch = patches.GetNoise2D(p.X, p.Y);
            double growth = patches.GetNoise2D((float)(p.X + 73.3), (float)(p.Y - 41.7));
            double mature = meadow_senescence(dry, cover, patch);
            // A continuous shorter sward supports the larger fountain-shaped tufts.
            // Older leaves tend to settle into the lower sward. A separate regrowth
            // pattern avoids every pale patch also becoming a uniformly tall island.
            double height = field.meadow_height(p.X, p.Y) * 0.82 * rng.RandfRange(0.45f, 1.13f);
            height *= lerpf(0.78, 1.06, smoothstep(-0.40, 0.40, growth)) * lerpf(1.0, 0.74, cover);
            height *= lerpf(1.0, 0.80, mature);
            double yaw = rng.Randf() * TAU;
            double lean_angle = yaw + rng.RandfRange(-0.6f, 0.6f);
            Color tint = _blade_tint(mature, cover, rng.Randf());
            // Inline the 3x4 transform (rotation about Y, y-scaled) to avoid allocations.
            double c = cos(yaw);
            double s = sin(yaw);
            long o = count * FLOATS_PER_INSTANCE;
            floats[(int)(o + 0)] = (float)(c * footprint);
            floats[(int)(o + 1)] = 0.0f;
            floats[(int)(o + 2)] = (float)(s * footprint);
            floats[(int)(o + 3)] = p.X;
            floats[(int)(o + 4)] = 0.0f;
            floats[(int)(o + 5)] = (float)height;
            floats[(int)(o + 6)] = 0.0f;
            floats[(int)(o + 7)] = (float)(y - 0.02);
            floats[(int)(o + 8)] = (float)(-s * footprint);
            floats[(int)(o + 9)] = 0.0f;
            floats[(int)(o + 10)] = (float)(c * footprint);
            floats[(int)(o + 11)] = p.Y;
            floats[(int)(o + 12)] = tint.R;
            floats[(int)(o + 13)] = tint.G;
            floats[(int)(o + 14)] = tint.B;
            floats[(int)(o + 15)] = tint.A;
            floats[(int)(o + 16)] = rng.Randf();
            floats[(int)(o + 17)] = (float)(cos(lean_angle) * 0.5 + 0.5);
            floats[(int)(o + 18)] = (float)(sin(lean_angle) * 0.5 + 0.5);
            floats[(int)(o + 19)] = rng.RandfRange(0.15f, 0.6f);
            count += 1;
            min_pos = new Vector3((float)minf(min_pos.X, p.X), (float)minf(min_pos.Y, y), (float)minf(min_pos.Z, p.Y));
            max_pos = new Vector3((float)maxf(max_pos.X, p.X), (float)maxf(max_pos.Y, y + height), (float)maxf(max_pos.Z, p.Y));
        }

        G.resize(floats, (int)(count * FLOATS_PER_INSTANCE));
        chunk.count = count;
        chunk.buffer = floats;
        if (count > 0)
        {
            double margin = maxf(0.6, footprint * 0.8);
            chunk.aabb = new Aabb(min_pos - new Vector3((float)margin, 0.2f, (float)margin), max_pos - min_pos + new Vector3((float)(margin * 2.0), 0.6f, (float)(margin * 2.0)));
        }
        return chunk;
    }

    public void _plan_distant()
    {
        Godot.Collections.Array<Vector2> origins = new Godot.Collections.Array<Vector2>();
        long half_chunks = ceili(DISTANT_EXTENT / DISTANT_CHUNK_SIZE);
        for (long z = -half_chunks; z < half_chunks; z++)
        {
            for (long x = -half_chunks; x < half_chunks; x++)
            {
                Vector2 origin = new Vector2(x, z) * (float)DISTANT_CHUNK_SIZE;
                double radius = (origin + Vector2.One * (float)DISTANT_CHUNK_SIZE * 0.5f).Length();
                if (radius > 110.0 && radius < DISTANT_EXTENT + DISTANT_CHUNK_SIZE)
                {
                    origins.Add(origin);
                }
            }
        }
        Godot.Collections.Array results = new Godot.Collections.Array();
        G.resize(results, (int)(long)origins.Count);
        Callable task = Callable.From((long index) =>
{
    Vector2 cell = origins[(int)index] / (float)DISTANT_CHUNK_SIZE;
    RandomNumberGenerator rng = new RandomNumberGenerator();
    rng.Seed = unchecked((ulong)(seed_value + 7301 + (long)cell.X * 73856093 + (long)cell.Y * 19349663));
    results[(int)index] = _plan_distant_chunk(origins[(int)index], rng);
});
        long group = WorkerThreadPool.AddGroupTask(task, (int)(long)origins.Count, (int)PLANNING_TASKS, false, "hill groundcover");
        WorkerThreadPool.WaitForGroupTaskCompletion(group);
        foreach (Variant result in results)
        {
            GrassPlanter.Chunk chunk = result.As<GrassPlanter.Chunk>();
            if (chunk.count > 0)
            {
                distant_chunks.Add(chunk);
                distant_clumps += chunk.count;
            }
        }
    }

    public GrassPlanter.Chunk _plan_distant_chunk(Vector2 origin, RandomNumberGenerator rng)
    {
        GrassPlanter.Chunk chunk = new GrassPlanter.Chunk();
        chunk.origin = origin;
        double quality_scale = lerpf(0.55, 1.0, clampf(density, 0.0, 1.0));
        long candidates = ceili(DISTANT_CHUNK_SIZE * DISTANT_CHUNK_SIZE * 0.62 * quality_scale);
        List<float> floats = new List<float>();
        G.resize(floats, (int)(candidates * FLOATS_PER_INSTANCE));
        Vector3 min_pos = new Vector3((float)INF, (float)INF, (float)INF);
        Vector3 max_pos = new Vector3((float)-INF, (float)-INF, (float)-INF);
        for (long i = 0; i < candidates; i++)
        {
            Vector2 p = origin + new Vector2(rng.Randf(), rng.Randf()) * (float)DISTANT_CHUNK_SIZE;
            double radius = p.Length();
            double band = smoothstep(120.0, 205.0, radius) * (1.0 - smoothstep(655.0, DISTANT_EXTENT, radius));
            double frequency = lerpf(0.62, 0.15, smoothstep(180.0, 650.0, radius));
            if (rng.Randf() > band * frequency / 0.62)
            {
                continue;
            }
            double y = field.surface_height(p.X, p.Y);
            double slope = field.surface_slope(p.X, p.Y);
            if (y < TerrainField.WATER_LEVEL + 0.05 || rng.Randf() < smoothstep(0.48, 0.95, slope))
            {
                continue;
            }
            double cover = field.woodland_cover(p.X, p.Y);
            double dry = field.dryness(p.X, p.Y);
            double size = lerpf(2.8, 4.4, smoothstep(200.0, 650.0, radius)) / sqrt(quality_scale);
            double height = field.meadow_height(p.X, p.Y) * rng.RandfRange(0.95f, 1.65f) * lerpf(1.15, 0.78, cover);
            double yaw = rng.Randf() * TAU;
            double c = cos(yaw);
            double s = sin(yaw);
            double mature = meadow_senescence(dry, cover, patches.GetNoise2D(p.X, p.Y));
            Color tint = _blade_tint(mature, cover, rng.Randf());
            long offset = chunk.count * FLOATS_PER_INSTANCE;
            floats[(int)offset] = (float)(c * size);
            floats[(int)(offset + 2)] = (float)(s * size);
            floats[(int)(offset + 3)] = p.X;
            floats[(int)(offset + 5)] = (float)height;
            floats[(int)(offset + 7)] = (float)(y - 0.025);
            floats[(int)(offset + 8)] = (float)(-s * size);
            floats[(int)(offset + 10)] = (float)(c * size);
            floats[(int)(offset + 11)] = p.Y;
            floats[(int)(offset + 12)] = tint.R;
            floats[(int)(offset + 13)] = tint.G;
            floats[(int)(offset + 14)] = tint.B;
            floats[(int)(offset + 15)] = tint.A;
            floats[(int)(offset + 16)] = rng.Randf();
            floats[(int)(offset + 17)] = (float)(c * 0.5 + 0.5);
            floats[(int)(offset + 18)] = (float)(s * 0.5 + 0.5);
            floats[(int)(offset + 19)] = rng.RandfRange(0.12f, 0.36f);
            chunk.count += 1;
            min_pos = min_pos.Min(new Vector3(p.X, (float)y, p.Y));
            max_pos = max_pos.Max(new Vector3(p.X, (float)(y + height), p.Y));
        }
        G.resize(floats, (int)(chunk.count * FLOATS_PER_INSTANCE));
        chunk.buffer = floats;
        if (chunk.count > 0)
        {
            chunk.aabb = new Aabb(min_pos - new Vector3(4.0f, 0.1f, 4.0f), max_pos - min_pos + new Vector3(8.0f, 0.7f, 8.0f));
        }
        return chunk;
    }

    public static FastNoiseLite meadow_patch_noise()
    {
        /// Stable metre-scale stands shared by the sward, tussocks and seed stems.
        /// The field has no chunk/quality boundaries and continues into distant cover.
        FastNoiseLite noise = new FastNoiseLite();
        noise.Seed = 70439;
        noise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        noise.Frequency = 0.18f;
        noise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        noise.FractalOctaves = 3;
        return noise;
    }

    public static double meadow_senescence(double dry, double cover, double patch)
    {
        // Keep a living green component even in drier stands. Wider noise thresholds
        // blend their boundaries, rather than dividing green turf from ripe crop.
        return clampf(0.10 + dry * 0.42 + smoothstep(-0.42, 0.50, patch) * 0.38 - cover * 0.30, 0.0, 0.72);
    }

    public static Color _blade_tint(double dry, double cover, double variation)
    {
        /// Moisture/shade establish the palette; maturity is carried in alpha for
        /// the shader's green-base/straw-tip blend. Fine variation is deliberately small.
        Color lush = new Color(0.82f, 1.0f, 0.90f);
        Color straw = new Color(1.06f, 0.99f, 0.89f);
        Color c = lush.Lerp(straw, (float)dry);
        c = c.Lerp(new Color(0.72f, 0.86f, 0.82f), (float)(cover * 0.55));
        c = c * (float)(0.92 + 0.16 * variation);
        c.A = (float)dry;
        return c;
    }

    public static ArrayMesh reed_mesh(long seed_value = 91)
    {
        /// A stand of reeds: tall thin blades and a couple of cattail stalks with
        /// brown heads, sharing the grass shader (UV/UV2 conventions as clump_mesh).
        /// Heights are fractions of the instance's y scale.
        MeshBuilder mb = new MeshBuilder();
        mb.use_tangents = false;
        mb.use_uv2 = true;
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        long blades = 8;
        for (long b = 0, b_end = blades + 2; b < b_end; b++)
        {
            bool is_cattail = b >= blades;
            double yaw = (double)b / (double)(blades + 2) * TAU + rng.RandfRange(-0.3f, 0.3f);
            Vector3 offset = new Vector3((float)cos(yaw), 0.0f, (float)sin(yaw)) * rng.RandfRange(0.02f, 0.12f);
            double height = !is_cattail ? rng.RandfRange(0.7f, 1.0f) : rng.RandfRange(0.85f, 1.0f);
            Vector3 lean = new Vector3((float)cos(yaw), 0.0f, (float)sin(yaw)) * rng.RandfRange(0.02f, 0.14f);
            double facing = yaw + rng.RandfRange(-0.6f, 0.6f);
            Vector3 right = new Vector3((float)cos(facing), 0.0f, (float)sin(facing));
            Vector3 normal = right.Cross(Vector3.Up).Normalized();
            Vector2 uv2 = new Vector2(rng.Randf(), (float)height);
            double half_w = (!is_cattail ? 0.011 : 0.007) * rng.RandfRange(0.8f, 1.2f);
            long segments = 5;
            Godot.Collections.Array<Godot.Collections.Array> rows = new Godot.Collections.Array<Godot.Collections.Array>();
            double stalk_top = height * (is_cattail ? 0.82 : 1.0);
            for (long i = 0, i_end = segments + 1; i < i_end; i++)
            {
                double v = (double)i / (double)segments;
                double taper = 1.0 - v * (!is_cattail ? 0.75 : 0.3);
                Vector3 p = offset + new Vector3(0.0f, (float)(v * stalk_top), 0.0f) + lean * (float)(v * v * height);
                long l = mb.add_vertex(p - right * (float)(half_w * taper), normal, new Vector2(0.0f, (float)(v * (!is_cattail ? 1.0 : 0.8))), Colors.White, new Vector4(1, 0, 0, 1), new Color(0, 0, 0, 0), uv2);
                long r = mb.add_vertex(p + right * (float)(half_w * taper), normal, new Vector2(1.0f, (float)(v * (!is_cattail ? 1.0 : 0.8))), Colors.White, new Vector4(1, 0, 0, 1), new Color(0, 0, 0, 0), uv2);
                rows.Add(new Godot.Collections.Array { l, r });
            }
            for (long i2 = 0; i2 < segments; i2++)
            {
                long a = rows[(int)i2][0].AsInt64();
                long bb = rows[(int)i2][1].AsInt64();
                long c = rows[(int)(i2 + 1)][1].AsInt64();
                long d = rows[(int)(i2 + 1)][0].AsInt64();
                mb.add_triangle(a, d, c);
                mb.add_triangle(a, c, bb);
            }
            if (!is_cattail)
            {
                Vector3 tip_p = offset + new Vector3(0.0f, (float)height, 0.0f) + lean * (float)height;
                long tip = mb.add_vertex(tip_p, normal, new Vector2(0.5f, 1.0f), Colors.White, new Vector4(1, 0, 0, 1), new Color(0, 0, 0, 0), uv2);
                Godot.Collections.Array last = rows[(int)segments];
                mb.add_triangle(last[0].AsInt64(), tip, last[1].AsInt64());
            }
            else
            {
                // Cattail head: a short brown sausage on top of the stalk.
                Vector3 @base = offset + new Vector3(0.0f, (float)stalk_top, 0.0f) + lean * (float)height;
                Vector3 top = @base + new Vector3(0.0f, (float)(height * 0.17), 0.0f);
                Color brown = Colors.White;
                long first = mb.vertex_count();
                mb.add_tube(new Godot.Collections.Array<Vector3> { @base, @base + Vector3.Up * 0.012f, top - Vector3.Up * 0.012f, top }, new Godot.Collections.Array<double> { 0.008, 0.018, 0.017, 0.007 }, 14, brown, 1.0, 1.0, 0.0, true);
                for (long i3 = first, i_end2 = mb.vertex_count(); i3 < i_end2; i3++)
                {
                    mb.uvs[(int)i3] = new Vector2(0.5f, 1.0f);
                    mb.uv2s[(int)i3] = new Vector2(-1, 1);
                }
            }
        }
        return mb.commit();
    }

    public static Transform3D read_transform(List<float> buffer, long index)
    {
        /// Reads one instance transform back out of a buffer (used by tests).
        long o = index * FLOATS_PER_INSTANCE;
        Basis basis = new Basis(new Vector3(buffer[(int)(o + 0)], buffer[(int)(o + 4)], buffer[(int)(o + 8)]), new Vector3(buffer[(int)(o + 1)], buffer[(int)(o + 5)], buffer[(int)(o + 9)]), new Vector3(buffer[(int)(o + 2)], buffer[(int)(o + 6)], buffer[(int)(o + 10)]));
        return new Transform3D(basis, new Vector3(buffer[(int)(o + 3)], buffer[(int)(o + 7)], buffer[(int)(o + 11)]));
    }

    public static ArrayMesh clump_mesh(long blades = 7, long segments = 5, double width = 0.018, long seed_value = 77, double spread = 1.0, double arch = 1.0, double droop = 0.0)
    {
        /// A clump of tapered blades sharing one instance. Blade width is in metres
        /// (the instance scales x/z by 1); heights are fractions of the instance's
        /// y scale. UV.x runs across a blade, UV.y from root (0) to tip (1).
        /// UV2 = (per-blade random, local vertical derivative per UV.y). Vertex colour stays white
        /// because Godot multiplies it with the instance colour.
        MeshBuilder mb = new MeshBuilder();
        mb.use_tangents = false;
        mb.use_uv2 = true;
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Seed = unchecked((ulong)(seed_value));
        for (long b = 0; b < blades; b++)
        {
            double yaw = (double)b / (double)blades * TAU + rng.RandfRange(-0.4f, 0.4f);
            Vector3 offset = new Vector3((float)cos(yaw), 0.0f, (float)sin(yaw)) * rng.RandfRange(0.0f, 0.07f) * (float)spread;
            double height = rng.RandfRange(0.55f, 1.0f);
            Vector3 lean = new Vector3((float)cos(yaw), 0.0f, (float)sin(yaw)) * rng.RandfRange(0.05f, 0.28f) * (float)arch;
            Vector3 curve = lean - Vector3.Up * (float)clampf(droop, 0.0, 0.40);
            double facing = yaw + rng.RandfRange(-0.7f, 0.7f);
            Vector3 right = new Vector3((float)cos(facing), 0.0f, (float)sin(facing));
            double blade_random = rng.Randf();
            double half_w = width * rng.RandfRange(0.7f, 1.15f) * 0.5;
            Godot.Collections.Array<Godot.Collections.Array> rows = new Godot.Collections.Array<Godot.Collections.Array>();
            for (long i = 0, i_end = segments + 1; i < i_end; i++)
            {
                double v = (double)i / (double)(segments + 1);
                double taper = 1.0 - v * 0.8;
                Vector3 p = offset + new Vector3(0.0f, (float)(v * height), 0.0f) + curve * (float)(v * v * height);
                // The width direction crossed with the curved centre-line tangent.
                // Taper adds a parallel width component, which cancels in the cross.
                Vector3 normal = right.Cross(Vector3.Up + curve * (float)(2.0 * v)).Normalized();
                Vector2 uv2 = new Vector2((float)blade_random, (float)(height * (1.0 + curve.Y * 2.0 * v)));
                long l = mb.add_vertex(p - right * (float)(half_w * taper), normal, new Vector2(0.0f, (float)v), Colors.White, new Vector4(1, 0, 0, 1), new Color(0, 0, 0, 0), uv2);
                long r = mb.add_vertex(p + right * (float)(half_w * taper), normal, new Vector2(1.0f, (float)v), Colors.White, new Vector4(1, 0, 0, 1), new Color(0, 0, 0, 0), uv2);
                rows.Add(new Godot.Collections.Array { l, r });
            }
            Vector3 tip_p = offset + new Vector3(0.0f, (float)height, 0.0f) + curve * (float)height;
            Vector3 tip_normal = right.Cross(Vector3.Up + curve * 2.0f).Normalized();
            long tip = mb.add_vertex(tip_p, tip_normal, new Vector2(0.5f, 1.0f), Colors.White, new Vector4(1, 0, 0, 1), new Color(0, 0, 0, 0), new Vector2((float)blade_random, (float)(height * (1.0 + curve.Y * 2.0))));
            for (long i2 = 0; i2 < segments; i2++)
            {
                long a = rows[(int)i2][0].AsInt64();
                long bb = rows[(int)i2][1].AsInt64();
                long c = rows[(int)(i2 + 1)][1].AsInt64();
                long d = rows[(int)(i2 + 1)][0].AsInt64();
                // Viewed from the front: a bottom-left, bb bottom-right, c top-right,
                // d top-left. Clockwise: a -> d -> c -> bb.
                mb.add_triangle(a, d, c);
                mb.add_triangle(a, c, bb);
            }
            Godot.Collections.Array last = rows[(int)segments];
            mb.add_triangle(last[0].AsInt64(), tip, last[1].AsInt64());
        }
        // Keep every blade at every LOD. General mesh decimation removes whole
        // blades because their borders prevent ordinary edge collapse.
        ArrayMesh mesh = new ArrayMesh();
        Godot.Collections.Dictionary lods = new Godot.Collections.Dictionary();
        if (segments >= 5)
        {
            lods = new Godot.Collections.Dictionary { { 0.006, Variant.From(clump_lod_indices(blades, segments, new List<int>(new List<int> { 0, 2, 4 })).ToArray()) }, { 0.018, Variant.From(clump_lod_indices(blades, segments, new List<int>(new List<int> { 0, 3 })).ToArray()) }, { 0.05, Variant.From(clump_lod_indices(blades, segments, new List<int>(new List<int> { 0 })).ToArray()) } };
        }
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, mb._arrays(), new Godot.Collections.Array<Godot.Collections.Array>(), lods, (Mesh.ArrayFormat)mb._format_flags());
        return mesh;
    }

    public static List<int> clump_lod_indices(long blades, long segments, List<int> retained_rows)
    {
        List<int> result = new List<int>();
        long stride = (segments + 1) * 2 + 1;
        for (long blade = 0; blade < blades; blade++)
        {
            long @base = blade * stride;
            for (long i = 0, i_end = (long)retained_rows.Count - 1; i < i_end; i++)
            {
                long a = @base + (long)retained_rows[(int)i] * 2;
                long b = @base + (long)retained_rows[(int)(i + 1)] * 2;
                result.AddRange(new List<int>(new List<int> { (int)a, (int)b, (int)(b + 1), (int)a, (int)(b + 1), (int)(a + 1) }));
            }
            long last = @base + (long)retained_rows[^1] * 2;
            result.AddRange(new List<int>(new List<int> { (int)last, (int)(@base + stride - 1), (int)(last + 1) }));
        }
        return result;
    }
}
