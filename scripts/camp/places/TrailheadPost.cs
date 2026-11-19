#nullable disable
using System;
using System.Collections.Generic;
using Godot;

namespace LastCamp;

/// The post Tom carved at the trailhead: a square cedar post with HALLOWAY 1974
/// cut into its face, running down it like a book spine, the seven turned the
/// wrong way round (he said it was on purpose), and a small register box on its
/// side. The letters are real grooves in the face's geometry, not a texture.
public partial class TrailheadPost : Node3D
{
    public const double WIDTH = 0.15;
    public const double ABOVE_GROUND = 1.3;
    public const double BURIED = 0.4;
    public const string TEXT = "HALLOWAY 1974";
    /// Cap height, across the face.
    public const double LETTER_HEIGHT = 0.095;
    /// Letter width, down the face.
    public const double LETTER_WIDTH = 0.060;
    public const double ROW_PITCH = 0.072;
    public const double WORD_GAP = 0.05;
    public const double TOP_MARGIN = 0.09;
    public const double GROOVE_HALF_WIDTH = 0.0075;
    public const double GROOVE_DEPTH = 0.009;
    /// Grid spacing of the carved face.
    public const double CARVE_STEP = 0.0025;

    public TerrainField field;
    public StaticBody3D body;

    public TrailheadPost(TerrainField p_field)
    {
        field = p_field;
        Name = "TrailheadPost";
    }

    public TrailheadPost()
    {
    }

    /// Unit-box strokes for the letters Tom cut. `7` is drawn mirrored.
    public static Vector2[][] glyph(char c)
    {
        switch (c)
        {
            case 'H': return new[] { new Vector2[] { new(0, 0), new(0, 1) }, new Vector2[] { new(1, 0), new(1, 1) }, new Vector2[] { new(0, 0.52f), new(1, 0.52f) } };
            case 'A': return new[] { new Vector2[] { new(0, 0), new(0.5f, 1), new(1, 0) }, new Vector2[] { new(0.22f, 0.4f), new(0.78f, 0.4f) } };
            case 'L': return new[] { new Vector2[] { new(0.05f, 1), new(0.05f, 0), new(0.9f, 0) } };
            case 'O': return new[] { ellipse(new Vector2(0.5f, 0.5f), 0.47f, 0.5f, 0.0, 1.0, 20) };
            case 'W': return new[] { new Vector2[] { new(0, 1), new(0.22f, 0), new(0.5f, 0.66f), new(0.78f, 0), new(1, 1) } };
            case 'Y': return new[] { new Vector2[] { new(0, 1), new(0.5f, 0.48f), new(1, 1) }, new Vector2[] { new(0.5f, 0.48f), new(0.5f, 0) } };
            case '1': return new[] { new Vector2[] { new(0.25f, 0.78f), new(0.56f, 1), new(0.56f, 0) }, new Vector2[] { new(0.28f, 0), new(0.84f, 0) } };
            case '9': return new[] { ellipse(new Vector2(0.5f, 0.69f), 0.4f, 0.31f, 0.0, 1.0, 16), new Vector2[] { new(0.9f, 0.69f), new(0.84f, 0.3f), new(0.6f, 0.02f), new(0.2f, 0.06f) } };
            // Backwards: the bar runs from the right and the stem falls to the right.
            case '7': return new[] { new Vector2[] { new(1, 1), new(0, 1), new(0.68f, 0) } };
            case '4': return new[] { new Vector2[] { new(0.72f, 0), new(0.72f, 1), new(0, 0.34f), new(1, 0.34f) } };
            default: return Array.Empty<Vector2[]>();
        }
    }

    static Vector2[] ellipse(Vector2 c, float rx, float ry, double from, double to, int n)
    {
        var pts = new Vector2[n + 1];
        for (int i = 0; i <= n; i++)
        {
            double a = (from + (to - from) * i / n) * Math.Tau;
            pts[i] = c + new Vector2((float)(Math.Cos(a) * rx), (float)(Math.Sin(a) * ry));
        }
        return pts;
    }

