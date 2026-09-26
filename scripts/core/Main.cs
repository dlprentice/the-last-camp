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

/// Scene entry point: runs the staged world build behind the loading screen,
/// warms the renderer up, then hands over to the intro dolly, the player, or
/// the capture/benchmark tool depending on the command line.
public partial class Main : Node3D
{
    public Main()
    {
        // Metadata-only package checks must never construct or enter the world.
        if (!OS.GetCmdlineUserArgs().Contains("--package-check"))
            MainScene.BuildInto(this);
    }

    public override void _ExitTree() => G.drain_finalizers();

    public Camp camp;
    public WorldController world;
    public Player player;
    public IntroDolly intro;
    public LoadingScreen loading;
    public Hud hud;

    public const double TOOL_LOADING_LIMIT_SECONDS = 300.0;

    public override async void _Ready()
    {
        if (Game.Instance.has_flag("package-check"))
        {
            GD.Print($"PACKAGE_MODE world_children={GetChildCount()}");
            long result = PackageCheck.run(Game.Instance.arg_value("notices", ""));
            Game.Instance.quit_cleanly(result);
            return;
        }
        camp = GetNode<Camp>("Camp");
        world = GetNode<WorldController>("World");
        player = GetNode<Player>("Player");
        intro = GetNode<IntroDolly>("Intro");
        loading = GetNode<LoadingScreen>("Loading");
        hud = GetNode<Hud>("HUD");
        Game.Instance.mode = Game.Mode.LOADING;
        camp.Connect(Camp.SignalName.stage_started, new Callable(this, Main.MethodName._on_stage_started));
        if (Game.Instance.is_tool_run())
        {
            _watch_loading();
        }
        long t0 = (long)Time.GetTicksMsec();
        // The opaque loading UI needs only 2D. Avoid rendering half-built forest
        // stages while their CPU-side upload buffers are still resident.
        bool restore_3d = GetViewport().Disable3D;
        GetViewport().Disable3D = true;
        await camp.build();
        if (!IsInsideTree())
        {
            return;
        }
        // Opt-in performance experiment: hero trees beyond 45 m become baked
        // billboards. Off by default because the demo draws the whole scene at
        // full distance; enable with -- --impostors to measure.
        if (Game.Instance.has_flag("impostors"))
        {
            TreeImpostors impostors = new TreeImpostors();
            camp.AddChild(impostors);
            loading.set_progress("Baking tree impostors", 0.90);
            await impostors.bake(camp.forest);
            if (!IsInsideTree())
            {
                return;
            }
            camp.forest.attach_impostors(impostors);
        }
        GetNode<BiomeDressing>("BiomeDressing").setup(camp);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetNode<ForestFloorDressing>("ForestFloorDressing").setup(camp);
        ScannedDressing scanned = new ScannedDressing();
        camp.AddChild(scanned);
        scanned.setup(camp);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        HabitatDiversity habitat = new HabitatDiversity();
        camp.AddChild(habitat);
        habitat.setup(camp);
        CanopyDrips drips = new CanopyDrips();
        camp.AddChild(drips);
        drips.setup(camp, world);
        TerrainFieldDirector director = GetNode<TerrainFieldDirector>("TerrainFieldDirector");
        while (director._rain_thread != null)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!IsInsideTree())
            {
                return;
            }
        }
        long t_built = (long)Time.GetTicksMsec();
        loading.set_progress("Warming up the renderer", 0.96);
        GetViewport().Disable3D = restore_3d;
        await world.warm_up();
        if (!IsInsideTree())
        {
            return;
        }
        camp.activate_reflections();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        long t_ready = (long)Time.GetTicksMsec();
        G.print(G.format("World ready in %d ms (%d build + %d warm-up)", new Godot.Collections.Array { t_ready - t0, t_built - t0, t_ready - t_built }));
        if (Game.Instance.has_flag("scene-smoke"))
        {
            bool ok = (long)camp.plan.trees.Count >= 1200 && (long)habitat.batches.Count > 0 && (long)camp.wildlife.fish_routes.Count >= 12;
            G.print(G.format("SHOWCASE_SCENE_SMOKE trees=%d habitat_batches=%d fish=%d result=%s", new Godot.Collections.Array { (long)camp.plan.trees.Count, (long)habitat.batches.Count, (long)camp.wildlife.fish_routes.Count, ok ? "PASS" : "FAIL" }));
            Game.Instance.quit_cleanly(ok ? 0 : 1);
            return;
        }
        if (Game.Instance.has_flag("check-pond"))
        {
            loading.finish();
            CaptureTool capture = new CaptureTool();
            AddChild(capture);
            capture.run_pond_check(Game.Instance.arg_value("out", "res://local-data/pond-check"));
            return;
        }
        if (Game.Instance.has_flag("review-assets"))
        {
            loading.finish();
            AssetReview review = new AssetReview();
            AddChild(review);
            review.run(Game.Instance.arg_value("review-assets", "res://local-data/asset-review"));
            return;
        }
        if (Game.Instance.has_flag("benchmark"))
        {
            loading.finish();
            CaptureTool capture2 = new CaptureTool();
            AddChild(capture2);
            capture2.run_benchmark(Game.Instance.arg_value("benchmark", ""), Game.Instance.arg_value("out", "user://benchmark.json"));
            return;
        }
        if (Game.Instance.has_flag("cinematic"))
        {
            loading.finish();
            Cinematic cinematic = new Cinematic();
            AddChild(cinematic);
            cinematic.play(Game.Instance.arg_value("cinematic", "showreel"));
            return;
        }
        if (Game.Instance.has_flag("profile"))
        {
            loading.finish();
            CaptureTool capture3 = new CaptureTool();
            AddChild(capture3);
            capture3.run_profile(Game.Instance.arg_value("out", "user://profile.json"));
            return;
        }
        if (Game.Instance.has_flag("capture"))
        {
            loading.finish();
            CaptureTool capture4 = new CaptureTool();
            AddChild(capture4);
            capture4.run_capture(Game.Instance.arg_value("capture", "res://local-data/captures"), Game.Instance.arg_value("shots", ""));
            return;
        }
        loading.finish();
        if (Game.Instance.has_flag("skip-intro"))
        {
            player.begin();
            if (Game.Instance.has_flag("traverse"))
            {
                TraversalCheck traversal = new TraversalCheck();
                AddChild(traversal);
                traversal.run(player, Game.Instance.arg_value("traverse", "res://local-data/traversal"));
            }
        }
        else
        {
            intro.play(player);
        }
        Quality.Instance.auto_tune();
    }

    public void _on_stage_started(string stage_name, long index, long total)
    {
        loading.set_progress(stage_name, (double)index / (double)(total + 1));
    }

    public async void _watch_loading()
    {
        /// Automated runs (captures, benchmarks, profiles, traversals, cinematics)
        /// must never sit on the loading screen forever: a script error during the
        /// build used to leave a fullscreen window up until someone killed it.
        await ToSignal(GetTree().CreateTimer(TOOL_LOADING_LIMIT_SECONDS), SceneTreeTimer.SignalName.Timeout);
        if (IsInsideTree() && Game.Instance.mode == Game.Mode.LOADING)
        {
            G.push_error(G.format("World build did not finish within %d s; quitting the tool run", (long)TOOL_LOADING_LIMIT_SECONDS));
            G.print(G.format("LOADING_TIMEOUT seconds=%d", (long)TOOL_LOADING_LIMIT_SECONDS));
            GetTree().Quit(1);
        }
    }
}
