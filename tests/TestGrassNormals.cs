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

public partial class TestGrassNormals : TestCase
{
    public void test_blade_normals_follow_the_rendered_curve()
    {
        // Recover tangents from the actual mesh positions instead of duplicating
        // the blade formula. The coarsest mesh still has three centre-line samples.
        // Lean along the ribbon width can barely rotate its normal, so validity
        // depends on perpendicularity, not a minimum root-to-tip angular change.
        foreach (Variant profile_item in new Godot.Collections.Array { new Godot.Collections.Array { 7, 1, 0.018, 77, 1.0, 1.0, 0.0 }, new Godot.Collections.Array { 7, 5, 0.018, 77, 1.0, 1.0, 0.0 }, new Godot.Collections.Array { 24, 5, 0.011, 4021, 3.0, 2.15, 0.18 }, new Godot.Collections.Array { 18, 5, 0.020, 4059, 3.2, 2.5, 0.26 } })
        {
            Godot.Collections.Array profile = profile_item.AsGodotArray();
            long blades = profile[0].AsInt64();
            long segments = profile[1].AsInt64();
            ArrayMesh mesh = G.callv(G.Variadic(call_args => GrassPlanter.clump_mesh((call_args.Length > 0 ? call_args[0].AsInt64() : 7), (call_args.Length > 1 ? call_args[1].AsInt64() : 5), (call_args.Length > 2 ? call_args[2].AsDouble() : 0.018), (call_args.Length > 3 ? call_args[3].AsInt64() : 77), (call_args.Length > 4 ? call_args[4].AsDouble() : 1.0), (call_args.Length > 5 ? call_args[5].AsDouble() : 1.0), (call_args.Length > 6 ? call_args[6].AsDouble() : 0.0))), profile).As<ArrayMesh>();
            Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
            List<Vector3> vertices = G.ListFromVariant<Vector3>(arrays[(int)Mesh.ArrayType.Vertex]);
            List<Vector3> normals = G.ListFromVariant<Vector3>(arrays[(int)Mesh.ArrayType.Normal]);
            List<Vector2> uv2s = G.ListFromVariant<Vector2>(arrays[(int)Mesh.ArrayType.TexUV2]);
            long stride = (segments + 1) * 2 + 1;
            for (long blade = 0; blade < blades; blade++)
            {
                long @base = blade * stride;
                List<Vector3> centres = new List<Vector3>();
                for (long row = 0, row_end = segments + 1; row < row_end; row++)
                {
                    centres.Add((vertices[(int)(@base + row * 2)] + vertices[(int)(@base + row * 2 + 1)]) * 0.5f);
                }
                centres.Add(vertices[(int)(@base + stride - 1)]);
                Vector3 across = (vertices[(int)(@base + 1)] - vertices[(int)@base]).Normalized();
                for (long row2 = 0, row_end2 = (long)centres.Count; row2 < row_end2; row2++)
                {
                    Vector3 along = default;
                    if (row2 == 0)
                    {
                        along = (-3.0f) * centres[0] + 4.0f * centres[1] - centres[2];
                    }
                    else if (row2 == (long)centres.Count - 1)
                    {
                        along = 3.0f * centres[(int)row2] - 4.0f * centres[(int)(row2 - 1)] + centres[(int)(row2 - 2)];
                    }
                    else
                    {
                        along = centres[(int)(row2 + 1)] - centres[(int)(row2 - 1)];
                    }
                    long index = @base + mini(row2 * 2, stride - 1);
                    double dy_dv = along.Y * (double)(segments + 1) * 0.5;
                    assert_near(uv2s[(int)index].Y, dy_dv, 0.0001, "wind derivative agrees with the rendered arch");
                    assert_gt(dy_dv, 0.0, "arched blades do not fold through themselves vertically");
                    along = along.Normalized();
                    Vector3 normal = normals[(int)index];
                    assert_true(normal.IsFinite(), "each blade normal is finite");
                    assert_near(normal.Length(), 1.0, 0.0001, "each blade normal has unit length");
                    // Recovering a width vector from millimetre-wide float32 positions
                    // loses a little more precision on the longer arched blades.
                    assert_near(normal.Dot(across), 0.0, 0.0002, "normal is perpendicular to blade width");
                    assert_near(normal.Dot(along), 0.0, 0.0001, "normal follows the curved centre line");
                }
            }
        }
    }

