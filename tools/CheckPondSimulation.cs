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

namespace LastCamp.Tools;

/// A small real-GPU check, independent of the camp scene and its rendering cost.
/// godot --path . --fullscreen --script res://tools/CheckPondSimulation.cs
/// State readbacks occur only at checkpoints, never in the runtime simulation.
public partial class CheckPondSimulation : SceneTree
{
    public const long SIZE = 64;
    public static readonly Rect2 BOUNDS = new Rect2(0, 0, 8, 8);

    public long _failures = 0;
    public Godot.Collections.Dictionary _report = new Godot.Collections.Dictionary();
    public string _out = "";

    public override void _Initialize()
    {
        CallDeferred("_run");
    }

    public async void _run()
    {
        _out = ProjectSettings.GlobalizePath(G.format("res://local-data/water-interaction-20260907/solver-%d-%d", new Godot.Collections.Array { Time.GetUnixTimeFromSystem(), (long)Time.GetTicksMsec() }));
        DirAccess.MakeDirRecursiveAbsolute(_out);
        if (DisplayServer.GetName() == "headless")
        {
            G.push_error("This focused check requires the real main RenderingDevice.");
            Quit(2);
            return;
        }
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        Image mask = Image.CreateEmpty((int)SIZE, (int)SIZE, false, Image.Format.Rf);
        mask.Fill(new Color(0, 0, 0, 0));
        for (long z = 0; z < SIZE; z++)
        {
            mask.SetPixel(32, (int)z, new Color(1, 0, 0));
        }
        PondSimulation blocked = new PondSimulation();
        PondSimulation open_water = new PondSimulation();
        _check(blocked.setup(BOUNDS, mask, SIZE), "main GPU setup queued");
        _check(open_water.setup(BOUNDS, null, SIZE), "open reference setup queued");
        for (long frame = 0; frame < 180; frame++)
        {
            if (blocked.is_ready() && open_water.is_ready())
            {
                break;
            }
            await ToSignal(this, SignalName.ProcessFrame);
        }
        if (!blocked.is_ready() || !open_water.is_ready())
        {
            _check(false, "GPU setup completed");
            blocked.shutdown();
            open_water.shutdown();
            _finish();
            return;
        }
        _check(true, "GPU setup completed");
        Rid stable_rid = (blocked.get_texture() as Texture2Drd).TextureRdRid;
        Image initial = blocked.get_texture().GetImage();
        _check(G.op("<", _stats(initial)["height_max"], 0.0000001).AsBool(), "initial state is neutral");
        blocked.queue_impulse(new Vector2(2.5f, 4.0f), 0.24, 0.45);
        open_water.queue_impulse(new Vector2(2.5f, 4.0f), 0.24, 0.45);
        // A half-timestep call must retain the event; its second half dispatches it.
        blocked.step(PondSimulation.FIXED_STEP * 0.5);
        _check(blocked.get_simulation_time() == 0.0, "fractional step retained");
        blocked.step(PondSimulation.FIXED_STEP * 0.5);
        open_water.step(PondSimulation.FIXED_STEP);
        await ToSignal(this, SignalName.ProcessFrame);
        await new Signal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
        Image first = blocked.get_texture().GetImage();
        Godot.Collections.Dictionary first_stats = _stats(first);
        _report["first_impulse"] = first_stats;
        _check(G.op(">", first_stats["velocity_max"], 0.1).AsBool(), "queued impulse is not lost below the fixed timestep");
        await _advance(new Godot.Collections.Array { blocked, open_water }, 15);
        Image early = blocked.get_texture().GetImage();
        Godot.Collections.Dictionary early_stats = _stats(early);
        _report["early_1_second"] = early_stats;
        _check(G.op(">", early_stats["height_max"], 0.0001).AsBool(), "persistent nonzero displacement");
        _check(_region_max(early, new Rect2I(4, 24, 7, 16)) > 0.00001, "impulse propagates beyond its input footprint");
        _check(G.op(">", early_stats["foam_max"], 0.001).AsBool(), "contacts create persistent foam");
        _save_state(early, "early");
        await _advance(new Godot.Collections.Array { blocked, open_water }, 30);
        Image reflected = blocked.get_texture().GetImage();
        Image reference = open_water.get_texture().GetImage();
        _report["reflected_3_seconds"] = _stats(reflected);
        double blocked_right = _region_max(reflected, new Rect2I(33, 0, 31, (int)SIZE));
        double open_right = _region_max(reference, new Rect2I(33, 0, 31, (int)SIZE));
        double return_difference = _region_difference(reflected, reference, new Rect2I(10, 20, 14, 24));
        _report["boundary"] = new Godot.Collections.Dictionary { { "blocked_right_height", blocked_right }, { "open_right_height", open_right }, { "returned_difference", return_difference } };
        _check(blocked_right < 0.0000001, "solid strip prevents wave leakage");
        _check(open_right > 0.00001, "open reference wave crosses the same location");
        _check(return_difference > 0.00001, "solid boundary returns a wave into the first basin");
        _save_state(reflected, "reflected");
        // Freeze after one gather; its exact source field remains available while
        // the small asynchronous readback arrives over the normal frame queue.
        List<Vector2> points = new List<Vector2>(new List<Vector2> { new Vector2(2.231f, 3.877f), new Vector2(1.518f, 4.411f), new Vector2(6.17f, 2.7f), new Vector2(-1, 2) });
        blocked.set_probe_positions(points, 10);
        blocked.step(1.0 / 30.0);
        await ToSignal(this, SignalName.ProcessFrame);
        await new Signal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
        Image gathered_image = blocked.get_texture().GetImage();
        double age_at_dispatch = blocked.get_probe_sample_time();
        for (long frame2 = 0; frame2 < 30; frame2++)
        {
            if (blocked.get_probe_sample_time() >= 0.0)
            {
                break;
            }
            await ToSignal(this, SignalName.ProcessFrame);
        }
        List<float> sampled = blocked.get_probe_heights();
        double maximum_error = 0.0;
        for (long i = 0, i_end = (long)points.Count; i < i_end; i++)
        {
            maximum_error = maxf(maximum_error, absf(sampled[(int)i] - _bilinear_height(gathered_image, points[(int)i])));
        }
        _report["probes"] = new Godot.Collections.Dictionary { { "maximum_error", maximum_error }, { "sample_time", blocked.get_probe_sample_time() }, { "time_at_dispatch", age_at_dispatch } };
        _check(blocked.get_probe_sample_time() >= 0.0, "asynchronous probes complete");
        _check(G.ArrayEquals(blocked.get_sampled_probe_positions(), points), "probe positions are returned with their samples");
        _check(maximum_error < 0.000001, "GPU probes match exact bilinear surface samples");
        blocked.set_probe_positions(points, 11);
        blocked.step(1.0 / 30.0);
        blocked.set_probe_positions(new List<Vector2>(new List<Vector2> { new Vector2(6.17f, 2.7f) }), 12);
        for (long frame3 = 0; frame3 < 8; frame3++)
        {
            await ToSignal(this, SignalName.ProcessFrame);
        }
        _check(blocked.get_probe_sample_time() < 0.0 && (long)blocked.get_probe_heights().Count == 1 && blocked.get_probe_heights()[0] == 0.0, "old probe layout cannot populate new body slots");
        blocked.set_probe_positions(new List<Vector2>(), 13);
        await _advance(new Godot.Collections.Array { blocked, open_water }, 210);
        Image settled = blocked.get_texture().GetImage();
        Godot.Collections.Dictionary settled_stats = _stats(settled);
        _report["settled_17_seconds"] = settled_stats;
        _check(settled_stats["finite"].AsBool(), "all state channels remain finite");
        _check(G.op("<=", settled_stats["height_max"], PondSimulation.MAX_HEIGHT + 0.000001).AsBool() && G.op("<=", settled_stats["velocity_max"], PondSimulation.MAX_VELOCITY + 0.000001).AsBool(), "state respects safety limits");
        _check(G.op("<", settled_stats["energy"], G.op("*", early_stats["energy"], 0.05)).AsBool(), "wave energy decays without new input");
        _check(G.op("<", settled_stats["foam_sum"], G.op("*", early_stats["foam_sum"], 0.15)).AsBool(), "persistent foam dissipates after motion settles");
        _check((blocked.get_texture() as Texture2Drd).TextureRdRid == stable_rid, "published texture RID stays stable");
        _save_state(settled, "settled");
        blocked.shutdown();
        open_water.shutdown();
        for (long frame4 = 0; frame4 < 4; frame4++)
        {
            await ToSignal(this, SignalName.ProcessFrame);
        }
        _check(!blocked.is_ready() && blocked.get_texture().GetImage().GetPixel(0, 0).R == 0.0, "shutdown returns neutral fallback");
        _finish();
    }

