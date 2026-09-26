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

public partial class Fingerprint
{
    public Godot.Collections.Dictionary result = new Godot.Collections.Dictionary();

    public void stamp(string label, Variant value)
    {
        List<byte> bytes = var_to_bytes(value);
        HashingContext hash = new HashingContext();
        hash.Start(HashingContext.HashType.Sha256);
        hash.Update(bytes.ToArray());
        result[label] = new Godot.Collections.Dictionary { { "bytes", (long)bytes.Count }, { "sha256", G.hex_encode(hash.Finish()) } };
    }

    public void mesh_stamp(string label, ArrayMesh mesh)
    {
        if (mesh == null)
        {
            stamp(label, default(Variant));
            return;
        }
        for (long surface = 0, surface_end = mesh.GetSurfaceCount(); surface < surface_end; surface++)
        {
            Godot.Collections.Array arrays = mesh.SurfaceGetArrays((int)surface);
            for (long channel = 0, channel_end = (long)arrays.Count; channel < channel_end; channel++)
            {
                stamp(G.format("%s/%d/%d", new Godot.Collections.Array { label, surface, channel }), arrays[(int)channel]);
            }
        }
    }

    public void run()
    {
        TerrainFieldEnhanced field = new TerrainFieldEnhanced();
        Godot.Collections.Array samples = new Godot.Collections.Array();
        for (long z = -300; z < 301; z += 13)
        {
            for (long x = -300; x < 301; x += 17)
            {
                samples.Add(field.height(x + 0.3125, z - 0.125));
            }
        }
        stamp("terrain", samples);
        ScenePlan plan = new ScenePlan(field);
        plan.build();
        Godot.Collections.Array entries = new Godot.Collections.Array();
        foreach (ScenePlan.TreeEntry t in plan.trees)
        {
            entries.Add(new Godot.Collections.Array { t.position, (long)t.kind, t.seed_value, t.scale, t.rotation, t.far });
        }
        stamp("tree_layout", entries);
        entries.Clear();
        foreach (ScenePlan.RockEntry r in plan.rocks)
        {
            entries.Add(new Godot.Collections.Array { r.position, r.scale, r.rotation, r.sink, r.variant });
        }
        stamp("rock_layout", entries);
        entries.Clear();
        foreach (ScenePlan.ShrubEntry s in plan.shrubs)
        {
            entries.Add(new Godot.Collections.Array { s.position, s.scale, s.rotation });
        }
        stamp("shrub_layout", entries);
        for (long kind = 0; kind < 6; kind++)
        {
            TreeGenerator generator = new TreeGenerator();
            TreeGenerator.Result tree = generator.generate(TreeSpecies.by_kind((TreeSpecies.Kind)kind), 21037 + kind * 113, 0.4);
            mesh_stamp(G.format("tree_%d/bark", kind), tree.bark);
            mesh_stamp(G.format("tree_%d/leaves", kind), tree.leaves);
            stamp(G.format("tree_%d/metadata", kind), new Godot.Collections.Array { tree.height, tree.trunk_radius, tree.crown_center, tree.crown_radius, tree.leaf_card_count, tree.branch_count });
        }
        for (long kind2 = 0; kind2 < 4; kind2++)
        {
            mesh_stamp(G.format("habitat_%d", kind2), HabitatDiversity.plant_mesh(kind2, 60011 + kind2 * 113));
        }
        for (long kind3 = 0; kind3 < 2; kind3++)
        {
            mesh_stamp(G.format("seed_heads_%d", kind3), MeadowPlants.seed_heads(kind3, 4021 + kind3 * 38));
        }
        mesh_stamp("grass", GrassPlanter.clump_mesh(24, 5, 0.011, 4021, 3.0, 2.15, 0.18));
        Canoe boat = new Canoe();
        mesh_stamp("canoe_hull", boat._hull_mesh());
        mesh_stamp("canoe_trim", boat._trim_mesh());
        boat.Free();
        Tent tent = new Tent(field);
        mesh_stamp("tent_canvas", tent._canvas_mesh());
        mesh_stamp("tent_hem", tent._hem_mesh());
        mesh_stamp("tent_poles", tent._pole_mesh());
        tent.Free();
        foreach (Variant sequence in new Godot.Collections.Array { "one_night", "showcase", "arrival", "pond", "nightfall", "afterglow" })
        {
            Godot.Collections.Array poses = new Godot.Collections.Array();
            foreach (Cinematic.Shot shot in Cinematic.sequence(sequence.AsString()))
            {
                Spline path = new Spline(Cinematic.resolve(field, shot.path, shot.absolute));
                Spline target = new Spline(Cinematic.resolve(field, shot.look, shot.absolute));
                poses.Add(new Godot.Collections.Array { shot.label, shot.duration, shot.fov, shot.fade_in, shot.fade_out, shot.walk, path.total_length() });
                for (long frame = 0; frame < 51; frame++)
                {
                    double seconds = (double)frame * shot.duration / 50.0;
                    Vector3 p = Cinematic.camera_position(shot, path, field, seconds);
                    double u = Cinematic.motion_progress(shot, seconds);
                    poses.Add(new Godot.Collections.Array { p, Cinematic.aim_target(shot, p, target.sample(u), seconds) });
                }
            }
            stamp(G.op("+", "camera/", sequence).AsString(), poses);
            stamp(G.op("+", "score/", sequence).AsString(), Cinematic.score_cues(sequence.AsString()));
        }
        Godot.Collections.Array water = new Godot.Collections.Array();
        for (long i = 0; i < 401; i++)
        {
            Vector2 p2 = new Vector2((float)sin(i * 1.5), (float)cos(i * 2.6)) * 25.0f;
            water.Add(PondSurface.displacement(p2, i * 0.31, WorldController.WIND_DIRECTION, i % 41 * 0.1));
        }
        stamp("water", water);
        stamp("noise", Variant.From(Synth.pink_noise(22050, 9817).ToArray()));
        stamp("filtered_noise", Variant.From(Synth.low_pass(Synth.white_noise(22050, 5741), 1300.0).ToArray()));
        ScannedDressing dressing = new ScannedDressing();
        dressing.setup_from(field, plan);
        foreach (Variant key_key in dressing._placements.Keys)
        {
            string key = key_key.AsString();
            stamp("scanned/" + key, G.Index(dressing._placements[key], "transforms"));
        }
        dressing.Free();
        Understory understory = new Understory(field, plan);
        stamp("meadow_plan", understory._plan_meadow_grasses());
        understory.Free();
        GrassPlanter planter = new GrassPlanter(field, 24.0, 1.0);
        planter.bake_suitability();
        foreach (Variant origin_item in new Godot.Collections.Array { new Vector2(-16, -16), new Vector2(8, -8), new Vector2(-8, 16) })
        {
            Vector2 origin = origin_item.AsVector2();
            RandomNumberGenerator rng = new RandomNumberGenerator();
            rng.Seed = unchecked((ulong)(98371));
            GrassPlanter.Chunk chunk = planter._plan_chunk(origin, rng);
            stamp("grass_chunk/" + G.str(origin), new Godot.Collections.Array { chunk.count, chunk.aabb, Variant.From(chunk.buffer.ToArray()) });
        }
        RandomNumberGenerator random = new RandomNumberGenerator();
        random.Seed = unchecked((ulong)(27182));
        Godot.Collections.Array draw_order = new Godot.Collections.Array();
        for (long i3 = 0; i3 < 1000; i3++)
        {
            double size = random.RandfRange(0.9f, 1.65f);
            // The original expression evaluated the scale argument before its receiver.
            Vector3 scale = new((float)size, (float)(size * random.RandfRange(0.75f, 1.15f)), (float)size);
            draw_order.Add(new Basis(Vector3.Up, (float)(random.Randf() * TAU)).Scaled(scale));
        }
        stamp("random_receiver_order", draw_order);
        AudioDirector director = new AudioDirector();
        for (long group = 0, group_end = AudioDirector.GENERATION_GROUPS; group < group_end; group++)
        {
            Godot.Collections.Dictionary into = new Godot.Collections.Dictionary();
            director._generate_group(group, into);
            audio_stamp(G.format("sound_bank/%d", group), into);
        }
        director.Free();
        FileAccess output = FileAccess.Open(Game.Instance.arg_value("fingerprints", "res://local-data/fingerprints.json"), FileAccess.ModeFlags.Write);
        output.StoreString(Json.Stringify(result, "\t", true) + "\n");
        output.Close();
        G.print("CONTENT_FINGERPRINTS ", (long)result.Count);
    }

