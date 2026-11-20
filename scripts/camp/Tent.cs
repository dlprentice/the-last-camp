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

/// A canvas A-frame tent: crossed poles front and back with a ridge pole,
/// one sheet of canvas draped over the ridge with sag between its pegs, a back
/// wall, tied-back door flaps, guy lines to stakes, a ground sheet, bedroll,
/// pack, and a lantern hung from the front crossing that glows through the
/// canvas at night. Local frame: origin at the ground centre, door towards -Z.
public partial class Tent : Node3D
{
    public const double WIDTH = 2.4;
    public const double LENGTH = 3.0;
    public const double HEIGHT = 1.6;
    public const double POLE_RADIUS = 0.028;
    public const double SAG = 0.105;
    /// Sewn panels along the ridge; the cloth bellies between the taut seams.
    public const double PANEL_WIDTH = 0.75;
    public const double CANVAS_TILE = 0.30;

    public TerrainField field;
    public CampLantern lantern;
    public ShaderMaterial _canvas;
    public ShaderMaterial _wood;
    public ShaderMaterial _rope;

    public Tent(TerrainField p_field)
    {
        field = p_field;
        Name = "Tent";
    }

    public Tent()
    {
    }

    public void build()
    {
        Vector2 pos = TerrainField.TENT;
        double y = field.height(pos.X, pos.Y);
        Vector3 to_fire = new Vector3(-pos.X, 0.0f, -pos.Y).Normalized();
        Position = new Vector3(pos.X, (float)y, pos.Y);
        Basis = Basis.LookingAt(to_fire, Vector3.Up);

        _canvas = new ShaderMaterial();
        _canvas.Shader = Content.Load<Shader>("res://shaders/canvas.gdshader");
        Camp.bind_prop_pbr(_canvas, "canvas");
        Camp.bind_texture(_canvas, "noise_tex", PropMaterials.NOISE);
        _wood = PropMaterials.wood(new Color(0.62f, 0.5f, 0.36f), 0.35, 0.0, 1.0);
        _rope = PropMaterials.rope();

        _add_mesh("Canvas", _canvas_mesh(), _canvas, true);
        _add_mesh("CanvasHems", _hem_mesh(), FieldKit.solid(new Color(0.28f, 0.24f, 0.16f), 0.96), false);
        _add_mesh("Poles", _pole_mesh(), _wood, true);
        _add_mesh("Lines", _line_mesh(), _rope, false);
        _add_mesh("Pegs", _peg_mesh(), PropMaterials.wood(new Color(0.45f, 0.36f, 0.26f), 0.5, 0.0, 1.0), false);
        _add_furniture();
        _add_tackle_tin();
        _add_lantern();
        _add_collision();
        _add_strike();
    }

    // ------------------------------------------------------------- Tom's tin

    public static readonly Vector3 TIN_SIZE = new(0.21f, 0.068f, 0.125f);
    public const double TIN_LID = 0.018;
    public const double TIN_OPEN_DEG = 104.0;
    /// On the floor to the right of the bedroll, a step inside the door.
    public static readonly Vector3 TIN_AT = new(0.30f, 0.004f, -0.45f);
    public const double TIN_YAW = 0.42;

    public Node3D tackle_tin;
    public Node3D tin_lid;
    public Interactable tin_body;
    public bool tin_open = false;