    public async Task _advance(Godot.Collections.Array simulations, long frames)
    {
        for (long frame = 0; frame < frames; frame++)
        {
            foreach (Variant simulation in simulations)
            {
                G.Call(simulation, "step", 1.0 / 15.0, new Vector2(0.08f, 0.025f));
            }
            await ToSignal(this, SignalName.ProcessFrame);
        }
        await new Signal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
    }

    public Godot.Collections.Dictionary _stats(Image image)
    {
        bool finite = true;
        double height_max = 0.0;
        double velocity_max = 0.0;
        double foam_max = 0.0;
        double foam_sum = 0.0;
        double energy = 0.0;
        double spacing = BOUNDS.Size.X / (double)SIZE;
        for (long z = 0; z < SIZE; z++)
        {
            for (long x = 0; x < SIZE; x++)
            {
                Color value = image.GetPixel((int)x, (int)z);
                finite = finite && is_finite(value.R) && is_finite(value.G) && is_finite(value.B) && is_finite(value.A);
                height_max = maxf(height_max, absf(value.R));
                velocity_max = maxf(velocity_max, absf(value.G));
                foam_max = maxf(foam_max, value.B);
                foam_sum += value.B;
                if (value.A < 0.5)
                {
                    continue;
                }
                energy += (double)value.G * value.G;
                foreach (Variant offset in new Godot.Collections.Array { new Vector2I(1, 0), new Vector2I(0, 1) })
                {
                    Vector2I neighbour = G.op("+", new Vector2I((int)x, (int)z), offset).AsVector2I();
                    if (neighbour.X >= SIZE || neighbour.Y >= SIZE)
                    {
                        continue;
                    }
                    Color other = image.GetPixelv(neighbour);
                    if (other.A > 0.5)
                    {
                        energy += pow(PondSimulation.WAVE_SPEED * ((double)other.R - value.R) / spacing, 2);
                    }
                }
            }
        }
        return new Godot.Collections.Dictionary { { "finite", finite }, { "height_max", height_max }, { "velocity_max", velocity_max }, { "foam_max", foam_max }, { "foam_sum", foam_sum }, { "energy", energy * spacing * spacing } };
    }

