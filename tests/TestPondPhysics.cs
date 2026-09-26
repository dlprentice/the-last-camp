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

public partial class TestPondPhysics : TestCase
{
    public void test_buoyancy_supports_weight_and_drag_opposes_motion()
    {
        double mass = Canoe.FLOATING_MASS;
        long points = (long)Canoe.FLOAT_POINTS.Count;
        double resting = PondSurface.buoyancy(Canoe.DRAFT, 0.0, mass, Canoe.DRAFT, points);
        assert_near(resting * points, mass * 9.8, 0.0001, "rest draft supports total weight");
        assert_gt(PondSurface.buoyancy(Canoe.DRAFT, -0.1, mass, Canoe.DRAFT, points), resting, "descending hull is resisted");
        assert_lt(PondSurface.buoyancy(Canoe.DRAFT, 0.1, mass, Canoe.DRAFT, points), resting, "rising hull is resisted");
        assert_near(PondSurface.buoyancy(-0.1, -1.0, mass, Canoe.DRAFT, points), 0.0, 0.0, "dry probe cannot lift the boat");
    }

    public void test_wave_queries_stay_inside_conservative_envelope()
    {
        foreach (Variant strength_item in new Godot.Collections.Array { 0.0, 0.4, 1.5, 4.0 })
        {
            double strength = strength_item.AsDouble();
            for (long i2 = 0; i2 < 100; i2++)
            {
                Vector2 p = new Vector2((float)sin(i2 * 1.5), (float)cos(i2 * 2.6)) * 25.0f;
                double height = PondSurface.height_at(p, i2 * 0.31, WorldController.WIND_DIRECTION, strength);
                assert_true(is_finite(height), "height remains finite");
                assert_lt(absf(height - TerrainField.WATER_LEVEL), 0.012 * 2.1 * 1.3 + 0.00001, "analytic surface fits the fog envelope");
            }
        }
    }

    public void test_physical_canoe_has_balanced_support_points()
    {
        Vector3 moment = Vector3.Zero;
        foreach (Vector3 point in Canoe.FLOAT_POINTS)
        {
            moment += point.Cross(Vector3.Up);
        }
        assert_lt(moment.Length(), 0.00001, "equal buoyancy has no pitch or roll torque");
        Canoe canoe = new Canoe();
        assert_true(canoe is RigidBody3D, "canoe is a native physical body");
        assert_true(canoe.Freeze, "construction does not let an unconfigured body fall");
        canoe.Free();
    }

    public void test_canoe_shell_has_no_open_stems_or_rim_edges()
    {
        Canoe canoe = new Canoe();
        Godot.Collections.Array arrays = canoe._hull_mesh().SurfaceGetArrays(0);
        List<Vector3> vertices = G.ListFromVariant<Vector3>(arrays[(int)Mesh.ArrayType.Vertex]);
        List<int> triangles = G.ListFromVariant<int>(arrays[(int)Mesh.ArrayType.Index]);
        // Caps deliberately duplicate vertices for flat normals. Weld by position
        // before checking the finished surface, so the test measures actual holes.
        Godot.Collections.Dictionary welded = new Godot.Collections.Dictionary();
        List<int> ids = new List<int>();
        foreach (Vector3 point in vertices)
        {
            Vector3I key = new Vector3I((int)roundi(point.X * 1000000.0), (int)roundi(point.Y * 1000000.0), (int)roundi(point.Z * 1000000.0));
            if (!welded.ContainsKey(key))
            {
                welded[key] = (long)welded.Count;
            }
            ids.Add(welded[key].AsInt32());
        }
        Godot.Collections.Dictionary edges = new Godot.Collections.Dictionary();
        for (long i = 0, i_end = (long)triangles.Count; i < i_end; i += 3)
        {
            long a = triangles[(int)i];
            long b = triangles[(int)(i + 1)];
            long c = triangles[(int)(i + 2)];
            assert_gt((vertices[(int)b] - vertices[(int)a]).Cross(vertices[(int)c] - vertices[(int)a]).LengthSquared(), 1e-16, "shell triangles have nonzero area");
            foreach (Variant pair_item in new Godot.Collections.Array { new Vector2I((int)a, (int)b), new Vector2I((int)b, (int)c), new Vector2I((int)c, (int)a) })
            {
                Vector2I pair = pair_item.AsVector2I();
                long first = ids[pair.X];
                long second = ids[pair.Y];
                Vector2I edge = new Vector2I((int)mini(first, second), (int)maxi(first, second));
                edges[edge] = G.to_int(G.get(edges, edge, 0)) + 1;
            }
        }
        foreach (Variant edge_key in edges.Keys)
        {
            Vector2I edge2 = edge_key.AsVector2I();
            assert_eq(edges[edge2], 2, "each welded shell edge joins exactly two triangles");
        }
        canoe.Free();
    }

