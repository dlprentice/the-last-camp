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

/// Builds the authored scene: survey, landscape, forest, understory, water,
/// the camp itself and its wildlife. Pure generation (terrain meshing, grass
/// planning) runs on worker threads while the main thread assembles nodes and
/// keeps the loading screen alive.
public partial class Camp : Node3D
{
    [Signal]
    public delegate void stage_startedEventHandler(string stage_name, long index, long total);

    public const long STAGE_COUNT = 7;

    public TerrainField field = new TerrainField();
    public ScenePlan plan;
    public ShaderMaterial terrain_material;
    public MeshInstance3D terrain;
    public StaticBody3D terrain_body;
    public Forest forest;
    public RidgeForest ridge_forest;
    public Understory understory;
    public Pond pond;
    public UnderwaterFX underwater_fx;
    public Campsite campsite;
    public CampRocks rocks;
    public Wildlife wildlife;
    public AudioDirector audio;
    public ShoreLife shore_life;

    public long _stage_index = 0;
    public string _stage_name = "";
    public long _stage_started_ms = 0;
    public GodotThread _terrain_thread;
    public GodotThread _grass_thread;

    public override void _Ready()
    {
        Game.Instance.camp = this;
        RenderingServer.GlobalShaderParameterSet("water_level", TerrainField.WATER_LEVEL);
        RenderingServer.GlobalShaderParameterSet("fire_position", new Vector3(TerrainField.FIRE.X, 0.55f, TerrainField.FIRE.Y));
    }

    public override void _ExitTree()
    {
        if (Game.Instance != null && ReferenceEquals(Game.Instance.camp, this)) Game.Instance.camp = null;
        /// Worker threads must be joined before the engine tears the scene down, even
        /// when the window is closed halfway through loading.
        foreach (Variant thread in new Godot.Collections.Array { _terrain_thread, _grass_thread })
        {
            if (thread.VariantType != Variant.Type.Nil && G.truthy(G.Call(thread, "is_started")))
            {
                G.Call(thread, "wait_to_finish");
            }
        }
        _terrain_thread = null;
        _grass_thread = null;
    }

    public double height_at(double x, double z)
    {
        return field.height(x, z);
    }

    public async Task build()
    {
        /// Runs the whole build. Awaits a frame between stages for the loading screen.
        await _stage("Surveying the clearing");
        if (!IsInsideTree())
        {
            return;
        }
        plan = new ScenePlan(field);
        plan.build();
        // One height bake feeds the mesh, the collision and every scatterer.
        field.bake_height_grid(TerrainBuilder.COLLISION_HALF, TerrainBuilder.INNER_SPACING);
        // Background work that depends only on the immutable field and plan.

        terrain_material = _make_terrain_material();
        TerrainBuilder terrain_builder = new TerrainBuilder(field);
        _terrain_thread = new GodotThread();
        _terrain_thread.Start(Callable.From(() => new Godot.Collections.Dictionary { { (StringName)"mesh", terrain_builder.build_mesh(terrain_material) }, { (StringName)"collision", terrain_builder.build_collision() }, { (StringName)"outer_collision", terrain_builder.build_outer_collision() } }));
        GrassPlanter planter = new GrassPlanter(field, Quality.Instance.current.grass_distance + GrassPlanter.CHUNK_SIZE, Quality.Instance.current.grass_density);
        _grass_thread = new GodotThread();
        _grass_thread.Start(Callable.From(() => planter.plan()));

        await _stage("Growing the forest");
        if (!IsInsideTree())
        {
            return;
        }
        forest = new Forest(field, plan);
        AddChild(forest);
        forest.build();
        ridge_forest = new RidgeForest(field, forest);
        AddChild(ridge_forest);
        ridge_forest.build();

        await _stage("Shaping the land");
        Variant terrain_data = await _join(_terrain_thread);
        _terrain_thread = null;
        if (!IsInsideTree() || terrain_data.VariantType == Variant.Type.Nil)
        {
            return;
        }
        _place_terrain(G.Index(terrain_data, "mesh").As<ArrayMesh>(), G.Index(terrain_data, "collision").As<CollisionShape3D>(), G.Index(terrain_data, "outer_collision").As<CollisionShape3D>());

        await _stage("Planting the understory");
        if (!IsInsideTree())
        {
            return;
        }
        understory = new Understory(field, plan);
        AddChild(understory);
        understory.build_plants();
        await _join(_grass_thread);
        _grass_thread = null;
        if (!IsInsideTree())
        {
            return;
        }
        understory.add_grass(planter);

        await _stage("Filling the pond");
        if (!IsInsideTree())
        {
            return;
        }
        pond = new Pond(field);
        AddChild(pond);
        pond.build();
        underwater_fx = new UnderwaterFX(field);
        AddChild(underwater_fx);
        shore_life = new ShoreLife(field);
        AddChild(shore_life);
        shore_life.build();

        await _stage("Setting up camp");
        if (!IsInsideTree())
        {
            return;
        }
        rocks = new CampRocks(field, plan);
        AddChild(rocks);
        rocks.build();
        campsite = new Campsite(field);
        AddChild(campsite);
        campsite.build();

        await _stage("Waking the wildlife");
        if (!IsInsideTree())
        {
            return;
        }
        wildlife = new Wildlife(field);
        AddChild(wildlife);
        wildlife.build();
        _setup_audio();
        await _stage("");
    }