    public double _region_max(Image image, Rect2I rect)
    {
        double result = 0.0;
        for (long z = rect.Position.Y, z_end = rect.End.Y; z < z_end; z++)
        {
            for (long x = rect.Position.X, x_end = rect.End.X; x < x_end; x++)
            {
                result = maxf(result, absf(image.GetPixel((int)x, (int)z).R));
            }
        }
        return result;
    }

    public double _region_difference(Image a, Image b, Rect2I rect)
    {
        double result = 0.0;
        for (long z = rect.Position.Y, z_end = rect.End.Y; z < z_end; z++)
        {
            for (long x = rect.Position.X, x_end = rect.End.X; x < x_end; x++)
            {
                result = maxf(result, absf((double)a.GetPixel((int)x, (int)z).R - b.GetPixel((int)x, (int)z).R));
            }
        }
        return result;
    }

    public double _bilinear_height(Image image, Vector2 point)
    {
        Vector2 uv = (point - BOUNDS.Position) / BOUNDS.Size;
        if (uv.X < 0.0 || uv.Y < 0.0 || uv.X >= 1.0 || uv.Y >= 1.0)
        {
            return 0.0;
        }
        Vector2 pixel = uv * (float)(double)SIZE - new Vector2(0.5f, 0.5f);
        Vector2I @base = new Vector2I((int)floori(pixel.X), (int)floori(pixel.Y));
        Vector2 f = pixel - (Vector2)@base;
        double result = 0.0;
        for (long z = 0; z < 2; z++)
        {
            for (long x = 0; x < 2; x++)
            {
                Vector2I sample_cell = (@base + new Vector2I((int)x, (int)z)).Clamp(Vector2I.Zero, new Vector2I((int)(SIZE - 1), (int)(SIZE - 1)));
                double weight = (x == 0 ? 1.0 - f.X : f.X) * (z == 0 ? 1.0 - f.Y : f.Y);
                result += image.GetPixelv(sample_cell).R * weight;
            }
        }
        return result;
    }

    public void _save_state(Image image, string label)
    {
        Image view = Image.CreateEmpty((int)SIZE, (int)SIZE, false, Image.Format.Rgb8);
        for (long z = 0; z < SIZE; z++)
        {
            for (long x = 0; x < SIZE; x++)
            {
                Color value = image.GetPixel((int)x, (int)z);
                Color colour = value.A < 0.5 ? new Color(0.06f, 0.06f, 0.06f) : new Color((float)(0.1 + maxf(value.R, 0.0) * 15.0), (float)(0.15 + value.B * 2.0), (float)(0.3 + maxf(-value.R, 0.0) * 15.0));
                view.SetPixel((int)x, (int)z, colour);
            }
        }
        view.Resize(512, 512, Image.Interpolation.Nearest);
        view.SavePng(_out.PathJoin(label + ".png"));
    }

    public void _check(bool condition, string label)
    {
        G.print(G.format("POND_CHECK %s %s", new Godot.Collections.Array { condition ? "PASS" : "FAIL", label }));
        if (!condition)
        {
            _failures += 1;
        }
    }

    public void _finish()
    {
        _report["failures"] = _failures;
        FileAccess file = FileAccess.Open(_out.PathJoin("report.json"), FileAccess.ModeFlags.Write);
        file.StoreString(Json.Stringify(_report, "\t"));
        G.print(G.format("POND_CHECK_DONE failures=%d output=%s", new Godot.Collections.Array { _failures, _out }));
        Quit((int)(_failures > 0 ? 1 : 0));
    }

    public override void _Finalize()
    {
        G.drain_finalizers();
    }
}