    public void test_canoe_seating_fits_the_loft_and_has_support()
    {
        Canoe canoe = new Canoe();
        MeshBuilder mesh = new MeshBuilder();
        canoe._add_cross_members(mesh);
        // Include face centres as well as the real board corners. The expected
        // boundary is sampled from the generated inner loft, not the seat-sizing
        // helper, so using the gunwale beam for a low seat fails this regression.
        List<Vector3> probes = new List<Vector3>(mesh.vertices);
        for (long i = 0, i_end = (long)mesh.indices.Count; i < i_end; i += 3)
        {
            probes.Add((mesh.vertices[mesh.indices[(int)i]] + mesh.vertices[mesh.indices[(int)(i + 1)]] + mesh.vertices[mesh.indices[(int)(i + 2)]]) / 3.0f);
        }
        foreach (Vector3 point in probes)
        {
            double width = _loft_width_at(point.Y, point.Z, true);
            assert_gt(width, 0.0, "cross-member stays above the floor and between the stems");
            assert_lt(absf(point.X), width - 0.001, "seat/thwart footprint stays inside the inner skin");
        }
        MeshBuilder bearers = new MeshBuilder();
        canoe._add_seat_bearers(bearers);
        assert_eq(bearers.vertex_count(), 4 * 24, "each low seat has two solid cross-bearers");
        foreach (Vector3 point2 in bearers.vertices)
        {
            assert_lt(absf(point2.X), _loft_width_at(point2.Y, point2.Z, false) - 0.001, "bearer joint stays behind the exterior skin");
        }
        for (long face = 0, face_end = bearers.vertex_count(); face < face_end; face += 4)
        {
            if (bearers.normals[(int)face].Dot(Vector3.Up) < 0.99)
            {
                continue;
            }
            Vector3 p = bearers.vertices[(int)face];
            double station = 0.5 - p.Z / Canoe.LENGTH;
            double seat_t = station < 0.5 ? 0.15 : 0.85;
            double underside = Canoe.rocker(seat_t) + Canoe.sheer(seat_t) - 0.13 - 0.015;
            assert_near(p.Y, underside, 1e-6, "bearer top meets seat underside");
            double left = INF;
            double right = -INF;
            for (long corner = 0; corner < 4; corner++)
            {
                Vector3 support = bearers.vertices[(int)(face + corner)];
                left = minf(left, support.X);
                right = maxf(right, support.X);
                assert_lt(absf(support.Z - (0.5 - seat_t) * Canoe.LENGTH), 0.15, "whole bearer top lies beneath the seat footprint");
            }
            assert_lt(left * right, 0.0, "bearer supports the seat across both sides of the centreline");
        }
        canoe.Free();
    }

    public double _loft_width_at(double y, double z, bool inside)
    {
        double t = 0.5 - z / Canoe.LENGTH;
        if (inside)
        {
            t = (t - Canoe.INNER_END_INSET) / (1.0 - 2.0 * Canoe.INNER_END_INSET);
        }
        if (t < 0.0 || t > 1.0)
        {
            return 0.0;
        }
        long station = clampi(floori(t * Canoe.STATIONS), 0, Canoe.STATIONS - 1);
        double blend = t * Canoe.STATIONS - station;
        double t0 = (double)station / Canoe.STATIONS;
        double t1 = (double)(station + 1) / Canoe.STATIONS;
        Vector3 previous = inside ? Canoe.inner_hull_point(t0, 0.0).Lerp(Canoe.inner_hull_point(t1, 0.0), (float)blend) : Canoe.hull_point(t0, 0.0).Lerp(Canoe.hull_point(t1, 0.0), (float)blend);
        if (y < previous.Y)
        {
            return 0.0;
        }
        for (long section = Canoe.SECTIONS / 2 + 1, section_end = Canoe.SECTIONS + 1; section < section_end; section++)
        {
            double u = 2.0 * (double)section / Canoe.SECTIONS - 1.0;
            Vector3 next = inside ? Canoe.inner_hull_point(t0, u).Lerp(Canoe.inner_hull_point(t1, u), (float)blend) : Canoe.hull_point(t0, u).Lerp(Canoe.hull_point(t1, u), (float)blend);
            if (y <= next.Y && next.Y > previous.Y)
            {
                return lerpf(previous.X, next.X, (y - previous.Y) / ((double)next.Y - previous.Y));
            }
            previous = next;
        }
        return previous.X;
    }
}