    public void test_seed_geometry_has_finite_surfaces_and_grounded_stems()
    {
        for (long kind = 0; kind < 2; kind++)
        {
            ArrayMesh mesh = MeadowPlants.seed_heads(kind, 4021 + kind * 38);
            Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
            List<Vector3> vertices = G.ListFromVariant<Vector3>(arrays[(int)Mesh.ArrayType.Vertex]);
            List<Vector3> normals = G.ListFromVariant<Vector3>(arrays[(int)Mesh.ArrayType.Normal]);
            List<int> indices = G.ListFromVariant<int>(arrays[(int)Mesh.ArrayType.Index]);
            long invalid = 0;
            long degenerate = 0;
            long rooted = 0;
            for (long i = 0, i_end = (long)vertices.Count; i < i_end; i++)
            {
                if (!vertices[(int)i].IsFinite() || !normals[(int)i].IsFinite() || absf(normals[(int)i].Length() - 1.0) > 0.002)
                {
                    invalid += 1;
                }
                if (absf(vertices[(int)i].Y) < 0.002)
                {
                    rooted += 1;
                }
            }
            for (long i2 = 0, i_end2 = (long)indices.Count; i2 < i_end2; i2 += 3)
            {
                Vector3 a = vertices[indices[(int)i2]];
                Vector3 b = vertices[indices[(int)(i2 + 1)]];
                Vector3 c = vertices[indices[(int)(i2 + 2)]];
                if ((b - a).Cross(c - a).LengthSquared() < 1e-18)
                {
                    degenerate += 1;
                }
            }
            assert_eq(invalid, 0, "seed stems and spikelets have usable geometry normals");
            assert_eq(degenerate, 0, "tiny seed surfaces retain area after mesh packing");
            assert_gt(rooted, 8, "seed stems meet the soil instead of floating above basal leaves");
        }
    }

    public void test_tussock_footprints_preserve_routes_props_and_pond()
    {
        TerrainField field = new TerrainField();
        field.bake_height_grid(48.0, 0.5);
        Understory understory = new Understory(field, null);
        Godot.Collections.Dictionary groups = understory._plan_meadow_grasses(1800, 44.0);
        Godot.Collections.Array<ArrayMesh> meshes = new Godot.Collections.Array<ArrayMesh> { GrassPlanter.clump_mesh(24, 5, 0.011, 4021, 3.0, 2.15, 0.18), GrassPlanter.clump_mesh(18, 5, 0.020, 4059, 3.2, 2.5, 0.26) };
        List<int> forms = new List<int>(new List<int> { 0, 0 });
        double min_route = INF;
        double max_wear = 0.0;
        double min_water_clearance = INF;
        double min_height = INF;
        double max_height = 0.0;
        long seeds = 0;
        foreach (Variant key_key in groups.Keys)
        {
            Vector3I key = key_key.AsVector3I();
            Godot.Collections.Dictionary group = groups[key].AsGodotDictionary();
            List<Vector3> vertices = G.ListFromVariant<Vector3>(meshes[key.Z].SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex]);
            forms[key.Z] = G.op("+", forms[key.Z], G.Call(group["transforms"], "size")).AsInt32();
            seeds = G.op("+", seeds, G.Call(group["seed_transforms"], "size")).AsInt64();
            foreach (Variant transform_item in G.Iter(group["transforms"]))
            {
                Transform3D transform = transform_item.AsTransform3D();
                min_height = minf(min_height, transform.Basis.Y.Length());
                max_height = maxf(max_height, transform.Basis.Y.Length());
                assert_near(transform.Origin.Y, field.height_fast(transform.Origin.X, transform.Origin.Z) - 0.018, 0.0001, "tussock roots use the same soil surface as the base sward");
                // Check the emitted curved mesh, not only the centre accepted by the
                // scatterer: tall leaning leaves can otherwise pierce the clear path.
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 at = transform * vertex;
                    Vector2 p = new Vector2(at.X, at.Z);
                    min_route = minf(min_route, field.walking_distance(p));
                    max_wear = maxf(max_wear, TerrainField.camp_wear(p));
                    min_water_clearance = minf(min_water_clearance, field.height_fast(p.X, p.Y) - TerrainField.WATER_LEVEL);
                }
            }
        }
        assert_gt(forms[0], 0, "open panicle habitat is populated");
        assert_gt(forms[1], 0, "compact seed-head habitat is populated");
        assert_gt(seeds, 0, "some basal tufts carry seed stems");
        assert_gt(max_height - min_height, 0.20, "tussocks retain a visible range of heights");
        assert_gt(min_route, 0.52, "full tuft meshes clear the walking route");
        assert_lt(max_wear, 0.081, "full tuft meshes clear fire, seats, tent, table and woodpile pads");
        assert_gt(min_water_clearance, 0.09, "terrestrial tuft footprints stay above the pond");
        understory.Free();
    }
}