    public void audio_stamp(string label, Variant item)
    {
        if (item.Obj is AudioStreamWav)
        {
            stamp(label, new Godot.Collections.Array { (long)item.As<AudioStreamWav>().Format, item.As<AudioStreamWav>().MixRate, item.As<AudioStreamWav>().Stereo, (long)item.As<AudioStreamWav>().LoopMode, item.As<AudioStreamWav>().LoopBegin, item.As<AudioStreamWav>().LoopEnd, item.As<AudioStreamWav>().Data });
        }
        else if (item.Obj is AudioDirector.FootstepSet)
        {
            audio_stamp(label + "/left", item.As<AudioDirector.FootstepSet>().left);
            audio_stamp(label + "/right", item.As<AudioDirector.FootstepSet>().right);
        }
        else if (item.VariantType == Variant.Type.Dictionary)
        {
            foreach (Variant key_item in G.Iter(item))
            {
                Variant key = key_item;
                audio_stamp(label + "/" + G.str(key), G.Index(item, key));
            }
        }
        else if (item.VariantType == Variant.Type.Array)
        {
            foreach (Variant i_item in G.Iter(G.Call(item, "size")))
            {
                Variant i = i_item;
                audio_stamp(label + G.format("/%d", i), G.Index(item, i));
            }
        }
        else
        {
            G.assert(false, "Unexpected audio bank value");
        }
    }
}
