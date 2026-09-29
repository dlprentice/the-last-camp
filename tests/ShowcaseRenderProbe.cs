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
using LastCamp.Tests;
using LastCamp.Construction;

namespace LastCamp.Tests;

/// Actual Forward+ material/geometry probe. Diagnostic studio, not a game view.
public partial class ShowcaseRenderProbe : SceneTree
{
    public SubViewport view;
    public Camera3D camera;
    public Node3D stage;
    public bool motion = false;
    public Label motion_caption;
    public bool motion_started = false;
    public string output = "res://local-data/render-work/integration/probes";

    public override void _Initialize()
    {
        CallDeferred("_run");
    }

    public async void _run()
    {
        await ToSignal(this, SignalName.ProcessFrame);
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument == "--probe-motion")
            {
                motion = true;
            }
            if (argument.StartsWith("--probe-out=", StringComparison.Ordinal))
            {
                output = argument.TrimPrefix("--probe-out=");
            }
        }
        if ((output.Length == 0))
        {
            G.push_error("Probe output directory is empty");
            Quit(1);
            return;
        }
        // --script entry points compile before autoload singletons are registered.
        // Resolve dependent scripts after that first frame, like the test runner.
        if (RenderingServer.GetCurrentRenderingMethod() != "forward_plus")
        {
            G.push_error("Probe requires the actual Forward+ renderer");
            Quit(1);
            return;
        }
        Error directory_error = DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(output));
        if (directory_error != Error.Ok)
        {
            G.push_error("Could not create probe directory: " + error_string((long)directory_error));
            Quit(1);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--probe-parallax"))
        {
            await CheckParallax();
            return;
        }
        view = new SubViewport();
        view.Size = new Vector2I(768, 512);
        view.OwnWorld3D = true;
        view.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        view.Msaa3D = Viewport.Msaa.Msaa4X;
        view.MeshLodThreshold = 0.05f;
        Root.AddChild(view);
        if (motion)
        {
            // Display the actual SubViewport in Movie Maker's root viewport. This is
            // an explicitly labelled asset diagnostic, never the complete game scene.
            TextureRect display = new TextureRect();
            display.Texture = view.GetTexture();
            display.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            display.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            Root.AddChild(display);
            display.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            Label heading = new Label();
            heading.Text = "THE LAST CAMP  |  ASSET / MATERIAL PROBE";
            heading.Position = new Vector2(24, 20);
            heading.AddThemeFontSizeOverride("font_size", 22);
            Root.AddChild(heading);
            motion_caption = new Label();
            motion_caption.Position = new Vector2(24, 52);
            motion_caption.AddThemeFontSizeOverride("font_size", 17);
            Root.AddChild(motion_caption);
        }
        WorldEnvironment world = new WorldEnvironment();
        world.Environment = new Environment();
        world.Environment.BackgroundMode = Environment.BGMode.Color;
        world.Environment.BackgroundColor = new Color(0.12f, 0.15f, 0.18f);
        world.Environment.AmbientLightSource = Environment.AmbientSource.Color;
        world.Environment.AmbientLightColor = new Color(0.64f, 0.73f, 0.90f);
        world.Environment.AmbientLightEnergy = 0.65f;
        world.Environment.TonemapMode = Environment.ToneMapper.Agx;
        view.AddChild(world);
        DirectionalLight3D sun = new DirectionalLight3D();
        sun.RotationDegrees = new Vector3(-42, -35, 0);
        sun.LightColor = new Color(1.0f, 0.88f, 0.70f);
        sun.LightEnergy = 1.8f;
        sun.ShadowEnabled = true;
        view.AddChild(sun);
        camera = new Camera3D();
        camera.CullMask = unchecked((uint)(1));
        camera.Fov = 42;
        view.AddChild(camera);
        camera.MakeCurrent();
        if (OS.GetCmdlineUserArgs().Contains("--probe-canopy-batches"))
        {
            await CheckCanopyBatches();
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--probe-grass-density"))
        {
            await CheckGrassDensity();
            return;
        }
        for (long form = 0; form < 4; form++)
        {
            stage = new Node3D();
            view.AddChild(stage);
            MultiMeshInstance3D plant = new MultiMeshInstance3D();
            MultiMesh mm = new MultiMesh();
            mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
            mm.UseCustomData = true;
            mm.Mesh = HabitatDiversity.plant_mesh(form, 60011 + form * 113);
            mm.InstanceCount = 1;
            mm.SetInstanceTransform(0, Transform3D.Identity);
            mm.SetInstanceCustomData(0, new Color(0.3f, 1, 1, 1));
            plant.Multimesh = mm;
            ShaderMaterial mat = new ShaderMaterial();
            mat.Shader = Content.Load<Shader>("res://shaders/habitat.gdshader");
            plant.MaterialOverride = mat;
            stage.AddChild(plant);
            camera.Position = new Vector3(0.9f, 0.78f, 1.3f);
            camera.LookAt(new Vector3(0, 0.23f, 0));
            await capture(G.format("plant_%d", form));
            stage.Free();
        }
        stage = new Node3D();
        view.AddChild(stage);
        FieldKit.kettle(stage, new Vector3(-0.22f, 0, 0));
        FieldKit.mug(stage, new Vector3(0.18f, 0, 0), new Color(0.63f, 0.64f, 0.48f));
        FieldKit.mug(stage, new Vector3(0.36f, 0, 0.12f), new Color(0.18f, 0.32f, 0.36f));
        camera.Position = new Vector3(0.65f, 0.50f, 1.15f);
        camera.LookAt(new Vector3(0, 0.14f, 0));
        await capture("enamel_dry");
        RenderingServer.GlobalShaderParameterSet("scene_wetness", 1.0);
        await capture("enamel_wet");
        stage.Free();
        stage = new Node3D();
        view.AddChild(stage);
        MeshInstance3D hardware = new MeshInstance3D();
        hardware.Mesh = FieldKit.rounded_box(new Vector3(0.38f, 0.08f, 0.12f), 0.018);
        hardware.MaterialOverride = PropMaterials.iron();
        stage.AddChild(hardware);
        MeshBuilder rope = new MeshBuilder();
        PropMeshes.add_rope(rope, new Vector3(-0.35f, 0.15f, 0.1f), new Vector3(0.35f, 0.15f, 0.1f), 0.08, 0.016, 12);
        MeshInstance3D rope_node = new MeshInstance3D();
        rope_node.Mesh = rope.commit();
        rope_node.MaterialOverride = PropMaterials.rope();
        stage.AddChild(rope_node);
        camera.Position = new Vector3(0.45f, 0.48f, 1.05f);
        camera.LookAt(new Vector3(0, 0.06f, 0));
        await capture("hardware_wet");
        G.print("SHOWCASE_RENDER_PROBE_DONE output=", output, " frames_drawn=", Engine.GetFramesDrawn());
        Quit(0);
    }

    private async Task CheckCanopyBatches()
    {
        bool passed = true;
        var rows = new Godot.Collections.Array();
        using (var partition = new TestCanopyBatching())
        using (var result = partition.run())
        {
            rows.Add(result);
            passed &= result["failed"].AsInt32() == 0;
        }
        foreach (string name in new[] { "bark", "foliage", "tree_impostor" })
        {
            int[] counts = new int[2];
            for (int variant = 0; variant < 2; variant++)
            {
                string path = $"res://shaders/{name}{(variant == 1 ? "_multimesh" : "")}.gdshader";
                var shader = Content.Load<Shader>(path);
                passed &= shader.GetShaderUniformList().Count > 0;
                var node = new MeshInstance3D { Mesh = new QuadMesh(), Visible = false,
                    MaterialOverride = new ShaderMaterial { Shader = shader } };
                view.AddChild(node);
                await ToSignal(this, SignalName.ProcessFrame);
                counts[variant] = RenderingServer.InstanceGeometryGetShaderParameterList(node.GetInstance()).Count;
                node.Free();
            }
            bool ok = counts[0] > 0 && counts[1] == 0;
            passed &= ok;
            rows.Add(new Godot.Collections.Dictionary { { "shader", name }, { "individual_parameters", counts[0] },
                { "multimesh_parameters", counts[1] }, { "passed", ok } });
            GD.Print($"CANOPY_PARAMETER_PROBE {name}: {counts[0]}->{counts[1]}, {(ok ? "PASS" : "FAIL")}");
        }
        var maker = new TreeImpostors();
        var individual = new Node3D();
        view.AddChild(individual);
        var transforms = new Transform3D[6];
        var keys = Enumerable.Range(0, 6).Select(i => i.ToString()).ToArray();
        for (int i = 0; i < 6; i++)
        {
            using Image color = Image.CreateEmpty(96 + i * 12, 16 + i * 2, false, Image.Format.Rgba8);
            color.Fill(Color.FromHsv(i / 6.0f, 0.65f, 0.85f));
            color.GenerateMipmaps();
            using Image normal = Image.CreateEmpty(color.GetWidth(), color.GetHeight(), false, Image.Format.Rgba8);
            normal.Fill(new Color(0.5f, 0.5f, 1, 1));
            normal.GenerateMipmaps();
            var baked = new TreeImpostors.Baked { albedo = ImageTexture.CreateFromImage(color), normal = ImageTexture.CreateFromImage(normal),
                extent = new Vector2(0.8f + i * 0.13f, 2 + i * 0.21f), base_y = i * 0.08 };
            baked.quad = TreeImpostors._quad(baked.extent.X, baked.extent.Y, baked.base_y);
            baked.material = new ShaderMaterial { Shader = Content.Load<Shader>("res://shaders/tree_impostor.gdshader") };
            baked.material.SetShaderParameter("albedo_atlas", baked.albedo);
            baked.material.SetShaderParameter("normal_atlas", baked.normal);
            baked.material.SetShaderParameter("use_instance_custom", true);
            baked.material.SetShaderParameter("roughness", 0.4f + i * 0.05f);
            baked.material.SetShaderParameter("translucency", 0.1f + i * 0.04f);
            baked.shadow_material = (ShaderMaterial)baked.material.Duplicate();
            maker.baked.Add(keys[i], baked);
            transforms[i] = new Transform3D(new Basis(Vector3.Up, i * 0.23f).Scaled(Vector3.One * (0.9f + i * 0.04f)),
                new Vector3(-7.5f + i * 3, 0, 0));
            var one = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true, Mesh = baked.quad, InstanceCount = 1 };
            one.SetInstanceTransform(0, transforms[i]);
            one.SetInstanceCustomData(0, new Color(i * 0.23f, 0.4f, 0, 0));
            individual.AddChild(new MultiMeshInstance3D { Multimesh = one, MaterialOverride = baked.material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        TreeImpostors.Family family = maker.family(keys);
        var all = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true, Mesh = family.Quad, InstanceCount = 6,
            CustomAabb = new Aabb(new Vector3(-10, -1, -10), new Vector3(20, 8, 20)) };
        for (int i = 0; i < 6; i++)
        {
            all.SetInstanceTransform(i, transforms[i]);
            all.SetInstanceCustomData(i, new Color(i * 0.23f, 0.4f, i, 0));
        }
        var batched = new MultiMeshInstance3D { Multimesh = all, MaterialOverride = family.Color,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        view.AddChild(batched);
        foreach (int angle in new[] { 0, 1, 2 })
        {
            camera.Position = new Vector3((angle - 1) * 5, 4, 27);
            camera.LookAt(new Vector3(0, 1.5f, 0));
            individual.Visible = true; batched.Visible = false;
            for (int frame = 0; frame < 8; frame++) await ToSignal(this, SignalName.ProcessFrame);
            await new Signal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
            using Image reference = view.GetTexture().GetImage();
            reference.SavePng(output.PathJoin($"canopy_{angle}_individual.png"));
            individual.Visible = false; batched.Visible = true;
            for (int frame = 0; frame < 8; frame++) await ToSignal(this, SignalName.ProcessFrame);
            await new Signal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
            using Image actual = view.GetTexture().GetImage();
            actual.SavePng(output.PathJoin($"canopy_{angle}_batched.png"));
            byte[] expected = reference.GetData(), observed = actual.GetData();
            double error = expected.Zip(observed).Average(pair => Math.Abs((int)pair.First - pair.Second));
            int changed = expected.Zip(observed).Count(pair => Math.Abs((int)pair.First - pair.Second) > 2);
            var centers = new HashSet<Color>();
            Color background = reference.GetPixel(0, 0);
            bool visible = true;
            for (int i = 0; i < 6; i++)
            {
                var b = maker.baked[keys[i]];
                Vector3 center = transforms[i].Origin + Vector3.Up *
                    (float)((b.base_y + b.extent.Y * 0.5) * transforms[i].Basis.Y.Length());
                Vector2I pixel = (Vector2I)camera.UnprojectPosition(center);
                Color sample = reference.GetPixel(pixel.X, pixel.Y);
                centers.Add(sample);
                visible &= new Vector3(sample.R - background.R, sample.G - background.G, sample.B - background.B).Length() > 0.1f;
            }
            bool ok = error < 0.05 && changed < expected.Length * 0.003 && visible && centers.Count == 6;
            passed &= ok;
            rows.Add(new Godot.Collections.Dictionary { { "angle", angle }, { "mean_byte_error", error }, { "changed_bytes", changed }, { "passed", ok } });
            GD.Print($"CANOPY_BATCH_PROBE {angle}: error={error:F5}, changed={changed}, {(ok ? "PASS" : "FAIL")}");
        }
        using var file = Godot.FileAccess.Open(output.PathJoin("canopy-batches.json"), Godot.FileAccess.ModeFlags.Write);
        file.StoreString(Json.Stringify(rows, "  "));
        view.Free(); maker.Free();
        G.drain_finalizers();
        await ToSignal(CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        Quit(passed ? 0 : 1);
    }

    private async Task CheckGrassDensity()
    {
        RenderingServer.GlobalShaderParameterSet("wind_strength", 0.0f);
        RenderingServer.GlobalShaderParameterSet("player_position", new Vector3(10000, 0, 0));
        var material = new ShaderMaterial { Shader = Content.Load<Shader>("res://shaders/grass.gdshader") };
        material.SetShaderParameter("density_lod", new Vector4(Understory.GrassDensityStart,
            Understory.GrassDensityTransition, Understory.GrassDensityMinimum, Understory.GrassDensityBlend));
        var planter = new GrassPlanter(new TerrainField(), 32, 0.15);
        planter.plan();
        var mesh = GrassPlanter.clump_mesh();
        var batches = new List<(MultiMesh Mesh, Aabb Bounds)>();
        foreach (var chunk in planter.chunks)
        {
            var batch = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true, UseCustomData = true, Mesh = mesh, InstanceCount = (int)chunk.count };
            batch.Buffer = GrassPlanter.upload_buffer(chunk);
            batch.CustomAabb = chunk.aabb;
            var node = new MultiMeshInstance3D { Multimesh = batch, MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            node.SetInstanceShaderParameter("grass_population", (float)chunk.count);
            view.AddChild(node);
            batches.Add((batch, chunk.aabb));
        }
        bool passed = true;
        var report = new Godot.Collections.Dictionary();
        var positions = new[] { new Vector3(10, 2, 30), new Vector3(20, 2, 45), new Vector3(-38, 1.7f, 15) };
        for (int shot = 0; shot < positions.Length; shot++)
        {
            camera.Position = positions[shot];
            camera.LookAt(new Vector3(0, 0.4f, 0));
            long full = 0, reduced = 0;
            foreach (var batch in batches) { batch.Mesh.VisibleInstanceCount = -1; full += batch.Mesh.InstanceCount; }
            for (int frame = 0; frame < 8; frame++) await ToSignal(this, SignalName.ProcessFrame);
            await new Signal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
            using var reference = view.GetTexture().GetImage();
            reference.SavePng(output.PathJoin($"grass_{shot}_shader_only.png"));
            foreach (var batch in batches)
            {
                int count = Understory.grass_draw_count(batch.Bounds, camera.Position, batch.Mesh.InstanceCount);
                batch.Mesh.VisibleInstanceCount = count;
                reduced += count;
            }
            for (int frame = 0; frame < 8; frame++) await ToSignal(this, SignalName.ProcessFrame);
            await new Signal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
            using var actual = view.GetTexture().GetImage();
            actual.SavePng(output.PathJoin($"grass_{shot}_native_prefix.png"));
            byte[] expected = reference.GetData(), observed = actual.GetData();
            long differences = expected.Length == observed.Length ? expected.Zip(observed).Count(pair => pair.First != pair.Second) : expected.Length;
            int stride = expected.Length / (reference.GetWidth() * reference.GetHeight());
            var colors = new HashSet<(byte, byte, byte)>();
            for (int pixel = 0; pixel + 2 < expected.Length; pixel += stride * 7)
                colors.Add((expected[pixel], expected[pixel + 1], expected[pixel + 2]));
            bool ok = differences == 0 && reduced < full && colors.Count > 64;
            passed &= ok;
            report[shot] = new Godot.Collections.Dictionary { { "full_instances", full }, { "submitted_instances", reduced },
                { "different_bytes", differences }, { "sampled_colors", colors.Count }, { "passed", ok } };
            GD.Print($"GRASS_DENSITY_PROBE {shot}: {full}->{reduced}, changed bytes={differences}, {(ok ? "PASS" : "FAIL")}");
        }
        using var file = Godot.FileAccess.Open(output.PathJoin("grass-density.json"), Godot.FileAccess.ModeFlags.Write);
        file.StoreString(Json.Stringify(report, "  "));
        view.Free();
        G.drain_finalizers();
        await ToSignal(CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        Quit(passed ? 0 : 1);
    }

    private async Task CheckParallax()
    {
        // Exercise the production function, not a C# translation of its march.
        // A constant-height field has an analytic intersection, independent of
        // step count. Render the resulting UVs into a linear HDR target.
        foreach (string path in new[] { "res://shaders/terrain.gdshader", "res://shaders/foliage.gdshader", "res://shaders/grass.gdshader" })
        {
            Shader source = Content.Load<Shader>(path);
            if (source.GetShaderUniformList().Count == 0)
            {
                GD.PushError("Shader did not compile: " + path);
                Quit(1);
                return;
            }
        }
        string terrain = Godot.FileAccess.GetFileAsString("res://shaders/terrain.gdshader");
        int start = terrain.IndexOf("vec4 sample_heights(", StringComparison.Ordinal);
        int end = terrain.IndexOf("void fragment()", start, StringComparison.Ordinal);
        string functions = terrain[start..end];
        var shader = new Shader { Code = """
            shader_type canvas_item;
            render_mode unshaded;
            uniform sampler2D grass_orm;
            uniform sampler2D litter_orm;
            uniform sampler2D mud_orm;
            uniform sampler2D path_orm;
            uniform int parallax_steps = 4;
            uniform float tile_near = 0.5;
            """ + functions + """
            void fragment() {
                vec3 ray = normalize(vec3(1.5, 0.45, 1.0));
                vec2 hit = parallax(UV, ray, vec4(0.0, 0.0, 0.0, 1.0),
                    dFdx(UV), dFdy(UV), 0.2);
                COLOR = vec4(hit, 0.0, 1.0);
            }
            """ };
        using var height = Image.CreateEmpty(1, 1, false, Image.Format.Rgbaf);
        height.Fill(new Color(0, 0, 0.375f));
        var texture = ImageTexture.CreateFromImage(height);
        var material = new ShaderMaterial { Shader = shader };
        foreach (string parameter in new[] { "grass_orm", "litter_orm", "mud_orm", "path_orm" })
            material.SetShaderParameter(parameter, texture);
        view = new SubViewport { Size = new Vector2I(192, 32), Disable3D = true,
            UseHdr2D = true, UseDebanding = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        Root.AddChild(view);
        view.AddChild(new ColorRect { Size = view.Size, Material = material });
        var rows = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        bool passed = true;
        Vector3 ray = new Vector3(1.5f, 0.45f, 1).Normalized();
        Vector2 shift = new Vector2(ray.X, ray.Y) / Math.Max(ray.Z, 0.5f) * (0.2f * 0.5f * (1 - 0.375f));
        foreach (int steps in new[] { 4, 8, 16, 24, 32 })
        {
            material.SetShaderParameter("parallax_steps", steps);
            for (int frame = 0; frame < 12; frame++) await ToSignal(this, SignalName.ProcessFrame);
            await new Signal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
            using Image pixels = view.GetTexture().GetImage();
            float maxError = 0;
            foreach (int x in new[] { 32, 96, 160 })
            {
                const int y = 16;
                Vector2 expected = new Vector2((x + 0.5f) / view.Size.X, (y + 0.5f) / view.Size.Y) - shift;
                Color actual = pixels.GetPixel(x, y);
                maxError = Math.Max(maxError, new Vector2(actual.R, actual.G).DistanceTo(expected));
            }
            bool ok = maxError < 0.0008f; // Includes the target's float16 rounding.
            passed &= ok;
            rows.Add(new Godot.Collections.Dictionary { { "steps", steps }, { "max_uv_error", maxError }, { "passed", ok } });
            GD.Print($"PARALLAX_PROBE steps={steps} max_uv_error={maxError:F7} passed={ok}");
        }
        using var reportFile = Godot.FileAccess.Open(output.PathJoin("parallax.json"), Godot.FileAccess.ModeFlags.Write);
        reportFile.StoreString(Json.Stringify(new Godot.Collections.Dictionary { { "passed", passed }, { "samples", rows } }, "  "));
        GD.Print($"PARALLAX_PROBE_DONE passed={passed}");
        if (passed && OS.GetCmdlineUserArgs().Contains("--probe-termination"))
        {
            // The caller sends SIGTERM only after this marker. Keep drawing
            // until the production Game handler takes the normal quit path.
            GD.Print($"TERMINATION_PROBE_READY pid={OS.GetProcessId()}");
            return;
        }
        Quit(passed ? 0 : 1);
    }

    public async Task capture(string label)
    {
        for (long frame = 0; frame < 8; frame++)
        {
            await ToSignal(this, SignalName.ProcessFrame);
        }
        await new Signal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
        Image image = view.GetTexture().GetImage();
        if (image == null || image.IsEmpty())
        {
            G.push_error("Probe image is empty: " + label);
            Quit(1);
            return;
        }
        double low = 1.0;
        double high = 0.0;
        for (long y = 0, y_end = image.GetHeight(); y < y_end; y += 4)
        {
            for (long x = 0, x_end = image.GetWidth(); x < x_end; x += 4)
            {
                Color pixel = image.GetPixel((int)x, (int)y);
                double value = ((double)pixel.R + pixel.G + pixel.B) / 3.0;
                low = minf(low, value);
                high = maxf(high, value);
            }
        }
        if (high - low < 0.025)
        {
            G.push_error("Probe produced no visible geometry: " + label);
            Quit(1);
            return;
        }
        Error error = image.SavePng(output.PathJoin(label + ".png"));
        if (error != Error.Ok)
        {
            G.push_error("Probe save failed: " + label);
            Quit(1);
            return;
        }
        G.print("PROBE_CAPTURE ", label);
        if (motion)
        {
            if (!motion_started)
            {
                motion_started = true;
                G.print("PROBE_MOTION_START frames_drawn=", Engine.GetFramesDrawn());
            }
            Godot.Collections.Dictionary captions = new Godot.Collections.Dictionary { { "plant_0", "Broadleaf rosette" }, { "plant_1", "Compound fernlet" }, { "plant_2", "Wet-margin rush" }, { "plant_3", "Curled forest litter" }, { "enamel_dry", "Enamelware / dry" }, { "enamel_wet", "Enamelware / wet" }, { "hardware_wet", "Iron and twisted rope / wet" } };
            motion_caption.Text = G.str(G.get(captions, label, label)) + "  |  Godot Forward+  |  Not a gameplay capture";
            double original_rotation = stage.Rotation.Y;
            for (long frame2 = 0; frame2 < 36; frame2++)
            {
                double progress = (double)frame2 / 35.0;
                Vector3 _t1 = stage.Rotation;
                _t1.Y = (float)(original_rotation + sin(progress * TAU) * 0.32);
                stage.Rotation = _t1;
                await ToSignal(this, SignalName.ProcessFrame);
                await new Signal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
            }
            Vector3 _t2 = stage.Rotation;
            _t2.Y = (float)original_rotation;
            stage.Rotation = _t2;
        }
    }

    public override void _Finalize()
    {
        G.drain_finalizers();
    }
}
