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

/// A cedar-strip canoe: a lofted hull with rocker and sheer, gunwale rails,
/// thwarts, timber seats and a paddle across the thwarts. It floats on the pond
/// using six buoyancy samples, water drag and a compliant mooring. The hull's local origin is the keel line at the
/// waterline, so `position.y` is the water level minus the draft.
public partial class Canoe : RigidBody3D
{
    public const double LENGTH = 4.6;
    public const double HALF_BEAM = 0.43;
    public const double DEPTH = 0.34;
    public const double DRAFT = 0.11;
    /// Inner floor (ribs and slats) sits above the waterline so the open hull
    /// never shows the pond surface inside it.
    public const double FLOOR_HEIGHT = DRAFT + 0.045;
    public const long STATIONS = 96;
    public const long SECTIONS = 24;
    public const double INNER_END_INSET = 0.018;
    public const double INNER_WIDTH_SCALE = 0.94;
    public const double GUNWALE_OFFSET = 0.004;
    public static readonly Godot.Collections.Array CROSS_MEMBERS = new Godot.Collections.Array { new Vector3(0.30f, 0.08f, 0.0f), new Vector3(0.50f, 0.11f, 0.0f), new Vector3(0.70f, 0.08f, 0.0f), new Vector3(0.15f, 0.30f, -0.13f), new Vector3(0.85f, 0.30f, -0.13f) };

    public const double FLOATING_MASS = 55.0;
    public static readonly Godot.Collections.Array<Vector3> FLOAT_POINTS = new Godot.Collections.Array<Vector3> { new Vector3(-0.27f, 0, -1.30f), new Vector3(0.27f, 0, -1.30f), new Vector3(-0.32f, 0, 0), new Vector3(0.32f, 0, 0), new Vector3(-0.27f, 0, 1.30f), new Vector3(0.27f, 0, 1.30f) };
    public Pond pond;
    public Vector3 _moored_position = Vector3.Zero;
    public double _moored_yaw = 0.0;
    public List<float> _probe_heights = new List<float>();

    public Canoe()
    {
        Name = "Canoe";
        Freeze = true;
    }

    public void build(Color hull_tint)
    {
        ShaderMaterial hull_mat = PropMaterials.wood(hull_tint, 0.05, 0.45, 0.7);
        hull_mat.SetShaderParameter("use_shore_mask", true);
        ShaderMaterial trim_mat = PropMaterials.wood(new Color(0.55f, 0.38f, 0.24f), 0.15, 0.0, 0.85);
        trim_mat.SetShaderParameter("wet_band", 0.08);
        MeshInstance3D hull = new MeshInstance3D();
        hull.Name = "Hull";
        hull.Mesh = _hull_mesh();
        hull.MaterialOverride = hull_mat;
        hull.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        AddChild(hull);

        MeshInstance3D trim = new MeshInstance3D();
        trim.Name = "Trim";
        trim.Mesh = _trim_mesh();
        trim.MaterialOverride = trim_mat;
        trim.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        AddChild(trim);
        ShaderMaterial paddle_mat = PropMaterials.wood(new Color(0.55f, 0.38f, 0.24f), 0.05, 0.0, 1.05);
        paddle_mat.SetShaderParameter("normal_strength", 0.22);
        paddle_mat.SetShaderParameter("wet_band", 0.08);
        MeshInstance3D paddle = new MeshInstance3D();
        paddle.Name = "Paddle";
        MeshBuilder paddle_mesh = new MeshBuilder();
        _add_paddle(paddle_mesh);
        paddle.Mesh = paddle_mesh.commit();
        paddle.MaterialOverride = paddle_mat;
        paddle.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        AddChild(paddle);

        CollisionLayer = 1;
        CollisionMask = 1;
        SetMeta("surface", (StringName)"wood");
        Mass = (float)FLOATING_MASS;
        CanSleep = false;
        LinearDamp = 0.08f;
        AngularDamp = 0.35f;
        CenterOfMassMode = RigidBody3D.CenterOfMassModeEnum.Custom;
        CenterOfMass = new Vector3(0, 0.04f, 0);
        CollisionShape3D shape = new CollisionShape3D();
        BoxShape3D box = new BoxShape3D();
        box.Size = new Vector3((float)(HALF_BEAM * 2.0), (float)(DEPTH + 0.1), (float)(LENGTH * 0.96));
        shape.Shape = box;
        shape.Position = new Vector3(0.0f, (float)(DEPTH * 0.5), 0.0f);
        AddChild(shape);
        _moored_position = GlobalPosition;
        _moored_yaw = GlobalRotation.Y;
    }