    /// Tom's tackle tin: a small painted steel box, its green worn to bare metal
    /// at the edges, a hinged lid with a wire latch. It sits flat on the ground
    /// sheet. Its interaction stays off until the story offers it.
    public void _add_tackle_tin()
    {
        tackle_tin = new Node3D { Name = "TackleTin", Position = TIN_AT, Rotation = new Vector3(0, (float)TIN_YAW, 0) };
        AddChild(tackle_tin);
        ShaderMaterial paint = PropMaterials.iron(new Color(0.24f, 0.30f, 0.22f));
        ShaderMaterial bare = PropMaterials.iron(new Color(0.42f, 0.40f, 0.36f));
        Vector3 s = TIN_SIZE;
        float body_h = s.Y - (float)TIN_LID;
        FieldKit.add(tackle_tin, FieldKit.rounded_box(new Vector3(s.X, body_h, s.Z), 0.006), paint, new Vector3(0, body_h * 0.5f, 0), "TinBox");
        // The lid hinges along the back edge (local +Z).
        tin_lid = new Node3D { Name = "TinLidHinge", Position = new Vector3(0, body_h, s.Z * 0.5f) };
        tackle_tin.AddChild(tin_lid);
        FieldKit.add(tin_lid, FieldKit.rounded_box(new Vector3(s.X + 0.004f, (float)TIN_LID, s.Z + 0.004f), 0.006), paint, new Vector3(0, (float)TIN_LID * 0.5f, -s.Z * 0.5f), "TinLid");
        MeshBuilder wire = new MeshBuilder();
        wire.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(-s.X * 0.5f, 0, 0.002f), new Vector3(s.X * 0.5f, 0, 0.002f) }, new Godot.Collections.Array<double> { 0.0028, 0.0028 }, 6);
        FieldKit.add(tin_lid, wire.commit(), bare, Vector3.Zero, "TinHinge");
        MeshBuilder latch = new MeshBuilder();
        latch.add_tube(new Godot.Collections.Array<Vector3> { new Vector3(-0.014f, body_h - 0.016f, -s.Z * 0.5f - 0.002f), new Vector3(-0.014f, body_h + 0.004f, -s.Z * 0.5f - 0.003f),
            new Vector3(0.014f, body_h + 0.004f, -s.Z * 0.5f - 0.003f), new Vector3(0.014f, body_h - 0.016f, -s.Z * 0.5f - 0.002f) }, new Godot.Collections.Array<double> { 0.0015, 0.0015, 0.0015, 0.0015 }, 5);
        FieldKit.add(tackle_tin, latch.commit(), bare, Vector3.Zero, "TinLatch");
        // Inside: the flat skipping stones on the tally book, seen once it is open.
        StandardMaterial3D page = FieldKit.solid(new Color(0.78f, 0.74f, 0.62f), 0.95);
        FieldKit.add(tackle_tin, FieldKit.rounded_box(new Vector3(s.X - 0.03f, 0.008f, s.Z - 0.025f), 0.002), page, new Vector3(0, body_h - 0.02f, 0), "TallyBook");
        ShaderMaterial pebble = PropMaterials.triplanar("rock", new Color(0.62f, 0.62f, 0.58f), 3.0);
        for (int i = 0; i < 4; i++)
        {
            MeshInstance3D stone = new MeshInstance3D { Name = $"TinStone{i + 1}", Mesh = PropMeshes.rock(9120 + i, 1.0), MaterialOverride = pebble,
                Position = new Vector3(-0.065f + i * 0.043f, body_h - 0.011f, (i % 2 == 0 ? -0.015f : 0.017f)),
                Rotation = new Vector3(0, i * 1.3f, 0), Scale = new Vector3(0.019f, 0.0055f, 0.016f) };
            tackle_tin.AddChild(stone);
        }
        tin_body = new Interactable { Name = "TinBody", CollisionLayer = 0, CollisionMask = 0, prompt_text = "Open" };
        tin_body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(s.X + 0.06f, 0.12f, s.Z + 0.06f) }, Position = new Vector3(0, 0.06f, 0) });
        tackle_tin.AddChild(tin_body);
    }

    /// Offers or withdraws the tin's interaction.
    public void offer_tin(bool offered) => tin_body.CollisionLayer = offered ? 1u << 1 : 0;

    /// Swings the lid open about its hinge, eased.
    public void open_tin()
    {
        if (tin_open) return;
        tin_open = true;
        Vector3 open = new((float)Mathf.DegToRad(TIN_OPEN_DEG), 0, 0);
        if (!IsInsideTree())
        {
            tin_lid.Rotation = open;
            return;
        }
        CreateTween().TweenProperty(tin_lid, "rotation", open, 0.7).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
    }

    // ------------------------------------------------------------- striking

    public Interactable strike_body;
    public bool struck = false;
    public const double PEG_SECONDS = 0.9;
    public const double FALL_SECONDS = 1.3;
    public const double PACK_SECONDS = 1.0;

    /// One collider over the whole tent that asks to strike it on the last
    /// morning. Off until the story offers it.
    public void _add_strike()
    {
        strike_body = new Interactable { Name = "StrikeTent", CollisionLayer = 0, CollisionMask = 0, prompt_text = "Strike" };
        strike_body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3((float)WIDTH, (float)HEIGHT, (float)LENGTH) }, Position = new Vector3(0, (float)(HEIGHT * 0.5), 0) });
        AddChild(strike_body);
    }

    public void offer_strike(bool offered) => strike_body.CollisionLayer = offered && !struck ? 1u << 1 : 0;

    /// Instant state restoration when loading an epilogue save where the tent was already struck.
    public void restore_struck()
    {
        struck = true;
        offer_strike(false);
        offer_tin(false);
        Visible = false;
        foreach (Node n in FindChildren("*", "CollisionObject3D", true, false))
            ((CollisionObject3D)n).CollisionLayer = 0;
    }

    /// Strikes the tent: the pegs come up out of the ground, the poles and the
    /// canvas come down flat, then everything is packed away. Calls `pegs_out`
    /// and `fallen` at those moments; completes when the pitch is bare.
    public async System.Threading.Tasks.Task strike(Action pegs_out = null, Action fallen = null)
    {
        if (struck) return;
        struck = true;
        offer_strike(false);
        offer_tin(false);
        var pegs = GetNode<Node3D>("Pegs");
        var lines = GetNode<Node3D>("Lines");
        if (IsInsideTree())
        {
            Tween pull = CreateTween();
            pull.TweenProperty(pegs, "position:y", 0.14f, PEG_SECONDS).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
            await ToSignal(pull, Tween.SignalName.Finished);
        }
        pegs.Visible = false;
        lines.Visible = false;
        pegs_out?.Invoke();
        // The canvas, hems and poles settle to the ground together: a fall that
        // starts slow and lands, flattened about the ground plane.
        var fallers = new List<Node3D>();
        foreach (string name in new[] { "Canvas", "CanvasHems", "Poles" })
            fallers.Add(GetNode<Node3D>(name));
        fallers.Add(lantern);
        if (IsInsideTree())
        {
            Tween fall = CreateTween().SetParallel(true);
            foreach (Node3D n in fallers)
                fall.TweenProperty(n, "scale:y", n == lantern ? 0.02f : 0.035f, FALL_SECONDS).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);
            await ToSignal(fall, Tween.SignalName.Finished);
        }
        fallen?.Invoke();
        if (IsInsideTree())
            await ToSignal(GetTree().CreateTimer(PACK_SECONDS), SceneTreeTimer.SignalName.Timeout);
        // Packed away: nothing of the tent is left on the pitch.
        Visible = false;
        foreach (Node n in FindChildren("*", "CollisionObject3D", true, false))
            ((CollisionObject3D)n).CollisionLayer = 0;
    }

    public MeshInstance3D _add_mesh(string node_name, ArrayMesh mesh, Material material, bool gi)
    {
        MeshInstance3D mi = new MeshInstance3D();
        mi.Name = node_name;
        mi.Mesh = mesh;
        mi.MaterialOverride = material;
        mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        mi.GIMode = gi ? GeometryInstance3D.GIModeEnum.Static : GeometryInstance3D.GIModeEnum.Disabled;
        AddChild(mi);
        return mi;
    }

    public static double ridge_y(double t)
    {
        /// Height of the ridge line at a point along the tent (rises at the poles).
        return HEIGHT - 0.03 * sin(t * PI);
    }

    public void _add_side(MeshBuilder mb, double side)
    {
        /// One sloping side as a sagging grid from the ridge to the ground line.
        long along = 48;
        long down = 24;
        List<List<int>> rows = new List<List<int>>();
        for (long j = 0, j_end = down + 1; j < j_end; j++)
        {
            double v = (double)j / (double)down;
            List<int> row = new List<int>();
            for (long i = 0, i_end = along + 1; i < i_end; i++)
            {
                double u = (double)i / (double)along;
                double z = (u - 0.5) * LENGTH;
                Vector3 p = _side_point(u, v, side);
                Vector2 uv = new Vector2((float)(z / CANVAS_TILE), (float)(v * WIDTH * 0.62 / CANVAS_TILE));
                Color color = new Color((float)(1.0 - v), (float)(sin(v * PI) * sin(u * PI)), 0.0f, 1.0f);
                Vector4 tangent = new Vector4(0.0f, 0.0f, 1.0f, 1.0f);
                row.Add((int)mb.add_vertex(p, Vector3.Up, uv, color, tangent));
            }
            rows.Add(row);
        }
        for (long j2 = 0; j2 < down; j2++)
        {
            for (long i2 = 0; i2 < along; i2++)
            {
                mb.add_quad_facing(rows[(int)j2][(int)i2], rows[(int)j2][(int)(i2 + 1)], rows[(int)(j2 + 1)][(int)(i2 + 1)], rows[(int)(j2 + 1)][(int)i2], new Vector3((float)side, 0.7f, 0.0f));
            }
        }
    }

    public static Vector3 _side_point(double u, double v, double side)
    {
        Vector3 p = new Vector3((float)(side * v * WIDTH * 0.5), (float)(ridge_y(u) * (1.0 - v)), (float)((u - 0.5) * LENGTH));
        double tension = sin(v * PI) * sin(u * PI);
        double sag = SAG * sin(v * PI) * (0.55 + 0.45 * sin(u * PI));
        sag += tension * (sin(u * 18.0 + v * 4.0) * 0.006 + sin(u * 27.0 - v * 3.0) * 0.003);
        double seam_phase = absf(fposmod((u - 0.5) * LENGTH / PANEL_WIDTH + 0.5, 1.0) - 0.5) * 2.0;
        sag += 0.016 * sin(v * PI) * smoothstep(0.0, 0.6, seam_phase);
        // The guy lines pull the hem taut at their pegs (u = 0.2, 0.8) and at
        // the poles; the cloth between hangs slack in a shallow scallop above
        // the ground line, never lifting the hem itself.
        double span = u < 0.2 ? u / 0.2 : (u < 0.8 ? (u - 0.2) / 0.6 : (u - 0.8) / 0.2);
        double hem_band = smoothstep(0.45, 0.88, v) * (1.0 - smoothstep(0.93, 1.0, v));
        sag += 0.032 * hem_band * sin(span * PI) * (u > 0.2 && u < 0.8 ? 1.0 : 0.6);
        // Tension folds fan up from each peg into the slack cloth.
        foreach (double peg in new[] { 0.2, 0.8 })
        {
            double du = u - peg;
            double rise = 1.0 - v;
            double fan = exp(-pow(du / (0.025 + rise * 0.16), 2.0)) * smoothstep(0.0, 0.25, rise) * (1.0 - smoothstep(0.35, 0.7, rise));
            sag += fan * 0.010 * sin(du / (0.02 + rise * 0.05) * 3.0);
        }
        return p - new Vector3((float)(side * 0.35), 1.0f, 0.0f).Normalized() * (float)sag;
    }

    public ArrayMesh _canvas_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        _add_side(mb, -1.0);
        _add_side(mb, 1.0);
        // Back wall: a triangle from the ridge to both peg lines.
        double back_z = LENGTH * 0.5;
        Vector3 apex = new Vector3(0.0f, (float)(ridge_y(1.0) - 0.02), (float)(back_z - 0.02));
        Vector3 bl = new Vector3((float)(-WIDTH * 0.5 + 0.03), 0.0f, (float)(back_z - 0.02));
        Vector3 br = new Vector3((float)(WIDTH * 0.5 - 0.03), 0.0f, (float)(back_z - 0.02));
        long a = mb.add_vertex(apex, Vector3.Back, new Vector2(0.5f, 0.0f), new Color(1, 0, 0, 1), new Vector4(1, 0, 0, 1));
        long b = mb.add_vertex(bl, Vector3.Back, new Vector2(0.0f, 1.8f), new Color(0, 0, 0, 1), new Vector4(1, 0, 0, 1));
        long c = mb.add_vertex(br, Vector3.Back, new Vector2(1.8f, 1.8f), new Color(0, 0, 0, 1), new Vector4(1, 0, 0, 1));
        mb.add_triangle(a, b, c);
        _add_gathered_doors(mb);
        _add_sod_cloth(mb);
        mb.recompute_normals();
        mb.recompute_tangents();
        return mb.commit();
    }

    public void _add_gathered_doors(MeshBuilder mb)
    {
        /// Door cloth gathers into pleats at a visible tie, clear of the roof panel.
        /// The hem at the front stays attached; the free edge is pulled toward it.
        foreach (Variant side_item in new Godot.Collections.Array { -1.0, 1.0 })
        {
            double side = side_item.AsDouble();
            long start = mb.vertex_count();
            for (long row = 0; row < 33; row++)
            {
                double v = (double)row / 32.0;
                double tie = exp(-pow((v - 0.58) / 0.15, 2.0));
                double width = sin(v * PI) * lerpf(0.26, 0.045, tie);
                for (long col = 0; col < 13; col++)
                {
                    double u = (double)col / 12.0;
                    Vector3 p = _side_point(0, v, side);
                    // Fold into the doorway, in front of the roof edge. No overlay
                    // triangle can cast a misleading dark patch across the side wall.
                    p.X = (float)(p.X - side * width * u);
                    p.Z = (float)(p.Z - (0.025 + sin(u * PI * 6.0) * sin(v * PI) * (0.020 + 0.015 * (1.0 - tie))));
                    p.Y = (float)(p.Y - sin(u * PI) * sin(v * PI) * 0.012);
                    mb.add_vertex(p, Vector3.Forward, new Vector2((float)(u * 0.6), (float)(v * 1.9)) / (float)CANVAS_TILE, new Color((float)(1.0 - v), (float)(u * sin(v * PI) * (1.0 - tie)), 0, 1));
                }
            }
            for (long row2 = 0; row2 < 32; row2++)
            {
                for (long col2 = 0; col2 < 12; col2++)
                {
                    long a = start + row2 * 13 + col2;
                    mb.add_quad_facing(a, a + 1, a + 14, a + 13, Vector3.Forward);
                }
            }
        }
    }

    public ArrayMesh _hem_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        foreach (Variant side_item in new Godot.Collections.Array { -1.0, 1.0 })
        {
            double side = side_item.AsDouble();
            foreach (Variant seam_u_item in new Godot.Collections.Array { 0.0, 0.5, 1.0 })
            {
                double seam_u = seam_u_item.AsDouble();
                Godot.Collections.Array<Vector3> points = new Godot.Collections.Array<Vector3>();
                Godot.Collections.Array<double> radii = new Godot.Collections.Array<double>();
                for (long i3 = 0; i3 < 33; i3++)
                {
                    points.Add(_side_point(seam_u, (double)i3 / 32.0, side) + new Vector3((float)side, 0.7f, 0).Normalized() * 0.002f);
                    radii.Add(0.0015);
                }
                mb.add_tube(points, radii, 4);
            }
            Vector3 at = _side_point(0, 0.58, side) + new Vector3((float)(-side * 0.020), 0, -0.035f);
            PropMeshes.add_rope(mb, at + new Vector3(-0.034f, 0.018f, 0), at + new Vector3(0.034f, -0.018f, 0), 0.002, 0.004, 5);
            PropMeshes.add_rope(mb, at, at + new Vector3((float)(side * 0.025), -0.095f, -0.014f), 0.007, 0.003, 5);
        }
        return mb.commit();
    }

    public void _add_sod_cloth(MeshBuilder mb)
    {
        /// Sod cloth: the strip of canvas that continues past each ground line and
        /// lies flat on the earth, sealing the tent. Without it the walls float.
        /// It carries no height (COLOR.r = 0), so the shader dirties it fully.
        FastNoiseLite noise = new FastNoiseLite();
        noise.Seed = 913;
        noise.Frequency = 2.0f;
        long segments = 12;
        double reach = 0.17;
        foreach (Variant side_item in new Godot.Collections.Array { -1.0, 1.0 })
        {
            double side = side_item.AsDouble();
            List<int> inner = new List<int>();
            List<int> outer = new List<int>();
            for (long i2 = 0, i_end = segments + 1; i2 < i_end; i2++)
            {
                double u = (double)i2 / (double)segments;
                double z = (u - 0.5) * LENGTH;
                double ripple = noise.GetNoise2D((float)(z * 3.0), (float)(side * 7.0));
                double x_in = side * (WIDTH * 0.5 - 0.02);
                double x_out = side * (WIDTH * 0.5 + reach + ripple * 0.03);
                Vector2 uv_in = new Vector2((float)(z / CANVAS_TILE), (float)(WIDTH * 0.62 / CANVAS_TILE));
                Vector2 uv_out = new Vector2((float)(z / CANVAS_TILE), (float)((WIDTH * 0.62 + reach) / CANVAS_TILE));
                inner.Add((int)mb.add_vertex(new Vector3((float)x_in, 0.004f, (float)z), Vector3.Up, uv_in, new Color(0.0f, 0.0f, 0.0f, 1.0f), new Vector4(0.0f, 0.0f, 1.0f, 1.0f)));
                outer.Add((int)mb.add_vertex(new Vector3((float)x_out, (float)(0.006 + absf(ripple) * 0.01), (float)z), Vector3.Up, uv_out, new Color(0.0f, 0.0f, 0.0f, 1.0f), new Vector4(0.0f, 0.0f, 1.0f, 1.0f)));
            }
            for (long i3 = 0; i3 < segments; i3++)
            {
                mb.add_quad_facing(inner[(int)i3], inner[(int)(i3 + 1)], outer[(int)(i3 + 1)], outer[(int)i3], Vector3.Up);
            }
        }
        // Along the back wall.
        double back_z = LENGTH * 0.5 - 0.02;
        List<int> b_in = new List<int>();
        List<int> b_out = new List<int>();
        for (long i4 = 0, i_end2 = segments + 1; i4 < i_end2; i4++)
        {
            double u2 = (double)i4 / (double)segments;
            double x = (u2 - 0.5) * (WIDTH - 0.06);
            double ripple2 = noise.GetNoise2D((float)(x * 3.0), 21.0f);
            b_in.Add((int)mb.add_vertex(new Vector3((float)x, 0.004f, (float)back_z), Vector3.Up, new Vector2((float)(x / CANVAS_TILE), 1.8f), new Color(0.0f, 0.0f, 0.0f, 1.0f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f)));
            b_out.Add((int)mb.add_vertex(new Vector3((float)x, (float)(0.006 + absf(ripple2) * 0.01), (float)(back_z + reach + ripple2 * 0.03)), Vector3.Up, new Vector2((float)(x / CANVAS_TILE), (float)(1.8 + reach / CANVAS_TILE)), new Color(0.0f, 0.0f, 0.0f, 1.0f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f)));
        }
        for (long i5 = 0; i5 < segments; i5++)
        {
            mb.add_quad_facing(b_in[(int)i5], b_in[(int)(i5 + 1)], b_out[(int)(i5 + 1)], b_out[(int)i5], Vector3.Up);
        }
    }

    public ArrayMesh _pole_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        long strip = 0;
        foreach (Variant end_item in new Godot.Collections.Array { -1.0, 1.0 })
        {
            double end = end_item.AsDouble();
            double z = end * (LENGTH * 0.5 + 0.02);
            foreach (Variant side_item in new Godot.Collections.Array { -1.0, 1.0 })
            {
                double side = side_item.AsDouble();
                Vector3 foot = new Vector3((float)(side * (WIDTH * 0.5 + 0.12)), -0.08f, (float)(z + end * 0.06));
                Vector3 top = new Vector3((float)(-side * 0.11), (float)(HEIGHT + 0.16), (float)z);
                PropMeshes.add_timber(mb, new Godot.Collections.Array<Vector3> { foot, foot.Lerp(top, 0.5f), top }, new Godot.Collections.Array<double> { POLE_RADIUS, POLE_RADIUS * 0.95, POLE_RADIUS * 0.85 }, 7, strip, Colors.White, 1.4);
                strip += 1;
            }
        }
        Vector3 ridge_a = new Vector3(0.0f, (float)(HEIGHT + 0.02), (float)(-LENGTH * 0.5 - 0.25));
        Vector3 ridge_b = new Vector3(0.0f, (float)(HEIGHT + 0.02), (float)(LENGTH * 0.5 + 0.25));
        PropMeshes.add_timber(mb, new Godot.Collections.Array<Vector3> { ridge_a, ridge_a.Lerp(ridge_b, 0.5f), ridge_b }, new Godot.Collections.Array<double> { 0.03, 0.032, 0.03 }, 7, 2, Colors.White, 1.4);
        return mb.commit(null, true);
    }

    public ArrayMesh _line_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        foreach (Variant end_item in new Godot.Collections.Array { -1.0, 1.0 })
        {
            double end = end_item.AsDouble();
            double z = end * (LENGTH * 0.5 + 0.02);
            // Lashing at each crossing.
            PropMeshes.add_rope_coil(mb, new Vector3(0.0f, (float)(HEIGHT - 0.01), (float)z), 0.05, 4, 0.022);
            // Guy line from the crossing out to a stake.
            Vector3 from = new Vector3(0.0f, (float)(HEIGHT + 0.04), (float)(z + end * 0.1));
            Vector3 to = new Vector3((float)(end * 0.25), 0.05f, (float)(z + end * 1.35));
            PropMeshes.add_rope(mb, from, to, 0.03, 0.009, 6);
        }
        // Side lines holding the peg edges taut.
        foreach (Variant side_item in new Godot.Collections.Array { -1.0, 1.0 })
        {
            double side = side_item.AsDouble();
            foreach (Variant t_item in new Godot.Collections.Array { 0.2, 0.8 })
            {
                double t = t_item.AsDouble();
                double z2 = (t - 0.5) * LENGTH;
                PropMeshes.add_rope(mb, new Vector3((float)(side * WIDTH * 0.5), 0.04f, (float)z2), new Vector3((float)(side * (WIDTH * 0.5 + 0.35)), 0.05f, (float)z2), 0.02, 0.008, 4);
            }
        }
        return mb.commit();
    }

    public ArrayMesh _peg_mesh()
    {
        MeshBuilder mb = new MeshBuilder();
        Godot.Collections.Array<Vector3> pegs = new Godot.Collections.Array<Vector3> { new Vector3(-0.25f, 0.0f, (float)(-LENGTH * 0.5 - 1.35)), new Vector3(0.25f, 0.0f, (float)(LENGTH * 0.5 + 1.35)) };
        foreach (Variant side_item in new Godot.Collections.Array { -1.0, 1.0 })
        {
            double side = side_item.AsDouble();
            foreach (Variant t_item in new Godot.Collections.Array { 0.2, 0.8 })
            {
                double t = t_item.AsDouble();
                pegs.Add(new Vector3((float)(side * (WIDTH * 0.5 + 0.35)), 0.0f, (float)((t - 0.5) * LENGTH)));
            }
        }
        foreach (Vector3 p in pegs)
        {
            Vector3 lean = new Vector3(-p.X, 0.0f, -p.Z).Normalized() * 0.08f;
            PropMeshes.add_timber(mb, new Godot.Collections.Array<Vector3> { p + new Vector3(0.0f, -0.12f, 0.0f), p + new Vector3(0.0f, 0.16f, 0.0f) + lean }, new Godot.Collections.Array<double> { 0.02, 0.018 }, 5, 1, new Color(0.9f, 0.85f, 0.78f), 0.8);
        }
        return mb.commit();
    }

    public void _add_furniture()
    {
        // Ground sheet: laid straight on the levelled pad, wrinkled, edges curled.
        MeshInstance3D sheet = new MeshInstance3D();
        sheet.Name = "GroundSheet";
        sheet.Mesh = PropMeshes.cloth_sheet_mesh(new Vector2((float)(WIDTH * 0.86), (float)(LENGTH * 0.9)), 4471);
        sheet.MaterialOverride = PropMaterials.triplanar("canvas", new Color(0.3f, 0.28f, 0.24f), 0.9, 0.0);
        sheet.Position = new Vector3(0.0f, 0.002f, 0.05f);
        sheet.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(sheet);

        Node3D bedroll = new Node3D();
        bedroll.Name = "Bedroll";
        bedroll.Position = new Vector3(-0.42f, 0.018f, 0.2f);
        AddChild(bedroll);
        ShaderMaterial cloth = PropMaterials.triplanar("canvas", new Color(0.35f, 0.42f, 0.3f), 3.0);
        FieldKit.add(bedroll, FieldKit.rounded_box(new Vector3(0.54f, 0.11f, 1.88f), 0.045), cloth, new Vector3(0, 0.055f, 0), "SleepingPad");
        MeshBuilder quilting = new MeshBuilder();
        for (long row = 0; row < 13; row++)
        {
            double z = -0.83 + row * 0.135;
            Godot.Collections.Array<Vector3> points = new Godot.Collections.Array<Vector3>();
            Godot.Collections.Array<double> radii = new Godot.Collections.Array<double>();
            for (long i = 0; i < 17; i++)
            {
                double t = (double)i / 16.0;
                points.Add(new Vector3((float)((t - 0.5) * 0.48), (float)(0.11 + sin(t * PI) * 0.0015), (float)z));
                radii.Add(0.0008);
            }
            quilting.add_tube(points, radii, 4);
        }
        FieldKit.add(bedroll, quilting.commit(), FieldKit.solid(new Color(0.16f, 0.22f, 0.12f), 0.96), Vector3.Zero, "QuiltStitching");
        FieldKit.add(bedroll, FieldKit.rounded_box(new Vector3(0.40f, 0.075f, 0.28f), 0.034), PropMaterials.triplanar("canvas", new Color(0.61f, 0.57f, 0.43f), 3.0), new Vector3(0, 0.1475f, 0.70f), "Pillow");
        // Someone sleeps here: a wool blanket rolled at the foot, and a book left
        // face-down by the pillow, open at the page they stopped on.
        MeshBuilder roll = new MeshBuilder();
        Godot.Collections.Array<Vector3> roll_axis = new Godot.Collections.Array<Vector3>();
        Godot.Collections.Array<double> roll_radii = new Godot.Collections.Array<double>();
        for (long i = 0; i < 9; i++)
        {
            double t = (double)i / 8.0;
            roll_axis.Add(new Vector3((float)((t - 0.5) * 0.50), (float)(0.185 + sin(t * 9.0) * 0.004), (float)(-0.80 + sin(t * PI) * 0.01)));
            roll_radii.Add(0.072 - 0.006 * absf(t - 0.5));
        }
        roll.add_tube(roll_axis, roll_radii, 12, null, 1.0, 1.0, 0.0, true);
        FieldKit.add(bedroll, roll.commit(), PropMaterials.triplanar("canvas", new Color(0.42f, 0.20f, 0.14f), 2.4), Vector3.Zero, "Blanket");
        Node3D book = new Node3D { Name = "Book", Position = new Vector3(0.13f, 0.112f, 0.36f), Rotation = new Vector3(0.0f, 0.42f, 0.0f) };
        bedroll.AddChild(book);
        // Spine up, the two halves of the cover tented over the open pages.
        foreach (float side in new[] { -1.0f, 1.0f })
        {
            MeshInstance3D cover = FieldKit.add(book, FieldKit.rounded_box(new Vector3(0.112f, 0.006f, 0.21f), 0.002), FieldKit.solid(new Color(0.30f, 0.12f, 0.08f), 0.8), new Vector3(side * 0.054f, 0.012f, 0.0f), side < 0 ? "CoverLeft" : "CoverRight");
            cover.Rotation = new Vector3(0.0f, 0.0f, side * -0.16f);
            MeshInstance3D pages = FieldKit.add(book, FieldKit.rounded_box(new Vector3(0.104f, 0.014f, 0.198f), 0.003), FieldKit.solid(new Color(0.80f, 0.75f, 0.62f), 0.9), new Vector3(side * 0.052f, 0.004f, 0.0f), side < 0 ? "PagesLeft" : "PagesRight");
            pages.Rotation = new Vector3(0.0f, 0.0f, side * -0.16f);
        }
        // Rounded waxed canvas with a front pocket, straps, buckles and a roll.

        Node3D pack = FieldKit.pack(this, new Vector3(0.28f, 0.014f, (float)(LENGTH * 0.5 - 0.57)));
        Vector3 _t1 = pack.Rotation;
        _t1.Y = -0.32f;
        pack.Rotation = _t1;
    }

    public void _add_lantern()
    {
        lantern = new CampLantern();
        lantern.Name = "TentLantern";
        lantern.attracts_moths = false;
        lantern.Position = new Vector3(0.0f, 0.0f, (float)(-LENGTH * 0.5 + 0.45));
        AddChild(lantern);
        // A rope from the ridge pole holds the lantern at head height.
        Vector3 ridge = new Vector3(0.0f, (float)(HEIGHT - 0.01), 0.0f);
        Vector3 hang = new Vector3(0.0f, (float)(HEIGHT - 0.22), 0.0f);
        lantern.build(false, Quality.Instance.current.lantern_shadows, false, hang);
        // Light through the cloth: the omni alone lights the inner face, but
        // canvas glows from within, brightest near the flame.
        Vector3 flame = new Vector3(0.0f, (float)(HEIGHT - 0.32), (float)(-LENGTH * 0.5 + 0.45));
        _canvas.SetShaderParameter("glow_position", IsInsideTree() ? ToGlobal(flame) : Transform * flame);
        _canvas.SetShaderParameter("lantern_glow", lantern.lit ? 1.0 : 0.0);
        lantern.toggled += on => _canvas.SetShaderParameter("lantern_glow", on ? 1.0 : 0.0);
        MeshBuilder mb = new MeshBuilder();
        PropMeshes.add_hang_link(mb, ridge, hang, 0.007);
        MeshInstance3D rope = new MeshInstance3D();
        rope.Mesh = mb.commit();
        rope.MaterialOverride = _rope;
        rope.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        lantern.AddChild(rope);
    }

    public void _add_collision()
    {
        StaticBody3D body = new StaticBody3D();
        body.CollisionLayer = unchecked((uint)(1));
        body.CollisionMask = unchecked((uint)(0));
        body.SetMeta("surface", (StringName)"canvas");
        foreach (Variant side_item in new Godot.Collections.Array { -1.0, 1.0 })
        {
            double side = side_item.AsDouble();
            CollisionShape3D shape = new CollisionShape3D();
            BoxShape3D box = new BoxShape3D();
            Vector2 slope = new Vector2((float)(WIDTH * 0.5), (float)HEIGHT);
            box.Size = new Vector3(0.06f, slope.Length(), (float)LENGTH);
            shape.Shape = box;
            shape.Position = new Vector3((float)(side * WIDTH * 0.25), (float)(HEIGHT * 0.5), 0.0f);
            Vector3 _t1 = shape.Rotation;
            // Each wall leans in from its ground line to the ridge, like the canvas.
            _t1.Z = (float)(side * atan2(slope.X, slope.Y));
            shape.Rotation = _t1;
            body.AddChild(shape);
        }
        AddChild(body);
    }
}
