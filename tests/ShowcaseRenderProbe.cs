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