    public static double sheer(double t)
    {
        /// Height of the sheer line (gunwale) above the keel line along the hull.
        return DEPTH + 0.15 * pow(1.0 - sin(t * PI), 1.6);
    }

    public static double rocker(double t)
    {
        /// Keel rocker: the ends of the keel lift clear of the water.
        return 0.07 * pow(1.0 - sin(t * PI), 1.4);
    }

    public static double half_width(double t)
    {
        return maxf(HALF_BEAM * pow(sin(t * PI), 0.62), 0.012);
    }

    public static Vector3 hull_point(double t, double u)
    {
        /// Hull surface point. `t` runs stern (0) to bow (1) along -Z; `u` runs from
        /// the port gunwale (-1) around the keel to the starboard gunwale (+1).
        double a = u * PI * 0.5;
        double x = half_width(t) * sin(a);
        double y = rocker(t) + sheer(t) * (1.0 - cos(a));
        double z = (0.5 - t) * LENGTH;
        return new Vector3((float)x, (float)y, (float)z);
    }

    public static Vector3 hull_outward(double u)
    {
        double a = u * PI * 0.5;
        return new Vector3((float)sin(a), (float)-cos(a), 0.0f);
    }

    public static Vector3 inner_hull_point(double t, double u)
    {
        /// The interior stops short of each solid stem. Its raised floor retains the
        /// water exclusion of the original hull without leaving a hole at either end.
        double station = lerpf(INNER_END_INSET, 1.0 - INNER_END_INSET, t);
        Vector3 p = hull_point(station, u);
        p.X = (float)(p.X * INNER_WIDTH_SCALE);
        p.Y = (float)maxf(p.Y, FLOOR_HEIGHT + rocker(station));
        return p;
    }

    public static double interior_half_width(double t, double y)
    {
        if (t < INNER_END_INSET || t > 1.0 - INNER_END_INSET || y < FLOOR_HEIGHT + rocker(t))
        {
            return 0.0;
        }
        double cosine = 1.0 - clampf((y - rocker(t)) / sheer(t), 0.0, 1.0);
        return half_width(t) * INNER_WIDTH_SCALE * sqrt(maxf(0.0, 1.0 - cosine * cosine));
    }

    public static Vector3 _skin_normal(double t, double u, bool inner)
    {
        Vector3 along = default;
        Vector3 across = default;
        if (inner)
        {
            along = inner_hull_point(minf(t + 0.001, 1.0), u) - inner_hull_point(maxf(t - 0.001, 0.0), u);
            across = inner_hull_point(t, minf(u + 0.001, 1.0)) - inner_hull_point(t, maxf(u - 0.001, -1.0));
        }
        else
        {
            along = hull_point(minf(t + 0.001, 1.0), u) - hull_point(maxf(t - 0.001, 0.0), u);
            across = hull_point(t, minf(u + 0.001, 1.0)) - hull_point(t, maxf(u - 0.001, -1.0));
        }
        Vector3 expected = inner ? -hull_outward(u) : hull_outward(u);
        Vector3 n = across.Cross(along);
        if (n.LengthSquared() < 1e-10)
        {
            return expected;
        }
        if (n.Dot(expected) < 0.0)
        {
            n = -n;
        }
        return n.Normalized();
    }