    /// Every stroke of the inscription in face coordinates: x across the face
    /// (centred), y up from the post's foot at ground level.
    public static List<(Vector2 a, Vector2 b)> strokes()
    {
        var result = new List<(Vector2, Vector2)>();
        double y_top = ABOVE_GROUND - TOP_MARGIN;
        int row = 0;
        double gap = 0.0;
        foreach (char c in TEXT)
        {
            if (c == ' ')
            {
                gap += WORD_GAP;
                continue;
            }
            // The words run down the face, letters turned a quarter so their tops
            // face right, as on a book's spine: big enough to read from the trail.
            double top = y_top - row * ROW_PITCH - gap;
            Vector2 place(Vector2 g) => new((float)(-LETTER_HEIGHT * 0.5 + g.Y * LETTER_HEIGHT), (float)(top - g.X * LETTER_WIDTH));
            foreach (Vector2[] line in glyph(c))
            {
                for (int i = 0; i + 1 < line.Length; i++) result.Add((place(line[i]), place(line[i + 1])));
            }
            row++;
        }
        return result;
    }

    /// Depth of the carving at a face point: a V groove along each stroke.
    public static double carve_depth(Vector2 q, List<(Vector2 a, Vector2 b)> lines)
    {
        double best = double.PositiveInfinity;
        foreach (var (a, b) in lines) best = Math.Min(best, q.DistanceTo(PlacesLayout.closest_on_segment(q, a, b)));
        if (best >= GROOVE_HALF_WIDTH) return 0.0;
        double t = best / GROOVE_HALF_WIDTH;
        return GROOVE_DEPTH * (1.0 - t * t * 0.85);
    }