    public void activate_reflections()
    {
        /// Planar reflections are switched on last, once the renderer is warm.
        if (pond != null)
        {
            pond.activate();
        }
    }

    public async Task _stage(string stage_name)
    {
        /// Announces a stage and logs how long the previous one took: its CPU work and
        /// the first frame rendered after it (shader compilation, uploads).
        long now = (long)Time.GetTicksMsec();
        string previous = _stage_name;
        long work_ms = now - _stage_started_ms;
        if (!(stage_name.Length == 0))
        {
            EmitSignal(SignalName.stage_started, stage_name, _stage_index, STAGE_COUNT);
        }
        _stage_index += 1;
        _stage_name = stage_name;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _stage_started_ms = (long)Time.GetTicksMsec();
        if (!(previous.Length == 0))
        {
            G.print(G.format("  %-26s %5d ms work  %5d ms first frame", new Godot.Collections.Array { previous, work_ms, _stage_started_ms - now }));
        }
    }

    public async Task<Variant> _join(GodotThread thread)
    {
        /// Waits for a worker thread without blocking the render loop. Returns null if
        /// the scene went away in the meantime (the thread is joined by _exit_tree).
        while (thread.IsAlive())
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!IsInsideTree())
            {
                return default(Variant);
            }
        }
        if (!thread.IsStarted())
        {
            return default(Variant);
        }
        return thread.WaitToFinish();
    }

    public ShaderMaterial _make_terrain_material()
    {
        // ------------------------------------------------------------------- terrain
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>("res://shaders/terrain.gdshader");
        foreach (Variant set_name in new Godot.Collections.Array { "grass", "litter", "mud", "path" })
        {
            bind_pbr_set(mat, set_name.AsString(), set_name.AsString());
        }
        bind_texture(mat, "noise_tex", "res://textures/noise_rgba.png");
        bind_texture(mat, "detail_normal", "res://textures/detail_normal.png");
        bind_texture(mat, "caustics_tex", "res://textures/caustics.png");
        return mat;
    }

    public void _place_terrain(ArrayMesh mesh, CollisionShape3D collision, CollisionShape3D outer_collision)
    {
        terrain = new MeshInstance3D();
        terrain.Name = "Terrain";
        terrain.Mesh = mesh;
        terrain.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        terrain.GIMode = GeometryInstance3D.GIModeEnum.Static;
        AddChild(terrain);

        terrain_body = new StaticBody3D();
        terrain_body.Name = "TerrainBody";
        terrain_body.CollisionLayer = unchecked((uint)(1));
        terrain_body.AddChild(collision);
        terrain_body.AddChild(outer_collision);
        AddChild(terrain_body);
        Quality.Instance.Connect(Quality.SignalName.preset_changed, new Callable(this, Camp.MethodName._on_quality));
        _on_quality(Quality.Instance.current);
    }

    public void _on_quality(QualityPreset p)
    {
        if (terrain_material != null)
        {
            terrain_material.SetShaderParameter("parallax_steps", p.parallax_steps);
        }
    }

    public override void _Process(double _delta)
    {
        // ------------------------------------------------------------------- helpers
        if (audio == null || !audio.is_ready())
        {
            return;
        }
        WorldController world = Game.Instance.world;
        if (world != null)
        {
            audio.set_daylight(world.daylight());
            audio.set_wind(clampf((world.wind_strength() - 0.4) / 0.6, 0.0, 1.0));
        }
        if (campsite != null && campsite.firepit != null)
        {
            audio.set_fire_intensity(campsite.firepit.intensity);
        }
    }

    public void _setup_audio()
    {
        audio = new AudioDirector();
        audio.Name = "Audio";
        AddChild(audio);
        Game.Instance.audio = audio;
        Godot.Collections.Array<Vector3> shore = new Godot.Collections.Array<Vector3>();
        for (long i = 0; i < 8; i++)
        {
            double a = (double)i / 8.0 * TAU;
            Vector2 p = TerrainField.shore_point(a);
            shore.Add(new Vector3(p.X, (float)(TerrainField.WATER_LEVEL + 0.05), p.Y));
        }
        Godot.Collections.Array<Vector3> canopy = new Godot.Collections.Array<Vector3>();
        if (plan != null)
        {
            foreach (ScenePlan.TreeEntry entry in plan.near_trees())
            {
                if ((long)canopy.Count >= 8)
                {
                    break;
                }
                if (entry.position.Length() < 18.0 || entry.position.Length() > 55.0)
                {
                    continue;
                }
                double y = field.height(entry.position.X, entry.position.Y) + 6.0;
                canopy.Add(new Vector3(entry.position.X, (float)y, entry.position.Y));
            }
        }
        if ((canopy.Count == 0))
        {
            canopy.Add(new Vector3(20.0f, 8.0f, -18.0f));
            canopy.Add(new Vector3(-16.0f, 8.0f, -22.0f));
            canopy.Add(new Vector3(18.0f, 7.0f, 20.0f));
        }
        Vector3 fire_pos = new Vector3(TerrainField.FIRE.X, 0.55f, TerrainField.FIRE.Y);
        if (campsite != null)
        {
            fire_pos = campsite.firepit.GlobalPosition + new Vector3(0.0f, 0.45f, 0.0f);
        }
        audio.setup(fire_pos, shore, canopy);
        Player player = Game.Instance.player;
        if (player != null)
        {
            if (!player.IsConnected(Player.SignalName.footstep, new Callable(this, Camp.MethodName._on_footstep)))
            {
                player.Connect(Player.SignalName.footstep, new Callable(this, Camp.MethodName._on_footstep));
            }
            if (!player.IsConnected(Player.SignalName.wading_changed, new Callable(this, Camp.MethodName._on_wading)))
            {
                player.Connect(Player.SignalName.wading_changed, new Callable(this, Camp.MethodName._on_wading));
            }
        }
    }

    public void _on_footstep(StringName surface, bool running)
    {
        if (audio != null)
        {
            audio.footstep(surface, running);
        }
    }

    public void _on_wading(bool active, double speed)
    {
        if (audio != null)
        {
            audio.set_wading(active, clampf(speed / 4.0, 0.0, 1.0));
        }
        if (pond != null)
        {
            pond.set_wading(active ? 0.7 : 0.0);
        }
    }

    public static void bind_pbr_set(ShaderMaterial material, string prefix, string set_name)
    {
        bind_texture(material, prefix + "_albedo", G.format("res://textures/%s_albedo.png", set_name));
        bind_texture(material, prefix + "_normal", G.format("res://textures/%s_normal.png", set_name));
        bind_texture(material, prefix + "_orm", G.format("res://textures/%s_orm.png", set_name));
    }

    public static void bind_prop_pbr(ShaderMaterial material, string set_name)
    {
        bind_texture(material, "albedo_tex", G.format("res://textures/%s_albedo.png", set_name));
        bind_texture(material, "normal_tex", G.format("res://textures/%s_normal.png", set_name));
        bind_texture(material, "orm_tex", G.format("res://textures/%s_orm.png", set_name));
    }

    public static void bind_texture(ShaderMaterial material, string uniform, string path)
    {
        if (!Content.Exists(path))
        {
            G.push_warning(G.format("Missing texture %s for %s", new Godot.Collections.Array { path, uniform }));
            return;
        }
        material.SetShaderParameter(uniform, Content.Load(path));
    }
}