    public ArrayMesh _hull_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        List<List<int>> rows = new List<List<int>>();
        for (long i = 0, i_end = STATIONS + 1; i < i_end; i++)
        {
            double t = (double)i / (double)STATIONS;
            List<int> row = new List<int>();
            for (long j = 0, j_end = SECTIONS + 1; j < j_end; j++)
            {
                double u = (double)j / (double)SECTIONS * 2.0 - 1.0;
                Vector3 p = hull_point(t, u);
                Vector3 n = _skin_normal(t, u, false);
                // Strakes run along the hull: v follows the length, u wraps the girth.
                Vector2 uv = new Vector2((float)((u + 1.0) * 1.0), (float)(t * LENGTH));
                Vector4 tangent = MeshBuilder.tangent_for(n, Vector3.Right, Vector3.Forward);
                Color shade = new Color(1.0f, 1.0f, 1.0f).Lerp(new Color(0.86f, 0.8f, 0.72f), (float)(0.5 - 0.5 * cos((double)j * 1.7)));
                row.Add((int)mb.add_vertex(p, n, uv, shade, tangent));
            }
            rows.Add(row);
        }
        for (long i2 = 0; i2 < STATIONS; i2++)
        {
            for (long j2 = 0; j2 < SECTIONS; j2++)
            {
                long a = rows[(int)i2][(int)j2];
                long b = rows[(int)i2][(int)(j2 + 1)];
                long c = rows[(int)(i2 + 1)][(int)(j2 + 1)];
                long d = rows[(int)(i2 + 1)][(int)j2];
                double u2 = ((double)j2 + 0.5) / (double)SECTIONS * 2.0 - 1.0;
                mb.add_quad_facing(a, b, c, d, hull_outward(u2));
            }
        }
        // Inner skin: the same loft pulled inwards with its bottom flattened into
        // a floor above the waterline.
        List<List<int>> inner = new List<List<int>>();
        for (long i3 = 0, i_end2 = STATIONS + 1; i3 < i_end2; i3++)
        {
            double t2 = (double)i3 / (double)STATIONS;
            List<int> row2 = new List<int>();
            for (long j3 = 0, j_end2 = SECTIONS + 1; j3 < j_end2; j3++)
            {
                double u3 = (double)j3 / (double)SECTIONS * 2.0 - 1.0;
                Vector3 p2 = inner_hull_point(t2, u3);
                Vector3 n2 = _skin_normal(t2, u3, true);
                Vector2 uv2 = new Vector2((float)((u3 + 1.0) * 1.0), (float)(t2 * LENGTH));
                Vector4 tangent2 = MeshBuilder.tangent_for(n2, Vector3.Right, Vector3.Forward);
                row2.Add((int)mb.add_vertex(p2, n2, uv2, new Color(0.9f, 0.86f, 0.8f, 0.0f), tangent2));
            }
            inner.Add(row2);
        }
        for (long i4 = 0; i4 < STATIONS; i4++)
        {
            for (long j4 = 0; j4 < SECTIONS; j4++)
            {
                double u4 = ((double)j4 + 0.5) / (double)SECTIONS * 2.0 - 1.0;
                mb.add_quad_facing(inner[(int)i4][(int)j4], inner[(int)i4][(int)(j4 + 1)], inner[(int)(i4 + 1)][(int)(j4 + 1)], inner[(int)(i4 + 1)][(int)j4], -hull_outward(u4) + new Vector3(0.0f, 0.5f, 0.0f));
            }
        }
        // The outer/inner skins are one closed shell. End bulkheads, short stem
        // decks and the lip under each gunwale all share exact boundary positions.
        for (long i5 = 0; i5 < STATIONS; i5++)
        {
            foreach (Variant edge_item in new Godot.Collections.Array { 0, SECTIONS })
            {
                long edge = edge_item.AsInt64();
                mb.add_quad_facing(rows[(int)i5][(int)edge], rows[(int)(i5 + 1)][(int)edge], inner[(int)(i5 + 1)][(int)edge], inner[(int)i5][(int)edge], Vector3.Up);
            }
        }
        foreach (Variant end_item in new Godot.Collections.Array { 0, STATIONS })
        {
            long end = end_item.AsInt64();
            Vector3 outward = end == 0 ? Vector3.Back : Vector3.Forward;
            _add_end_cap(mb, rows[(int)end], outward, Colors.White);
            _add_end_cap(mb, inner[(int)end], -outward, new Color(0.9f, 0.86f, 0.8f, 0.0f));
            mb.add_quad_facing(rows[(int)end][0], rows[(int)end][(int)SECTIONS], inner[(int)end][(int)SECTIONS], inner[(int)end][0], Vector3.Up);
        }
        mb.recompute_tangents();
        // A single hero prop does not need automatic simplification of its narrow
        // sealed lip or stems; keep those connections at every camera distance.
        return mb.commit();
    }

    public void _add_end_cap(MeshBuilder mb, List<int> row, Vector3 normal, Color tint)
    {
        Vector3 centre = Vector3.Zero;
        foreach (int index in row)
        {
            centre += mb.vertices[index];
        }
        centre /= (float)(double)(long)row.Count;
        long middle = mb.add_vertex(centre, normal, new Vector2(0.37f, centre.Y), tint);
        List<int> rim = new List<int>();
        foreach (int index2 in row)
        {
            Vector3 p = mb.vertices[index2];
            rim.Add((int)mb.add_vertex(p, normal, new Vector2((float)(0.37 + p.X), p.Y), tint));
        }
        for (long j = 0, j_end = (long)rim.Count; j < j_end; j++)
        {
            long a = rim[(int)j];
            long b = rim[(int)((j + 1) % (long)rim.Count)];
            if ((mb.vertices[(int)a] - centre).Cross(mb.vertices[(int)b] - centre).Dot(normal) > 0.0)
            {
                mb.add_triangle(middle, b, a);
            }
            else
            {
                mb.add_triangle(middle, a, b);
            }
        }
    }

    public ArrayMesh _trim_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        Godot.Collections.Array<Vector3> gunwale = _gunwale_loop();
        Godot.Collections.Array<double> rail_widths = new Godot.Collections.Array<double>();
        Godot.Collections.Array<double> rail_heights = new Godot.Collections.Array<double>();
        foreach (Vector3 point in gunwale)
        {
            rail_widths.Add(0.010);
            rail_heights.Add(0.018);
        }
        _add_trim_sweep(mb, gunwale, rail_widths, rail_heights, true, 1, Colors.White);
        _add_cross_members(mb);
        _add_seat_bearers(mb);
        mb.recompute_tangents();
        return mb.commit();
    }

    public Godot.Collections.Array<Vector3> _gunwale_loop()
    {
        Godot.Collections.Array<Vector3> points = new Godot.Collections.Array<Vector3>();
        for (long i = 0, i_end = STATIONS + 1; i < i_end; i++)
        {
            double t = (double)i / (double)STATIONS;
            points.Add(hull_point(t, -1.0) + new Vector3((float)-GUNWALE_OFFSET, 0.008f, 0.0f));
        }
        // Rounded returns join the rails over both solid stems. The return radius
        // exceeds the rail's horizontal half-width, avoiding a pinched/self-cut tip.
        double radius = half_width(1.0) + GUNWALE_OFFSET;
        double tip_y = rocker(1.0) + sheer(1.0) + 0.008;
        for (long i2 = 1; i2 < 13; i2++)
        {
            double angle = PI * (1.0 - (double)i2 / 12.0);
            points.Add(new Vector3((float)(radius * cos(angle)), (float)tip_y, (float)(-LENGTH * 0.5 - radius * sin(angle))));
        }
        for (long i3 = STATIONS - 1; i3 > -1; i3 += -1)
        {
            double t2 = (double)i3 / (double)STATIONS;
            points.Add(hull_point(t2, 1.0) + new Vector3((float)GUNWALE_OFFSET, 0.008f, 0.0f));
        }
        for (long i4 = 1; i4 < 12; i4++)
        {
            double angle2 = PI * (double)i4 / 12.0;
            points.Add(new Vector3((float)(radius * cos(angle2)), (float)tip_y, (float)(LENGTH * 0.5 + radius * sin(angle2))));
        }
        return points;
    }

    public void _add_trim_sweep(MeshBuilder mb, Godot.Collections.Array<Vector3> points, Godot.Collections.Array<double> widths, Godot.Collections.Array<double> heights, bool closed, long strip, Color tint)
    {
        /// A closed rail or capped paddle part with one shared ring per station.
        /// The world-up frame is well-conditioned on these nearly horizontal paths;
        /// it is evaluated consistently at the closing ring rather than restarted.
        long SIDES = 16;
        long count = (long)points.Count;
        List<float> lengths = new List<float>(new List<float> { 0.0f });
        for (long i = 1; i < count; i++)
        {
            lengths.Add((float)((double)lengths[(int)(i - 1)] + points[(int)i].DistanceTo(points[(int)(i - 1)])));
        }
        double total = lengths[(int)(count - 1)] + (closed ? points[(int)(count - 1)].DistanceTo(points[0]) : 0.0);
        double v_scale = closed ? maxf(1.0, roundf(total)) / total : 1.0;
        long @base = mb.vertex_count();
        Godot.Collections.Array<Vector3> frames = new Godot.Collections.Array<Vector3>();
        for (long i2 = 0, i_end = count + (closed ? 1 : 0); i2 < i_end; i2++)
        {
            long index = i2 % count;
            long before = closed ? (index - 1 + count) % count : maxi(index - 1, 0);
            long after = closed ? (index + 1) % count : mini(index + 1, count - 1);
            Vector3 along = (points[(int)after] - points[(int)before]).Normalized();
            Vector3 side = Vector3.Up.Cross(along).Normalized();
            Vector3 up = along.Cross(side).Normalized();
            frames.Add(along);
            double v = i2 == count ? total * v_scale : lengths[(int)index] * v_scale;
            for (long j = 0, j_end = SIDES + 1; j < j_end; j++)
            {
                double a = TAU * (double)j / (double)SIDES;
                Vector3 p = points[(int)index] + side * (float)cos(a) * (float)widths[(int)index] + up * (float)sin(a) * (float)heights[(int)index];
                Vector3 n = (side * (float)cos(a) / (float)widths[(int)index] + up * (float)sin(a) / (float)heights[(int)index]).Normalized();
                Vector3 across = -side * (float)sin(a) * (float)widths[(int)index] + up * (float)cos(a) * (float)heights[(int)index];
                Vector2 uv = new Vector2((float)((double)posmod(strip, 4) * 0.25 + 0.018 + 0.214 * (double)j / SIDES), (float)v);
                mb.add_vertex(p, n, uv, tint, MeshBuilder.tangent_for(n, across, along));
            }
        }
        for (long i3 = 0, i_end2 = closed ? count : count - 1; i3 < i_end2; i3++)
        {
            for (long j2 = 0; j2 < SIDES; j2++)
            {
                long a2 = @base + i3 * (SIDES + 1) + j2;
                long b = a2 + 1;
                long c = b + SIDES + 1;
                long d = a2 + SIDES + 1;
                mb.add_quad_facing(a2, b, c, d, mb.normals[(int)a2] + mb.normals[(int)b]);
            }
        }
        if (!closed)
        {
            foreach (Variant end_item in new Godot.Collections.Array { 0, count - 1 })
            {
                long end = end_item.AsInt64();
                List<int> ring = new List<int>();
                for (long j3 = 0; j3 < SIDES; j3++)
                {
                    ring.Add((int)(@base + end * (SIDES + 1) + j3));
                }
                _add_end_cap(mb, ring, frames[(int)end] * (float)(end == 0 ? -1.0 : 1.0), tint);
            }
        }
    }

    public void _add_cross_members(MeshBuilder mb)
    {
        // Thwarts and the centre yoke, seats near the ends.
        foreach (Variant spec_item in CROSS_MEMBERS)
        {
            Vector3 spec = spec_item.AsVector3();
            double t = spec.X;
            double width = spec.Y;
            double drop = spec.Z;
            double y = rocker(t) + sheer(t) + drop;
            // The lower corners, including the edge nearest the narrow stem, must
            // fit inside the loft. Gunwale beam is not the usable width of a low seat.
            double half_span = HALF_BEAM;
            for (long sample = 0; sample < 9; sample++)
            {
                double station = t + lerpf(-width * 0.5, width * 0.5, (double)sample / 8.0) / LENGTH;
                half_span = minf(half_span, interior_half_width(station, y - 0.015));
            }
            double span = 2.0 * maxf(0.0, half_span - 0.004);
            Transform3D xf = new Transform3D(new Basis(Vector3.Up, (float)(PI * 0.5)), new Vector3(0.0f, (float)y, (float)((0.5 - t) * LENGTH)));
            PropMeshes.add_board(mb, xf, new Vector3((float)width, 0.03f, (float)span), 2, t * 3.0, new Color(0.95f, 0.93f, 0.9f));
        }
    }

    public void _add_seat_bearers(MeshBuilder mb)
    {
        // Two narrow cross-bearers meet each low seat's underside. Their bevelled
        // ends follow the inside skin with a 2 mm housed joint, while preserving
        // at least 3 mm of clearance from the outside of the hull.
        foreach (Variant seat_item in CROSS_MEMBERS)
        {
            Vector3 seat = seat_item.AsVector3();
            if (seat.Z >= 0.0)
            {
                continue;
            }
            double seat_bottom = rocker(seat.X) + sheer(seat.X) + seat.Z - 0.015;
            foreach (Variant offset_item in new Godot.Collections.Array { -0.105, 0.105 })
            {
                double offset = offset_item.AsDouble();
                double station = seat.X + offset / LENGTH;
                double y = seat_bottom - 0.013;
                double span = 2.0 * interior_half_width(station, y);
                Transform3D xf = new Transform3D(new Basis(Vector3.Up, (float)(PI * 0.5)), new Vector3(0.0f, (float)y, (float)((0.5 - station) * LENGTH)));
                long first = mb.vertex_count();
                PropMeshes.add_board(mb, xf, new Vector3(0.028f, 0.026f, (float)span), 2, station * 3.0, new Color(0.95f, 0.93f, 0.9f));
                for (long i3 = first, i_end = mb.vertex_count(); i3 < i_end; i3++)
                {
                    Vector3 p = mb.vertices[(int)i3];
                    double t = 0.5 - p.Z / LENGTH;
                    double inner_span = interior_half_width(t, p.Y);
                    double fitted = minf(inner_span + 0.002, inner_span / INNER_WIDTH_SCALE - 0.003);
                    Vector3 _t1 = mb.vertices[(int)i3];
                    _t1.X = (float)(signf(p.X) * fitted);
                    mb.vertices[(int)i3] = _t1;
                }
                // add_board has separate vertices for each face; retain crisp joinery
                // normals after beveling the end corners to the curved skin.
                for (long i4 = first, i_end2 = mb.vertex_count(); i4 < i_end2; i4 += 4)
                {
                    Vector3 n = (mb.vertices[(int)(i4 + 1)] - mb.vertices[(int)i4]).Cross(mb.vertices[(int)(i4 + 3)] - mb.vertices[(int)i4]).Normalized();
                    for (long corner = 0; corner < 4; corner++)
                    {
                        mb.normals[(int)(i4 + corner)] = n;
                    }
                }
            }
        }
    }

    public void _add_paddle(MeshBuilder mb)
    {
        // The paddle rests on the centre yoke and forward thwart. Its blade has a
        // rounded shoulder and thin oval edge; the hand grip is a rounded T, not a box.
        double centre_height = rocker(0.5) + sheer(0.5) + 0.015 + 0.016;
        double forward_height = rocker(0.7) + sheer(0.7) + 0.015 + 0.009;
        double rise = (forward_height - centre_height) / (LENGTH * 0.2);
        Vector3 grip = new Vector3(-0.32f, (float)(centre_height - 0.55 * rise), 0.55f);
        Vector3 tip = new Vector3(0.34f, (float)(centre_height + 0.95 * rise), -0.95f);
        Vector3 shaft_dir = (tip - grip).Normalized();
        Vector3 blade_start = grip.Lerp(tip, 0.65f);
        Color tint = new Color(0.9f, 0.8f, 0.62f);
        _add_trim_sweep(mb, new Godot.Collections.Array<Vector3> { grip, blade_start + shaft_dir * 0.04f }, new Godot.Collections.Array<double> { 0.016, 0.014 }, new Godot.Collections.Array<double> { 0.016, 0.014 }, false, 3, tint);
        Godot.Collections.Array<Vector3> blade_points = new Godot.Collections.Array<Vector3>();
        Godot.Collections.Array<double> widths = new Godot.Collections.Array<double>();
        Godot.Collections.Array<double> heights = new Godot.Collections.Array<double>();
        foreach (Variant shape_item in new Godot.Collections.Array { new Vector3(0.0f, 0.016f, 0.013f), new Vector3(0.13f, 0.045f, 0.012f), new Vector3(0.32f, 0.075f, 0.011f), new Vector3(0.66f, 0.085f, 0.010f), new Vector3(0.90f, 0.070f, 0.009f), new Vector3(0.985f, 0.033f, 0.007f), new Vector3(1.0f, 0.005f, 0.004f) })
        {
            Vector3 shape = shape_item.AsVector3();
            blade_points.Add(blade_start.Lerp(tip, shape.X));
            widths.Add(shape.Y);
            heights.Add(shape.Z);
        }
        _add_trim_sweep(mb, blade_points, widths, heights, false, 3, tint);
        Vector3 across = Vector3.Up.Cross(shaft_dir).Normalized();
        Godot.Collections.Array<Vector3> grip_points = new Godot.Collections.Array<Vector3>();
        foreach (Variant offset_item in new Godot.Collections.Array { -0.05, -0.042, 0.0, 0.042, 0.05 })
        {
            double offset = offset_item.AsDouble();
            grip_points.Add(grip + across * (float)offset);
        }
        _add_trim_sweep(mb, grip_points, new Godot.Collections.Array<double> { 0.003, 0.019, 0.021, 0.019, 0.003 }, new Godot.Collections.Array<double> { 0.003, 0.013, 0.014, 0.013, 0.003 }, false, 3, tint);
    }

    public Vector3 bow_point()
    {
        /// World-space point at the bow (the -Z end) for the mooring line.
        return ToGlobal(new Vector3(0.0f, (float)(rocker(1.0) + sheer(1.0) - 0.04), (float)(-LENGTH * 0.5 + 0.06)));
    }

    public void start_floating(Pond on_pond)
    {
        pond = on_pond;
        G.resize(_probe_heights, (int)(long)FLOAT_POINTS.Count);
        G.fill(_probe_heights, 0.0f);
        Freeze = false;
    }

    public List<Vector2> probe_positions()
    {
        List<Vector2> positions = new List<Vector2>();
        foreach (Vector3 point in FLOAT_POINTS)
        {
            Vector3 at = ToGlobal(point);
            positions.Add(new Vector2(at.X, at.Z));
        }
        return positions;
    }

    public void set_residual_heights(List<float> heights)
    {
        if ((long)heights.Count == (long)FLOAT_POINTS.Count)
        {
            _probe_heights = heights;
        }
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        if (pond == null)
        {
            return;
        }
        Transform3D pose = state.Transform;
        Vector2 wind = WorldController.WIND_DIRECTION.Normalized();
        double strength = Game.Instance.world != null ? Game.Instance.world.wind_strength() : 0.4;
        for (long i = 0, i_end = (long)FLOAT_POINTS.Count; i < i_end; i++)
        {
            Vector3 offset = pose.Basis * FLOAT_POINTS[(int)i];
            Vector3 at = pose.Origin + offset;
            double water = pond.base_height(new Vector2(at.X, at.Z)) + _probe_heights[(int)i];
            double immersion = water - at.Y;
            Vector3 point_velocity = state.LinearVelocity + state.AngularVelocity.Cross(offset - pose.Basis * CenterOfMass);
            double upward = PondSurface.buoyancy(immersion, point_velocity.Y, Mass, DRAFT, (long)FLOAT_POINTS.Count);
            double wet = clampf(immersion / DRAFT, 0.0, 1.0);
            Vector3 horizontal = new Vector3(point_velocity.X, 0, point_velocity.Z);
            Vector3 drag = -horizontal * (float)(6.0 + horizontal.Length() * 24.0) * (float)wet;
            state.ApplyForce(Vector3.Up * (float)upward + drag, offset);
        }
        // The mooring/fenders resist horizontal drift and yaw but leave heave,
        // pitch and roll free. Forces are integrated by Jolt, never assigned poses.
        Vector3 drift = pose.Origin - _moored_position;
        drift.Y = 0.0f;
        Vector3 velocity = new Vector3(state.LinearVelocity.X, 0, state.LinearVelocity.Z);
        state.ApplyCentralForce(-drift * 45.0f - velocity * 22.0f + new Vector3(wind.X, 0, wind.Y) * (float)strength * 2.0f);
        double heading = pose.Basis.GetEuler().Y;
        state.ApplyTorque(Vector3.Up * (float)(-wrapf(heading - _moored_yaw, -PI, PI) * 28.0 - state.AngularVelocity.Y * 16.0));
    }
}