    /// The post's mesh: local +Z is the lettered face, the foot is at y = 0 on
    /// the ground, and it runs BURIED metres below.
    public static ArrayMesh build_post_mesh()
    {
        var mb = new MeshBuilder();
        float h = (float)WIDTH * 0.5f;
        float top = (float)ABOVE_GROUND, foot = -(float)BURIED;
        Color plain = new(0.86f, 0.84f, 0.80f);
        // Back and sides: flat sawn faces with the grain running up the post.
        void face(Vector3 o, Vector3 across, Vector3 up, float w, float hgt)
        {
            Vector3 n = across.Cross(up).Normalized();
            Vector4 tan = new(across.X, across.Y, across.Z, 1.0f);
            long a = mb.add_vertex(o, n, new Vector2(0, 0), plain, tan);
            long b = mb.add_vertex(o + across * w, n, new Vector2(w, 0), plain, tan);
            long c = mb.add_vertex(o + across * w + up * hgt, n, new Vector2(w, hgt * 0.5f), plain, tan);
            long d = mb.add_vertex(o + up * hgt, n, new Vector2(0, hgt * 0.5f), plain, tan);
            mb.add_triangle(a, c, b);
            mb.add_triangle(a, d, c);
        }
        face(new Vector3(h, foot, -h), Vector3.Left, Vector3.Up, (float)WIDTH, top - foot);      // back (-Z)
        face(new Vector3(h, foot, h), Vector3.Forward, Vector3.Up, (float)WIDTH, top - foot);    // right (+X)
        face(new Vector3(-h, foot, -h), Vector3.Back, Vector3.Up, (float)WIDTH, top - foot);     // left (-X)
        // The lettered face, below the carving: plain down into the ground.
        float carve_from = (float)(ABOVE_GROUND - TOP_MARGIN - 13 * ROW_PITCH);
        face(new Vector3(-h, foot, h), Vector3.Right, Vector3.Up, (float)WIDTH, carve_from - foot);
        // The carved band, as a fine grid displaced into the face.
        var lines = strokes();
        int cols = (int)Math.Round(WIDTH / CARVE_STEP);
        int rows = (int)Math.Round((top - carve_from) / CARVE_STEP);
        var pts = new Vector3[(cols + 1) * (rows + 1)];
        var depth = new double[pts.Length];
        for (int j = 0; j <= rows; j++)
        {
            for (int i = 0; i <= cols; i++)
            {
                Vector2 q = new((float)(-h + i * CARVE_STEP), (float)(carve_from + j * CARVE_STEP));
                // Keep the rim of the face whole so the edges stay square.
                double d = (i == 0 || i == cols || j == rows) ? 0.0 : carve_depth(q, lines);
                depth[j * (cols + 1) + i] = d;
                pts[j * (cols + 1) + i] = new Vector3(q.X, q.Y, (float)(h - d));
            }
        }
        var idx = new long[pts.Length];
        for (int j = 0; j <= rows; j++)
        {
            for (int i = 0; i <= cols; i++)
            {
                int k = j * (cols + 1) + i;
                Vector3 dx = pts[j * (cols + 1) + Math.Min(i + 1, cols)] - pts[j * (cols + 1) + Math.Max(i - 1, 0)];
                Vector3 dy = pts[Math.Min(j + 1, rows) * (cols + 1) + i] - pts[Math.Max(j - 1, 0) * (cols + 1) + i];
                Vector3 n = dx.Cross(dy).Normalized();
                // Dirt and shadow settle in the grooves.
                float shade = (float)(1.0 - 0.62 * Math.Min(depth[k] / GROOVE_DEPTH, 1.0));
                idx[k] = mb.add_vertex(pts[k], n, new Vector2(pts[k].X + h, pts[k].Y * 0.5f), plain * shade, new Vector4(1, 0, 0, 1));
            }
        }
        for (int j = 0; j < rows; j++)
        {
            for (int i = 0; i < cols; i++)
            {
                long a = idx[j * (cols + 1) + i], b = idx[j * (cols + 1) + i + 1];
                long c = idx[(j + 1) * (cols + 1) + i + 1], d = idx[(j + 1) * (cols + 1) + i];
                mb.add_triangle(a, c, b);
                mb.add_triangle(a, d, c);
            }
        }
        // A low pyramid cap sheds the rain.
        Vector3 apex = new(0, top + 0.035f, 0);
        Vector3[] rim = { new(-h, top, h), new(h, top, h), new(h, top, -h), new(-h, top, -h) };
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = rim[i], b = rim[(i + 1) % 4];
            Vector3 n = (b - a).Cross(apex - a).Normalized();
            if (n.Y < 0) n = -n;
            long ia = mb.add_vertex(a, n, new Vector2(0, 0), plain * 0.92f, new Vector4(1, 0, 0, 1));
            long ib = mb.add_vertex(b, n, new Vector2((float)WIDTH, 0), plain * 0.92f, new Vector4(1, 0, 0, 1));
            long ic = mb.add_vertex(apex, n, new Vector2(h, h), plain * 0.92f, new Vector4(1, 0, 0, 1));
            mb.add_triangle(ia, ic, ib);
        }
        return mb.commit();
    }

    public void build(Vector2 at, Vector2 face_towards)
    {
        float ground = (float)field.height(at.X, at.Y);
        Position = new Vector3(at.X, ground, at.Y);
        Vector2 d = (face_towards - at).Normalized();
        // Local +Z turns to face `face_towards`.
        Rotation = new Vector3(0, (float)Math.Atan2(d.X, d.Y), 0);
        // On a slope the uphill side of the post meets the ground highest.
        float lowest = ground;
        foreach (Vector2 c in footprint(at, (float)WIDTH * 0.5f)) lowest = Math.Min(lowest, (float)field.height(c.X, c.Y));
        Position = new Vector3(at.X, lowest, at.Y);

        ShaderMaterial cedar = PropMaterials.wood(new Color(0.83f, 0.78f, 0.72f), 0.85, 0.0, 1.1);
        MeshInstance3D post = FieldKit.add(this, build_post_mesh(), cedar, Vector3.Zero, "CarvedPost");
        post.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        post.GIMode = GeometryInstance3D.GIModeEnum.Static;

        // The register box hangs on the post's right-hand side under a sloped lid.
        var box = new Node3D { Name = "RegisterBox", Position = new Vector3((float)(WIDTH * 0.5 + 0.06), 1.02f, 0.0f) };
        AddChild(box);
        ShaderMaterial board = PropMaterials.wood(new Color(0.78f, 0.72f, 0.64f), 0.7, 0.0, 1.1);
        FieldKit.add(box, FieldKit.rounded_box(new Vector3(0.12f, 0.22f, 0.20f), 0.006), board, Vector3.Zero, "Body");
        MeshInstance3D lid = FieldKit.add(box, FieldKit.rounded_box(new Vector3(0.15f, 0.018f, 0.23f), 0.004), board, new Vector3(0.005f, 0.122f, 0.0f), "Lid");
        lid.Rotation = new Vector3(0, 0, -0.22f);
        FieldKit.add(box, FieldKit.rounded_box(new Vector3(0.006f, 0.045f, 0.022f), 0.002), PropMaterials.iron(), new Vector3(0.063f, 0.085f, 0.0f), "Hasp");

        body = new StaticBody3D { Name = "PostBody", CollisionLayer = 1, CollisionMask = 0 };
        body.SetMeta("surface", (StringName)"wood");
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3((float)WIDTH, (float)ABOVE_GROUND, (float)WIDTH) }, Position = new Vector3(0, (float)ABOVE_GROUND * 0.5f, 0) });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.13f, 0.25f, 0.22f) }, Position = box.Position });
        AddChild(body);
    }

    public static Vector2[] footprint(Vector2 at, float half)
    {
        return new[] { at + new Vector2(-half, -half), at + new Vector2(half, -half), at + new Vector2(half, half), at + new Vector2(-half, half), at };
    }
}
