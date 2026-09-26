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

/// Scripted camera sequences for recording with Godot's Movie Maker mode:
///   godot --write-movie out.avi --fixed-fps 60 --fullscreen -- --cinematic=arrival
/// Each shot moves the camera along an eased spline, aims it along a second
/// one, and may set or time-lapse the hour, change the field of view, focus the
/// depth of field and feed the fire. Each shot defines its cuts or fades; a
/// sequence opens on a title card and quits after the end card. Rendering runs at Film quality with
/// supersampling: in movie mode every frame is written, whatever it costs.
public partial class Cinematic : Node
{
    public partial class Shot
    {
        public string label = "";
        /// One line naming the rendering features on screen, shown under the label.
        public string caption = "";
        public Godot.Collections.Array<Vector3> path = new Godot.Collections.Array<Vector3>();
        public Godot.Collections.Array<Vector3> look = new Godot.Collections.Array<Vector3>();
        public double duration = 8.0;
        public double fov = 50.0;
        public double fov_end = NAN;
        public double hour = NAN;
        public double hour_end = NAN;
        public bool absolute = false;
        public double fade_in = 0.7;
        public double fade_out = 0.7;
        public bool focus = false;
        public bool feed_fire = false;
        public bool skip_stone = false;
        public double feed_at = 0.0;
        public double fire = NAN;
        public long lantern_state = -1;
        public double lantern_hour = NAN;
        public bool clock = false;
        public bool interior = false;
        public double wind = NAN;
        public double wind_end = NAN;
        public double hold_start = 0.0;
        public double hold_end = 0.0;
        public double ramp_seconds = 0.0;
        public double rest_at = 0.5;
        public double rest_seconds = 0.0;
        public double stone_at = 1.4;
        public Vector3 stone_origin = Vector3.Inf;
        public Vector3 stone_direction = Vector3.Zero;
        public double fire_end = NAN;
        // Walking is opt-in: grounded eye height and distance-driven footsteps.
        // Close observations, floating and seated shots retain their authored pose.
        public bool walk = false;
        public double eye_height = 1.64;
        public StringName walk_surface = "dirt";
        public StringName wildlife_cue = "";
        public double exposure = 1.0;
        public double exposure_end = NAN;
        public bool continuous_in = false;
        public double focus_distance = NAN;
        public double focus_distance_end = NAN;
        public string weather = "";
        // Seconds, pitch in degrees, blend weight. Local framing can leave the
        // entry, composed hold and ending aim of the base spline untouched.
        public Godot.Collections.Array<Vector3> pitch_envelope = new Godot.Collections.Array<Vector3>();
        public double pitch_ramp_seconds = 0.2;
    }

    /// Internal render scale for 1080p movies. At `--movie-size` resolutions the
    /// frame is already large, so it renders native instead.
    public const double SUPERSAMPLE = 1.5;
    public const double TITLE_SECONDS = 5.0;
    /// Closing card plus a movie-style credit roll; the roll's length depends on
    /// the sequence (see end_seconds), the default covers the short studies.
    public const double END_SECONDS = 25.0;
    public const double CARD_SECONDS = 5.0;

    public Camera3D camera;
    public string sequence_name = "";

    public List<Cinematic.Shot> _shots = new List<Cinematic.Shot>();
    public double _max_seconds = 0.0;
    public double _end_seconds = END_SECONDS;
    public bool _roll_compact = false;
    public VBoxContainer _roll;
    public double _roll_height = 0.0;
    public SystemFont _serif;
    public double _total_elapsed = 0.0;
    public string _frames_dir = "";
    public long _frames_saved = 0;
    public Godot.Collections.Array<long> _save_tasks = new Godot.Collections.Array<long>();
    public const long SAVE_TASKS_IN_FLIGHT = 20;
    public long _index = -1;
    public double _elapsed = 0.0;
    public string _phase = "title";
    public bool _stone_cued = false;
    public bool _fire_cued = false;
    public bool _lanterns_cued = false;
    public double _shot_fire_start = 1.0;
    public long _walk_step = 0;
    public Spline _path;
    public Spline _look;
    public CanvasLayer _overlay;
    public ColorRect _black;
    public Label _title;
    public Label _subtitle;
    public Label _end_title;
    public Label _end_note;
    public long _credits_page = -1;
    public Label _clock;
    public Label _caption_title;
    public Label _caption;

    public override void _Ready()
    {
        // Water masks and planar reflections must read this frame's camera pose.
        ProcessPriority = -10;
        camera = new Camera3D();
        camera.Name = "CinematicCamera";
        camera.Near = 0.05f;
        camera.Far = 1600.0f;
        camera.CullMask = unchecked((uint)(0xFFFFF & ~(Pond.REFLECTION_LAYER | Pond.UNDERWATER_REFLECTION_LAYER)));
        AddChild(camera);
        _build_overlay();
    }

    public void _build_overlay()
    {
        _overlay = new CanvasLayer();
        _overlay.Layer = 30;
        AddChild(_overlay);
        _black = new ColorRect();
        _black.Color = Colors.Black;
        _black.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _black.MouseFilter = Control.MouseFilterEnum.Ignore;
        _overlay.AddChild(_black);
        _title = UiTheme.label("THE LAST CAMP", 64, new Color(0.93f, 0.89f, 0.79f));
        SystemFont serif = new SystemFont();
        serif.FontNames = new List<string>(new List<string> { "Noto Serif", "DejaVu Serif" }).ToArray();
        _title.AddThemeFontOverride("font", serif);
        _title.AddThemeConstantOverride("outline_size", 0);
        _place(_title, new Vector2(-600, -70), new Vector2(600, 30));
        _subtitle = UiTheme.label("A F T E R G L O W", 18, UiTheme.MUTED);
        _place(_subtitle, new Vector2(-600, 40), new Vector2(600, 80));
        _end_title = UiTheme.label("Stay a little longer.", 46, new Color(0.93f, 0.89f, 0.79f));
        _end_title.AddThemeFontOverride("font", serif);
        _place(_end_title, new Vector2(-600, -60), new Vector2(600, 20));
        _end_note = UiTheme.label("THE LAST CAMP\nCaptured in Godot 4.8", 18, UiTheme.MUTED);
        _place(_end_note, new Vector2(-600, 30), new Vector2(600, 110));
        foreach (Label label in new[] { _title, _subtitle, _end_title, _end_note })
        {
            Color color = label.Modulate;
            color.A = 0.0f;
            label.Modulate = color;
        }
        _clock = UiTheme.label("", 23, new Color(0.87f, 0.86f, 0.79f, 0.85f));
        _clock.Position = new Vector2(58, 994);
        _clock.Visible = false;
        _overlay.AddChild(_clock);
        // Lower-third for each shot: its name and the features on screen.
        _caption_title = UiTheme.label("", 27, new Color(0.93f, 0.89f, 0.79f), HorizontalAlignment.Left);
        _caption_title.AddThemeFontOverride("font", serif);
        _caption_title.AddThemeConstantOverride("outline_size", 0);
        _caption_title.Position = new Vector2(58, 918);
        _caption_title.Visible = false;
        _overlay.AddChild(_caption_title);
        _caption = UiTheme.label("", 20, new Color(0.80f, 0.81f, 0.84f, 0.95f), HorizontalAlignment.Left);
        _caption.AddThemeConstantOverride("outline_size", 0);
        _caption.Position = new Vector2(58, 958);
        _caption.Visible = false;
        _overlay.AddChild(_caption);
    }

    public void _place(Control c, Vector2 top_left, Vector2 bottom_right)
    {
        c.AnchorLeft = 0.5f;
        c.AnchorRight = 0.5f;
        c.AnchorTop = 0.5f;
        c.AnchorBottom = 0.5f;
        c.OffsetLeft = top_left.X;
        c.OffsetTop = top_left.Y;
        c.OffsetRight = bottom_right.X;
        c.OffsetBottom = bottom_right.Y;
        if (c.GetParent() == null)
        {
            _black.AddChild(c);
        }
    }

    public void play(string name)
    {
        /// Starts a named sequence (see `sequence`). Forces Film quality + supersampling and
        /// a sharper, full-rate mirror, hides the HUD and takes the camera.
        sequence_name = name;
        _subtitle.Text = name == "afterglow" ? "A F T E R G L O W" : name.ToUpperInvariant();
        if (name == "one_night")
        {
            _subtitle.Text = "O N E  N I G H T";
            _end_note.Text = "Captured in Godot 4.8";
        }
        _shots = sequence(name);
        // Inspect selected complete shots at their real pace before a full export.
        // The delivered film omits this review-only argument.
        if (Game.Instance.has_flag("cinematic-shots"))
        {
            List<Cinematic.Shot> selected = new List<Cinematic.Shot>();
            foreach (string value in G.split(Game.Instance.arg_value("cinematic-shots", ""), ",", false))
            {
                if (!value.IsValidInt() || G.to_int(value) < 0 || G.to_int(value) >= (long)_shots.Count)
                {
                    G.push_error("cinematic-shots expects valid zero-based shot indices");
                    GetTree().Quit(1);
                    return;
                }
                selected.Add(_shots[(int)G.to_int(value)]);
            }
            _shots = selected;
        }
        if ((_shots.Count == 0))
        {
            G.push_error(G.format("Cinematic: unknown sequence '%s'", name));
            GetTree().Quit(1);
            return;
        }
        Quality.Instance.@explicit = true;
        Quality.Instance.apply(Quality.tier_from_name(Game.Instance.arg_value("capture-quality", "ultra")));
        bool large_frame = Game.Instance.has_flag("movie-size");
        if (!Game.Instance.has_flag("capture-quality"))
        {
            GetViewport().Scaling3DScale = (float)(large_frame ? 1.0 : SUPERSAMPLE);
        }
        if (!Game.Instance.has_flag("capture-quality") && Game.Instance.camp != null && Game.Instance.camp.pond != null)
        {
            Game.Instance.camp.pond.set_reflection_scale(large_frame ? 0.65 : 1.0);
        }
        _max_seconds = G.to_float(Game.Instance.arg_value("max-seconds", "0"));
        _end_seconds = end_seconds(name);
        _roll_compact = name != "one_night";
        if (Game.Instance.has_flag("credits-only"))
        {
            _shots.Clear();
        }
        // `--frames-dir=DIR` writes every rendered frame of the root viewport as a
        // JPEG, at the viewport's own size (so 4K with --movie-size even though the
        // window cannot be), to be muxed with the movie writer's audio track.
        _frames_dir = Game.Instance.arg_value("frames-dir", "");
        if (_frames_dir != "")
        {
            DirAccess.MakeDirRecursiveAbsolute(_frames_dir);
            new Signal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw).Owner.Connect(new Signal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw).Name, new Callable(this, Cinematic.MethodName._save_frame));
        }
        // The cards are laid out in 1080p pixels; scale the overlay to the frame,
        // with the black backing sized in pre-scale units so its centre (where the
        // labels anchor) stays the frame's centre.
        Vector2 frame_size = GetViewport().GetVisibleRect().Size;
        double overlay_scale = frame_size.Y / 1080.0;
        _overlay.Scale = Vector2.One * (float)overlay_scale;
        _black.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _black.Position = Vector2.Zero;
        _black.Size = frame_size / (float)overlay_scale;
        Game.Instance.hud_visible = false;
        Game.Instance.mode = Game.Mode.PHOTO;
        // The unattended player must not leave a stationary trample disc in the
        // meadow while this camera moves through it.
        if (Game.Instance.player != null)
        {
            Game.Instance.player.SetPhysicsProcess(false);
            Game.Instance.player.GlobalPosition = new Vector3(10000, 10, 10000);
        }
        camera.MakeCurrent();
        if (Game.Instance.world != null)
        {
            camera.Attributes = Game.Instance.world.attributes;
        }
        _phase = "title";
        _elapsed = 0.0;
        _index = -1;
        // Resolve the opening view under the card so the mirror, shadows and GI
        // have the full title duration to settle before the first image appears.
        if (!(_shots.Count == 0))
        {
            Cinematic.Shot first = _shots[0];
            camera.GlobalPosition = _resolve(first.path, first.absolute)[0];
            camera.LookAt(_resolve(first.look, first.absolute)[0], Vector3.Up);
            camera.Fov = (float)first.fov;
            if (Game.Instance.world != null && !is_nan(first.hour))
            {
                Game.Instance.world.hour = first.hour;
            }
            if (first.lantern_state >= 0)
            {
                _set_lanterns(first.lantern_state == 1);
            }
        }
        if (Game.Instance.camp.wildlife != null)
        {
            Game.Instance.camp.wildlife.set_film_cue("", 0.0);
        }
        if (Game.Instance.audio != null)
        {
            Game.Instance.audio.begin_film();
        }
        G.print(G.format("CINEMATIC_START sequence=%s frames_drawn=%d shots=%d", new Godot.Collections.Array { name, Engine.GetFramesDrawn(), (long)_shots.Count }));
        if (Game.Instance.has_flag("skip-cards"))
        {
            _next_shot();
        }
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        _total_elapsed += delta;
        if (_max_seconds > 0.0 && _total_elapsed >= _max_seconds)
        {
            _finish();
            return;
        }
        switch (_phase)
        {
            case "title":
                Color _t1 = _black.Color;
                _t1.A = 1.0f;
                _black.Color = _t1;
                double a = smoothstep(0.0, 0.6, _elapsed) * (1.0 - smoothstep(TITLE_SECONDS - 0.6, TITLE_SECONDS, _elapsed));
                Color _t2 = _title.Modulate;
                _t2.A = (float)a;
                _title.Modulate = _t2;
                Color _t3 = _subtitle.Modulate;
                _t3.A = (float)a;
                _subtitle.Modulate = _t3;
                if (_elapsed >= TITLE_SECONDS)
                {
                    _next_shot();
                }
                break;
            case "shot":
                _update_shot();
                break;
            case "end":
                Color _t4 = _black.Color;
                _t4.A = 1.0f;
                _black.Color = _t4;
                _update_credits();
                if (Game.Instance.audio != null)
                {
                    // The ambience settles under the roll and leaves with the last card.
                    double bed = 1.0 - 0.65 * smoothstep(0.2, 9.0, _elapsed);
                    Game.Instance.audio.film_fade(bed * (1.0 - smoothstep(_end_seconds - 4.0, _end_seconds - 0.4, _elapsed)));
                }
                if (_elapsed >= _end_seconds)
                {
                    _finish();
                }
                break;
        }
    }

    public void _update_credits()
    {
        if (_elapsed < CARD_SECONDS)
        {
            double alpha = smoothstep(0.0, 0.6, _elapsed) * (1.0 - smoothstep(CARD_SECONDS - 0.6, CARD_SECONDS, _elapsed));
            Color _t1 = _end_title.Modulate;
            _t1.A = (float)alpha;
            _end_title.Modulate = _t1;
            Color _t2 = _end_note.Modulate;
            _t2.A = (float)alpha;
            _end_note.Modulate = _t2;
            return;
        }
        Color _t3 = _end_title.Modulate;
        _t3.A = 0.0f;
        _end_title.Modulate = _t3;
        Color _t4 = _end_note.Modulate;
        _t4.A = 0.0f;
        _end_note.Modulate = _t4;
        if (_roll == null)
        {
            _build_credit_roll(_roll_compact);
        }
        // Constant speed from below the frame to fully above it, finishing a
        // breath before the film ends.
        double travel = _roll_height + 1080.0 + 60.0;
        double seconds = maxf(_end_seconds - CARD_SECONDS - 1.5, 1.0);
        Vector2 _t5 = _roll.Position;
        _t5.Y = (float)(1080.0 - (_elapsed - CARD_SECONDS) * travel / seconds);
        _roll.Position = _t5;
    }

    public void _build_credit_roll(bool compact)
    {
        /// Movie-style credits built from the three source tables so every creator
        /// and licence is on screen; the film is posted without its sidecar files.
        _roll = new VBoxContainer();
        _roll.MouseFilter = Control.MouseFilterEnum.Ignore;
        _roll.AddThemeConstantOverride("separation", 6);
        _roll.CustomMinimumSize = new Vector2(1500.0f, 0.0f);
        _roll.Position = new Vector2(210.0f, 1080.0f);
        _black.AddChild(_roll);
        _roll_line("THE LAST CAMP", 54, new Color(0.93f, 0.89f, 0.79f), true);
        _roll_line("A rendering tech demo made with Godot 4.8", 24, UiTheme.MUTED);
        _roll_gap(70);
        _roll_header("Created by");
        _roll_line("David Prentice", 30, UiTheme.TEXT);
        _roll_gap(50);
        _roll_header("Rendered in");
        _roll_line("Godot Engine 4.8  ·  Forward+  ·  Vulkan", 24, UiTheme.TEXT);
        _roll_line("Movie Maker mode, 1920x1080 at 60 frames per second", 22, UiTheme.MUTED);
        _roll_gap(50);
        _roll_header("Everything grown by the project");
        _roll_line("Terrain, trees, understory, water, sky, weather, camp and wildlife", 22, UiTheme.MUTED);
        _roll_gap(50);
        _roll_header("Surfaces");
        _roll_line("Photoscanned by Poly Haven  ·  CC0 1.0", 22, UiTheme.MUTED);
        Godot.Collections.Array surfaces = _credit_rows("res://textures/SOURCES.md", 1, 2, 4);
        if ((surfaces.Count == 0))
        {
            surfaces = new Godot.Collections.Array { new Godot.Collections.Array { "Leafy Grass, Jolcham Oak Bark 01", "Charlotte Baglioni" }, new Godot.Collections.Array { "Forest Leaves 02, Rough Wood", "Rob Tuytel" }, new Godot.Collections.Array { "Mud Forest, Stony Dirt Path", "eye-candy.xyz" }, new Godot.Collections.Array { "Lichen Rock", "Rico Cilliers" }, new Godot.Collections.Array { "Pine Bark", "Dimitrios Savva" }, new Godot.Collections.Array { "Fabric 061", "ambientCG  ·  CC0 1.0" } };
        }
        foreach (Variant row in surfaces)
        {
            _roll_pair(G.Index(row, 0).AsString(), G.Index(row, 1).AsString());
        }
        _roll_gap(50);
        _roll_header("Photoscanned models");
        _roll_line("Poly Haven  ·  CC0 1.0", 22, UiTheme.MUTED);
        Godot.Collections.Array models = _credit_rows("res://models/SOURCES.md", 1, 2, 4);
        if ((models.Count == 0))
        {
            models = new Godot.Collections.Array { new Godot.Collections.Array { "Stumps, trunks, roots, rocks, plants, saplings and camp props", "Rico Cilliers, Rob Tuytel, Jenelle van Heerden, James Ray Cock, Kless Gyzen, Kuutti Siitonen, Dario Barresi, Ulan Cabanilla, Alex Weber" } };
        }
        if (compact)
        {
            long i = 0;
            while (i < (long)models.Count)
            {
                string left = G.op("+", G.op("+", G.Index(models[(int)i], 0), "  ·  "), G.Index(models[(int)i], 1)).AsString();
                string right = i + 1 < (long)models.Count ? G.op("+", G.op("+", G.Index(models[(int)(i + 1)], 0), "  ·  "), G.Index(models[(int)(i + 1)], 1)).AsString() : "";
                _roll_pair(left, right, 20);
                i += 2;
            }
        }
        else
        {
            foreach (Variant row2 in models)
            {
                _roll_pair(G.Index(row2, 0).AsString(), G.Index(row2, 1).AsString());
            }
        }
        _roll_gap(50);
        _roll_header("Sound");
        Godot.Collections.Array sounds = _audio_credit_rows();
        if ((sounds.Count == 0))
        {
            sounds = new Godot.Collections.Array { new Godot.Collections.Array { "Water Splash", "Mike Koenig  ·  SoundBible  ·  CC BY 3.0" }, new Godot.Collections.Array { "Water Churning, Thunder HD", "Mark DiAngelo  ·  SoundBible  ·  CC BY 3.0" }, new Godot.Collections.Array { "Rain", "Ylmir  ·  OpenGameArt  ·  CC0 1.0" }, new Godot.Collections.Array { "Fireplace", "PagDev  ·  OpenGameArt  ·  CC0 1.0" }, new Godot.Collections.Array { "Trees in Wind", "naturenotesuk  ·  Freesound  ·  CC0 1.0" } };
        }
        foreach (Variant row3 in sounds)
        {
            _roll_pair(G.Index(row3, 0).AsString(), G.Index(row3, 1).AsString());
        }
        _roll_line("Recordings edited and mixed for the film", 20, UiTheme.MUTED);
        _roll_line("All other sound generated by the project  ·  no music", 20, UiTheme.MUTED);
        _roll_gap(80);
        _roll_line("Thank you for watching", 34, new Color(0.93f, 0.89f, 0.79f), true);
        _roll_gap(40);
        _roll_line("github.com/dlprentice/the-last-camp", 20, UiTheme.MUTED);
        _roll_height = _roll.GetCombinedMinimumSize().Y;
    }

    public SystemFont _roll_font()
    {
        if (_serif == null)
        {
            _serif = new SystemFont();
            _serif.FontNames = new List<string>(new List<string> { "Noto Serif", "DejaVu Serif" }).ToArray();
        }
        return _serif;
    }

    public void _roll_line(string text, long size, Color color, bool serif = false)
    {
        Label l = UiTheme.label(text, size, color);
        if (serif)
        {
            l.AddThemeFontOverride("font", _roll_font());
        }
        l.AddThemeConstantOverride("outline_size", 0);
        _roll.AddChild(l);
    }

    public void _roll_header(string text)
    {
        _roll_gap(10);
        Label l = UiTheme.label(text.ToUpperInvariant(), 22, UiTheme.ACCENT);
        l.AddThemeFontOverride("font", _roll_font());
        l.AddThemeConstantOverride("outline_size", 0);
        _roll.AddChild(l);
        _roll_gap(4);
    }

    public void _roll_pair(string left, string right, long size = 24)
    {
        HBoxContainer row = new HBoxContainer();
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddThemeConstantOverride("separation", 40);
        Label a = UiTheme.label(left, size, UiTheme.MUTED, HorizontalAlignment.Right);
        a.CustomMinimumSize = new Vector2(730.0f, 0.0f);
        a.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Label b = UiTheme.label(right, size, UiTheme.TEXT, HorizontalAlignment.Left);
        b.CustomMinimumSize = new Vector2(730.0f, 0.0f);
        b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        foreach (Variant l in new Godot.Collections.Array { a, b })
        {
            G.Call(l, "add_theme_constant_override", "outline_size", 0);
            row.AddChild(l.As<Node>());
        }
        _roll.AddChild(row);
    }

    public void _roll_gap(long pixels)
    {
        Control spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(0.0f, (float)(double)pixels);
        spacer.MouseFilter = Control.MouseFilterEnum.Ignore;
        _roll.AddChild(spacer);
    }

    public static Godot.Collections.Array _credit_rows(string path, long name_column, long creator_column, long licence_column)
    {
        /// Rows of a markdown table as [name, creator (with a licence note when it is
        /// not CC0)] from the given column indices; empty when the file is absent.
        Godot.Collections.Array rows = new Godot.Collections.Array();
        foreach (Variant cells in _table_cells(path))
        {
            if (G.op("<=", G.Call(cells, "size"), maxi(maxi(name_column, creator_column), licence_column)).AsBool())
            {
                continue;
            }
            string name = _link_text(G.Index(cells, name_column).AsString());
            if ((long)name.IndexOf("_", StringComparison.Ordinal) != -1 || name == name.ToLowerInvariant())
            {
                name = name.Replace("_", " ").Capitalize();
            }
            string creator = _strip_parentheticals(_link_text(G.Index(cells, creator_column).AsString()));
            string licence = _link_text(G.Index(cells, licence_column).AsString());
            if (!licence.StartsWith("CC0", StringComparison.Ordinal))
            {
                creator += "  ·  " + licence;
            }
            rows.Add(new Godot.Collections.Array { name, creator });
        }
        return rows;
    }

    public static Godot.Collections.Array _audio_credit_rows()
    {
        Godot.Collections.Array rows = new Godot.Collections.Array();
        Godot.Collections.Dictionary seen = new Godot.Collections.Dictionary();
        foreach (Variant cells in _table_cells("res://audio/SOURCES.md"))
        {
            if (G.op("<", G.Call(cells, "size"), 3).AsBool())
            {
                continue;
            }
            string entry = _link_text(G.Index(cells, 1).AsString());
            string url = _link_url(G.Index(cells, 1).AsString());
            List<string> parts = G.split(entry, " — ", false);
            if ((long)parts.Count < 2)
            {
                continue;
            }
            string title = _strip_parentheticals(parts[0]);
            if (seen.ContainsKey(title))
            {
                continue;
            }
            seen[title] = true;
            string provider = (long)url.IndexOf("soundbible", StringComparison.Ordinal) != -1 ? "SoundBible" : (long)url.IndexOf("opengameart", StringComparison.Ordinal) != -1 ? "OpenGameArt" : (long)url.IndexOf("freesound", StringComparison.Ordinal) != -1 ? "Freesound" : "";
            string licence = _link_text(G.Index(cells, 2).AsString()).Replace("CC0", "CC0 1.0").Replace("CC0 1.0 1.0", "CC0 1.0");
            // "Mike Koenig" or "Ylmir, take 3": the take note belongs to the sidecar.
            string credit = G.split(parts[1], ",", false)[0].StripEdges();
            if (provider != "")
            {
                credit += "  ·  " + provider;
            }
            credit += "  ·  " + licence;
            rows.Add(new Godot.Collections.Array { title, credit });
        }
        return rows;
    }

    public static Godot.Collections.Array _table_cells(string path)
    {
        Godot.Collections.Array @out = new Godot.Collections.Array();
        if (!FileAccess.FileExists(path))
        {
            return @out;
        }
        string text = FileAccess.GetFileAsString(path);
        bool header_seen = false;
        foreach (string line in G.split(text, "\n"))
        {
            string trimmed = line.StripEdges();
            if (!trimmed.StartsWith("|", StringComparison.Ordinal))
            {
                continue;
            }
            if (trimmed.StartsWith("|---", StringComparison.Ordinal) || trimmed.StartsWith("| ---", StringComparison.Ordinal))
            {
                header_seen = true;
                continue;
            }
            if (!header_seen)
            {
                continue;
            }
            Godot.Collections.Array cells = new Godot.Collections.Array();
            foreach (string cell in G.split(trimmed.TrimPrefix("|").TrimSuffix("|"), "|"))
            {
                cells.Add((cell).StripEdges());
            }
            @out.Add(cells);
        }
        return @out;
    }

    public static string _link_text(string cell)
    {
        RegEx regex = new RegEx();
        regex.Compile("\\[([^\\]]+)\\]\\([^)]*\\)");
        return regex.Sub(cell, "$1", true).Replace("`", "").StripEdges();
    }

    public static string _link_url(string cell)
    {
        RegEx regex = new RegEx();
        regex.Compile("\\]\\(([^)]*)\\)");
        RegExMatch m = regex.Search(cell);
        return m != null ? m.GetString(1) : "";
    }

    public static string _strip_parentheticals(string text)
    {
        RegEx regex = new RegEx();
        regex.Compile("\\s*\\([^)]*\\)");
        return regex.Sub(text, "", true).StripEdges();
    }

    public void _finish()
    {
        SetProcess(false);
        G.print(G.format("CINEMATIC_DONE frames_drawn=%d frames_saved=%d viewport=%s", new Godot.Collections.Array { Engine.GetFramesDrawn(), _frames_saved, GetViewport().GetVisibleRect().Size }));
        if (_frames_dir != "")
        {
            new Signal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw).Owner.Disconnect(new Signal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw).Name, new Callable(this, Cinematic.MethodName._save_frame));
            foreach (long task in _save_tasks)
            {
                WorkerThreadPool.WaitForTaskCompletion(task);
            }
            _save_tasks.Clear();
        }
        Game.Instance.quit_cleanly();
    }

    public void _save_frame()
    {
        /// One JPEG per drawn frame, numbered from the first frame after play(). The
        /// encode runs on the worker pool (the image is this frame's own copy), a few
        /// frames deep, so the GPU is not waiting on the JPEG writer.
        Image image = GetViewport().GetTexture().GetImage();
        if (image == null)
        {
            return;
        }
        string path = G.format("%s/%06d.jpg", new Godot.Collections.Array { _frames_dir, _frames_saved });
        _frames_saved += 1;
        _save_tasks.Add(WorkerThreadPool.AddTask(Callable.From(() => image.SaveJpg(path, 0.95f)), false, "frame jpeg"));
        while ((long)_save_tasks.Count > SAVE_TASKS_IN_FLIGHT)
        {
            WorkerThreadPool.WaitForTaskCompletion(G.pop_front(_save_tasks));
        }
    }

    public void _next_shot()
    {
        double previous_duration = _index < 0 ? TITLE_SECONDS : _shots[(int)_index].duration;
        double remainder = maxf(_elapsed - previous_duration, 0.0);
        _index += 1;
        _elapsed = remainder;
        if (_index >= (long)_shots.Count)
        {
            if (Game.Instance.has_flag("skip-cards"))
            {
                _finish();
                return;
            }
            _phase = "end";
            // The world is fully covered during credits. Keep the UI drawing, but
            // avoid rendering the hidden supersampled forest for another 25 seconds.
            GetViewport().Disable3D = true;
            Game.Instance.world.exposure_scale = 1.0;
            Color _t1 = _black.Color;
            _t1.A = 1.0f;
            _black.Color = _t1;
            _clock.Visible = false;
            _caption_title.Visible = false;
            _caption.Visible = false;
            return;
        }
        _phase = "shot";
        _stone_cued = false;
        _fire_cued = false;
        _lanterns_cued = false;
        _walk_step = 0;
        Color _t2 = _title.Modulate;
        _t2.A = 0.0f;
        _title.Modulate = _t2;
        Color _t3 = _subtitle.Modulate;
        _t3.A = 0.0f;
        _subtitle.Modulate = _t3;
        Cinematic.Shot shot = _shots[(int)_index];
        G.print(G.format("CINEMATIC_SHOT index=%d name=%s time=%.3f", new Godot.Collections.Array { _index, shot.label, _total_elapsed }));
        _path = new Spline(_resolve(shot.path, shot.absolute));
        _look = new Spline(_resolve(shot.look, shot.absolute));
        if (!is_nan(shot.hour) && Game.Instance.world != null)
        {
            Game.Instance.world.hour = shot.hour;
        }
        if (!is_nan(shot.fire))
        {
            Game.Instance.camp.campsite.firepit.intensity = shot.fire;
        }
        _shot_fire_start = Game.Instance.camp.campsite.firepit.intensity;
        if (shot.lantern_state >= 0)
        {
            _set_lanterns(shot.lantern_state == 1);
        }
        _update_shot();
    }

    public void _update_shot()
    {
        Cinematic.Shot shot = _shots[(int)_index];
        if (shot.feed_fire && !_fire_cued && _elapsed >= shot.feed_at)
        {
            _fire_cued = true;
            Game.Instance.camp.campsite.firepit.feed();
            if (Game.Instance.audio != null)
            {
                Game.Instance.audio.play_interact("log_added");
            }
        }
        if (shot.skip_stone && !_stone_cued && _elapsed >= shot.stone_at)
        {
            _stone_cued = true;
            Dock dock = Game.Instance.camp.campsite.dock;
            Vector3 origin = dock.ToGlobal(new Vector3(0.0f, 0.9f, (float)(-dock.total_length() - 0.2)));
            Game.Instance.camp.pond.skip_stone(shot.stone_origin.IsFinite() ? shot.stone_origin : origin, shot.stone_direction.LengthSquared() > 0.0 ? shot.stone_direction : -dock.GlobalBasis.Z);
        }
        double t = clampf(_elapsed / shot.duration, 0.0, 1.0);
        double u = motion_progress(shot, _elapsed);
        Vector3 pos = camera_position(shot, _path, Game.Instance.camp.field, _elapsed);
        Vector3 target = aim_target(shot, pos, _look.sample(u), _elapsed);
        camera.GlobalPosition = pos;
        if (pos.DistanceTo(target) > 1e-3)
        {
            camera.LookAt(target, Vector3.Up);
        }
        camera.Fov = (float)lerpf(shot.fov, !is_nan(shot.fov_end) ? shot.fov_end : shot.fov, u);
        if (shot.walk)
        {
            long step = floori(_path.total_length() * u / WALK_STEP_LENGTH);
            if (step > _walk_step && Game.Instance.audio != null)
            {
                Game.Instance.audio.footstep(on_dock(pos) ? "wood" : shot.walk_surface, false, -6.0);
            }
            _walk_step = step;
        }
        Game.Instance.world.weather.apply_chapter(shot.weather, _elapsed, shot.duration);
        if (Game.Instance.camp.wildlife != null)
        {
            Game.Instance.camp.wildlife.set_film_cue(shot.wildlife_cue, _elapsed);
        }
        if (!is_nan(shot.hour_end) && Game.Instance.world != null)
        {
            Game.Instance.world.hour = lerpf(shot.hour, shot.hour_end, t);
        }
        Game.Instance.world.exposure_scale = lerpf(shot.exposure, !is_nan(shot.exposure_end) ? shot.exposure_end : shot.exposure, t);
        if (!is_nan(shot.fire_end))
        {
            Game.Instance.camp.campsite.firepit.intensity = lerpf(_shot_fire_start, shot.fire_end, smoothstep(0.0, 1.0, t));
        }
        if (!is_nan(shot.wind))
        {
            Game.Instance.world.wind_scale = lerpf(shot.wind, !is_nan(shot.wind_end) ? shot.wind_end : shot.wind, t);
        }
        if (!is_nan(shot.lantern_hour) && !_lanterns_cued && Game.Instance.world.hour >= shot.lantern_hour)
        {
            _lanterns_cued = true;
            _set_lanterns(true);
        }
        _clock.Visible = shot.clock;
        if (shot.clock)
        {
            _clock.Text = clock_text(Game.Instance.world.hour);
        }
        bool captioned = shot.caption != "" && !Game.Instance.has_flag("no-captions");
        _caption_title.Visible = captioned;
        _caption.Visible = captioned;
        if (captioned)
        {
            _caption_title.Text = shot.label;
            _caption.Text = shot.caption;
            double caption_alpha = smoothstep(shot.fade_in + 0.4, shot.fade_in + 1.4, _elapsed) * (1.0 - smoothstep(shot.duration - shot.fade_out - 1.6, shot.duration - shot.fade_out - 0.5, _elapsed));
            Color _t1 = _caption_title.Modulate;
            _t1.A = (float)caption_alpha;
            _caption_title.Modulate = _t1;
            Color _t2 = _caption.Modulate;
            _t2.A = (float)caption_alpha;
            _caption.Modulate = _t2;
        }
        double focus_distance = pos.DistanceTo(target);
        if (!is_nan(shot.focus_distance))
        {
            focus_distance = lerpf(shot.focus_distance, !is_nan(shot.focus_distance_end) ? shot.focus_distance_end : shot.focus_distance, smoothstep(0.25, 0.75, t));
        }
        apply_focus(shot.focus, focus_distance, !is_nan(shot.focus_distance));
        double fade = maxf(shot.fade_in > 0 ? 1.0 - _elapsed / shot.fade_in : 0.0, shot.fade_out > 0 ? (_elapsed - (shot.duration - shot.fade_out)) / shot.fade_out : 0.0);
        Color _t3 = _black.Color;
        _t3.A = (float)clampf(fade, 0.0, 1.0);
        _black.Color = _t3;
        if (_elapsed >= shot.duration)
        {
            _next_shot();
        }
    }

    public static string clock_text(double hour)
    {
        long minutes = floori(fposmod(hour, 24.0) * 60.0 + 0.00001) % 1440;
        return G.format("%02d:%02d", new Godot.Collections.Array { minutes / 60, minutes % 60 });
    }

    public const double WALK_STEP_LENGTH = 0.64;

    public static Vector3 camera_position(Cinematic.Shot shot, Spline path, TerrainField field, double seconds)
    {
        /// Re-evaluate the ground beneath each frame rather than interpolating only
        /// the control-point heights. Small, speed-weighted gait stops with the walker;
        /// its phase also drives the footfalls, so no bob or footsteps continue at rest.
        double u = motion_progress(shot, seconds);
        Vector3 pos = path.sample(u);
        if (!shot.walk)
        {
            return pos;
        }
        if (!shot.absolute)
        {
            pos.Y = (float)(walking_support(field, pos) + shot.eye_height);
        }
        double before = motion_progress(shot, seconds - 1.0 / 120.0);
        double after = motion_progress(shot, seconds + 1.0 / 120.0);
        double speed = (after - before) * path.total_length() * 60.0;
        double strength = smoothstep(0.02, 0.75, speed);
        double phase = u * path.total_length() / WALK_STEP_LENGTH * TAU;
        Vector3 forward = path.sample(minf(u + 0.001, 1.0)) - path.sample(maxf(u - 0.001, 0.0));
        Vector3 side = forward.Cross(Vector3.Up).Normalized();
        return pos + Vector3.Up * (float)(-cos(phase) * 0.012 * strength) + side * (float)(sin(phase * 0.5) * 0.009 * strength);
    }

    public static bool on_dock(Vector3 pos)
    {
        Vector2 delta = new Vector2(pos.X, pos.Z) - TerrainField.DOCK_START;
        Vector2 forward = (TerrainField.POND_CENTRE - TerrainField.DOCK_START).Normalized();
        double along = delta.Dot(forward);
        return along >= -Dock.INLAND && along <= Dock.LENGTH && absf(delta.Cross(forward)) < Dock.WIDTH * 0.5;
    }

    public static double walking_support(TerrainField field, Vector3 pos)
    {
        double ground = field.height(pos.X, pos.Z);
        Vector2 delta = new Vector2(pos.X, pos.Z) - TerrainField.DOCK_START;
        Vector2 forward = (TerrainField.POND_CENTRE - TerrainField.DOCK_START).Normalized();
        double along = delta.Dot(forward);
        if (along > Dock.LENGTH + 0.25)
        {
            return ground;
        }
        // Anticipate the low threshold with a smooth step rather than snapping
        // eye height when a foot first crosses the inland edge of the boards.
        double board = TerrainField.WATER_LEVEL + Dock.DECK_ABOVE_WATER;
        double edge = 1.0 - smoothstep(Dock.WIDTH * 0.5 - 0.25, Dock.WIDTH * 0.5 + 0.25, absf(delta.Cross(forward)));
        edge *= 1.0 - smoothstep(Dock.LENGTH - 0.25, Dock.LENGTH + 0.25, along);
        return lerpf(ground, maxf(ground, board), edge * smoothstep(-Dock.INLAND - 0.35, -Dock.INLAND + 0.25, along));
    }

    public static Vector3 aim_target(Cinematic.Shot shot, Vector3 pos, Vector3 base_target, double seconds)
    {
        Godot.Collections.Array<Vector3> keys = shot.pitch_envelope;
        if ((long)keys.Count < 2 || seconds < G.front(keys).X || seconds > G.back(keys).X)
        {
            return base_target;
        }
        for (long i = 1, i_end = (long)keys.Count; i < i_end; i++)
        {
            if (seconds > keys[(int)i].X)
            {
                continue;
            }
            Vector3 a = keys[(int)(i - 1)];
            Vector3 b = keys[(int)i];
            double t = _travel_progress(seconds - a.X, (double)b.X - a.X, shot.pitch_ramp_seconds);
            Vector3 ray = base_target - pos;
            Vector3 flat = new Vector3(ray.X, 0.0f, ray.Z);
            double pitch = lerpf(atan2(ray.Y, flat.Length()), deg_to_rad(lerpf(a.Y, b.Y, t)), lerpf(a.Z, b.Z, t));
            return pos + (flat.Normalized() * (float)cos(pitch) + Vector3.Up * (float)sin(pitch)) * ray.Length();
        }
        return base_target;
    }

    public static double motion_progress(Cinematic.Shot shot, double seconds)
    {
        /// Held compositions flank a slow move. A cosine velocity ramp reaches a
        /// constant cruise without the mid-shot speed surge of whole-shot easing.
        if (shot.ramp_seconds <= 0.0)
        {
            double t = clampf(seconds / shot.duration, 0.0, 1.0);
            return lerpf(t, smoothstep(0.0, 1.0, t), 0.75);
        }
        double travel = maxf(shot.duration - shot.hold_start - shot.hold_end, 0.01);
        double t2 = clampf(seconds - shot.hold_start, 0.0, travel);
        if (shot.rest_seconds > 0.0)
        {
            double moving = maxf(travel - shot.rest_seconds, 0.01);
            double arrival = moving * shot.rest_at;
            if (t2 < arrival)
            {
                return shot.rest_at * _travel_progress(t2, arrival, shot.ramp_seconds);
            }
            if (t2 < arrival + shot.rest_seconds)
            {
                return shot.rest_at;
            }
            return shot.rest_at + (1.0 - shot.rest_at) * _travel_progress(t2 - arrival - shot.rest_seconds, moving - arrival, shot.ramp_seconds);
        }
        return _travel_progress(t2, travel, shot.ramp_seconds);
    }

    public static double _travel_progress(double seconds, double travel, double ramp_seconds)
    {
        double t = clampf(seconds, 0.0, travel);
        double ramp = minf(ramp_seconds, travel * 0.5);
        double distance = travel - ramp;
        if (t < ramp)
        {
            return 0.5 * (t - ramp / PI * sin(PI * t / ramp)) / distance;
        }
        if (t > travel - ramp)
        {
            double remaining = travel - t;
            return 1.0 - 0.5 * (remaining - ramp / PI * sin(PI * remaining / ramp)) / distance;
        }
        return (t - ramp * 0.5) / distance;
    }

    public static void _set_lanterns(bool lit)
    {
        Campsite site = Game.Instance.camp.campsite;
        foreach (CampLantern lantern in site.lanterns)
        {
            lantern.set_lit(lit);
        }
        site.tent.lantern.set_lit(lit);
        site.dock.lantern.set_lit(lit);
    }

    public static void apply_focus(bool focus, double distance, bool tight = false)
    {
        if (Game.Instance.world == null)
        {
            return;
        }
        CameraAttributesPractical a = Game.Instance.world.attributes;
        a.DofBlurFarEnabled = focus;
        a.DofBlurNearEnabled = focus;
        if (focus)
        {
            a.DofBlurFarDistance = (float)(tight ? distance + 0.15 : distance * 1.6);
            a.DofBlurFarTransition = (float)(tight ? 0.65 : distance * 3.0);
            a.DofBlurNearDistance = (float)(tight ? maxf(0.01, distance - 0.15) : distance * 0.45);
            a.DofBlurNearTransition = (float)(tight ? 0.65 : distance * 0.4);
        }
        a.DofBlurAmount = (float)(tight ? 0.10 : 0.045);
    }

    public static Godot.Collections.Array<Vector3> _resolve(Godot.Collections.Array<Vector3> points, bool absolute)
    {
        /// Ground-relative points (y above the ground or water under them) become
        /// absolute world positions; absolute shots pass through untouched.
        return resolve(Game.Instance.camp.field, points, absolute);
    }

    public static Godot.Collections.Array<Vector3> resolve(TerrainField field, Godot.Collections.Array<Vector3> points, bool absolute)
    {
        /// Relative shot points are metres above the ground (or the water, whichever is
        /// higher); absolute ones are world space.
        if (absolute)
        {
            return points.Duplicate();
        }
        Godot.Collections.Array<Vector3> @out = new Godot.Collections.Array<Vector3>();
        foreach (Vector3 p in points)
        {
            double ground = maxf(field.height(p.X, p.Z), TerrainField.WATER_LEVEL);
            @out.Add(new Vector3(p.X, (float)(ground + p.Y), p.Z));
        }
        return @out;
    }

    public static List<string> obstructions(TerrainField field, ScenePlan plan, string sequence_name, Godot.Collections.Array<Vector3> piles, Vector3 canoe_position, double canoe_yaw, long samples = 48)
    {
        /// Samples every shot of `sequence_name` and lists the points where the lens
        /// would sit inside the ground or the pond bed, a tree trunk or crown, a dock
        /// pile, the moored canoe, the tent or the fire tripod. Empty when the shots
        /// are clear; the camp test keeps it that way.
        List<string> @out = new List<string>();
        Vector2 canoe_axis = new Vector2((float)sin(canoe_yaw), (float)cos(canoe_yaw)) * (float)Canoe.LENGTH * 0.5f;
        Vector2 canoe_xz = new Vector2(canoe_position.X, canoe_position.Z);
        List<Cinematic.Shot> shots = sequence(sequence_name);
        for (long si = 0, si_end = (long)shots.Count; si < si_end; si++)
        {
            Cinematic.Shot shot = shots[(int)si];
            Spline path = new Spline(resolve(field, shot.path, shot.absolute));
            for (long i = 0; i < samples; i++)
            {
                Vector3 p = camera_position(shot, path, field, shot.duration * (double)i / (double)(samples - 1));
                Vector2 xz = new Vector2(p.X, p.Z);
                double ground = field.height(p.X, p.Z);
                if (p.Y < ground + 0.25)
                {
                    @out.Add(G.format("shot %d: ground %.2f under lens at %s", new Godot.Collections.Array { si, ground, p }));
                }
                foreach (Vector3 pile in piles)
                {
                    if (p.Y < pile.Y + 0.3 && xz.DistanceTo(new Vector2(pile.X, pile.Z)) < 0.45)
                    {
                        @out.Add(G.format("shot %d: pile %s at lens %s", new Godot.Collections.Array { si, pile, p }));
                    }
                }
                if (p.Y > TerrainField.WATER_LEVEL - 0.7 && p.Y < TerrainField.WATER_LEVEL + 0.8)
                {
                    double d = _segment_distance(xz, canoe_xz - canoe_axis, canoe_xz + canoe_axis);
                    if (d < Canoe.HALF_BEAM + 0.35)
                    {
                        @out.Add(G.format("shot %d: canoe %.2f m from lens at %s", new Godot.Collections.Array { si, d, p }));
                    }
                }
                if (xz.DistanceTo(TerrainField.TENT) < 2.0 && p.Y < ground + Tent.HEIGHT + 0.35)
                {
                    Vector3 local = tent_transform(field).AffineInverse() * p;
                    double roof_clearance = Tent.WIDTH * 0.5 * (1.0 - local.Y / Tent.HEIGHT) - absf(local.X);
                    if (!shot.interior || roof_clearance < 0.18 || absf(local.Z) > Tent.LENGTH * 0.5 - 0.25)
                    {
                        @out.Add(G.format("shot %d: tent at lens %s", new Godot.Collections.Array { si, p }));
                    }
                }
                if (xz.DistanceTo(TerrainField.FIRE) < 0.8 && p.Y < ground + 1.5)
                {
                    @out.Add(G.format("shot %d: fire tripod at lens %s", new Godot.Collections.Array { si, p }));
                }
                if (xz.DistanceTo(TerrainField.FIRE) < 2.0)
                {
                    Vector3 local2 = p - new Vector3(TerrainField.FIRE.X, (float)field.height(0, 0), TerrainField.FIRE.Y);
                    for (long leg_index = 0; leg_index < 3; leg_index++)
                    {
                        Godot.Collections.Array<Vector3> leg = Firepit.tripod_leg(leg_index);
                        Vector3 axis = leg[1] - leg[0];
                        double along = clampf((double)(local2 - leg[0]).Dot(axis) / axis.LengthSquared(), 0.0, 1.0);
                        if (local2.DistanceTo(leg[0] + axis * (float)along) < 0.2)
                        {
                            @out.Add(G.format("shot %d: tripod leg at lens %s", new Godot.Collections.Array { si, p }));
                        }
                    }
                }
                if (xz.DistanceTo(TerrainField.TABLE) < 1.15 && p.Y < ground + 1.55)
                {
                    @out.Add(G.format("shot %d: cooking table at lens %s", new Godot.Collections.Array { si, p }));
                }
                foreach (Vector2 seat in TerrainField.SEATS)
                {
                    Vector2 outward = (seat - TerrainField.FIRE).Normalized();
                    Vector2 tangent = new Vector2(outward.Y, -outward.X);
                    Vector2 offset = xz - seat;
                    if (absf(offset.Dot(tangent)) < 1.08 && absf(offset.Dot(outward)) < 0.36 && p.Y < ground + 0.80)
                    {
                        @out.Add(G.format("shot %d: bench at lens %s", new Godot.Collections.Array { si, p }));
                    }
                }
                foreach (ScenePlan.TreeEntry t in plan.near_trees())
                {
                    TreeSpecies species = TreeSpecies.by_kind(t.kind);
                    double @base = field.height(t.position.X, t.position.Y);
                    double top = @base + species.height.Y * t.scale + 1.0;
                    double crown_base = @base + species.branch_start * species.height.X * t.scale - 0.6;
                    double radius = ScenePlan.crown_footprint(t.kind) * t.scale + 0.5;
                    double dxz = xz.DistanceTo(t.position);
                    if (dxz < 1.0 && p.Y < top)
                    {
                        @out.Add(G.format("shot %d: trunk %s %s at lens %s", new Godot.Collections.Array { si, species.name, t.position, p }));
                    }
                    else if (dxz < radius && p.Y > crown_base && p.Y < top)
                    {
                        @out.Add(G.format("shot %d: crown %s %s at lens %s", new Godot.Collections.Array { si, species.name, t.position, p }));
                    }
                }
            }
        }
        return @out;
    }

    public static double _segment_distance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        double t = clampf((p - a).Dot(ab) / maxf(ab.LengthSquared(), 1e-6), 0.0, 1.0);
        return p.DistanceTo(a + ab * (float)t);
    }

    public static Cinematic.Shot _shot(Godot.Collections.Array<Vector3> path, Godot.Collections.Array<Vector3> look, double duration, double fov = 50.0, bool absolute = false)
    {
        // ------------------------------------------------------------------ authoring
        Cinematic.Shot s = new Cinematic.Shot();
        s.path = path;
        s.look = look;
        s.duration = duration;
        s.fov = fov;
        s.absolute = absolute;
        return s;
    }

    public static Godot.Collections.Array<Vector3> _orbit(Vector3 centre, double radius, double height, double from_deg, double to_deg, long steps = 9)
    {
        /// Camera positions on an arc around `centre` (ground-relative heights).
        Godot.Collections.Array<Vector3> @out = new Godot.Collections.Array<Vector3>();
        for (long i = 0; i < steps; i++)
        {
            double a = deg_to_rad(lerpf(from_deg, to_deg, (double)i / (double)(steps - 1)));
            @out.Add(new Vector3((float)(centre.X + cos(a) * radius), (float)height, (float)(centre.Z + sin(a) * radius)));
        }
        return @out;
    }

    public static Godot.Collections.Array<Vector3> _repeat(Vector3 p, long count = 2)
    {
        Godot.Collections.Array<Vector3> @out = new Godot.Collections.Array<Vector3>();
        for (long i = 0; i < count; i++)
        {
            @out.Add(p);
        }
        return @out;
    }

    public static List<Cinematic.Shot> arrival()
    {
        List<Cinematic.Shot> shots = new List<Cinematic.Shot>();
        Cinematic.Shot dolly = _shot(IntroDolly.PATH, IntroDolly.LOOK, 17.0, 38.0, true);
        dolly.fov_end = 64.0;
        dolly.fade_in = 1.2;
        shots.Add(dolly);
        Vector3 fire_target = new Vector3(0.4f, 0.75f, -0.9f);
        Cinematic.Shot orbit = _shot(_orbit(new Vector3(0.3f, 0.0f, -0.8f), 5.6, 1.7, 215.0, 335.0), _repeat(fire_target), 12.0, 46.0);
        shots.Add(orbit);
        Cinematic.Shot close = _shot(new Godot.Collections.Array<Vector3> { new Vector3(1.9f, 0.5f, 1.7f), new Vector3(1.05f, 0.55f, 0.95f) }, new Godot.Collections.Array<Vector3> { new Vector3(0.0f, 0.35f, 0.0f), new Vector3(0.0f, 0.4f, 0.0f) }, 8.0, 34.0);
        close.focus = true;
        shots.Add(close);
        Cinematic.Shot pile = _shot(new Godot.Collections.Array<Vector3> { new Vector3(3.0f, 1.3f, 1.2f), new Vector3(4.4f, 1.25f, 0.0f) }, new Godot.Collections.Array<Vector3> { new Vector3(5.4f, 0.55f, -2.4f), new Vector3(6.9f, 0.9f, -4.0f) }, 7.0, 40.0);
        pile.focus = true;
        shots.Add(pile);
        return shots;
    }

    public static List<Cinematic.Shot> pond()
    {
        List<Cinematic.Shot> shots = new List<Cinematic.Shot>();
        shots.Add(_shot(new Godot.Collections.Array<Vector3> { new Vector3(-5.0f, 1.65f, -1.5f), new Vector3(-11.0f, 1.6f, -0.2f), new Vector3(-14.0f, 1.55f, 3.5f) }, new Godot.Collections.Array<Vector3> { new Vector3(-20.0f, 0.8f, 4.0f), new Vector3(-24.0f, 0.6f, 5.5f), new Vector3(-30.0f, 0.4f, 5.0f) }, 10.0, 52.0));
        shots.Add(_shot(new Godot.Collections.Array<Vector3> { new Vector3(-16.4f, 1.1f, 6.2f), new Vector3(-23.0f, 1.1f, 5.4f) }, new Godot.Collections.Array<Vector3> { new Vector3(-28.0f, -0.4f, 4.0f), new Vector3(-42.0f, 1.2f, 0.0f) }, 9.0, 50.0, true));
        Cinematic.Shot canoe = _shot(new Godot.Collections.Array<Vector3> { new Vector3(-24.5f, 0.0f, 1.0f), new Vector3(-22.2f, 0.05f, 0.6f) }, new Godot.Collections.Array<Vector3> { new Vector3(-21.0f, -0.6f, 3.8f), new Vector3(-20.6f, -0.5f, 4.0f) }, 6.0, 40.0, true);
        canoe.focus = true;
        shots.Add(canoe);
        shots.Add(_shot(new Godot.Collections.Array<Vector3> { new Vector3(-22.0f, -0.25f, 9.5f), new Vector3(-33.0f, -0.35f, 6.0f), new Vector3(-43.0f, -0.2f, 1.0f) }, new Godot.Collections.Array<Vector3> { new Vector3(-36.0f, -0.6f, 4.0f), new Vector3(-50.0f, -0.5f, -4.0f), new Vector3(-55.0f, 1.5f, -8.0f) }, 9.0, 56.0, true));
        // Under the surface past the end piles (a pile stays in frame, a metre
        // off the lens), then a breach in open water looking back at the jetty.
        shots.Add(_shot(new Godot.Collections.Array<Vector3> { new Vector3(-21.8f, -1.8f, 2.9f), new Vector3(-26.5f, -2.3f, 4.0f) }, new Godot.Collections.Array<Vector3> { new Vector3(-30.0f, -2.5f, 5.0f), new Vector3(-35.0f, -3.0f, 4.0f) }, 7.0, 60.0, true));
        shots.Add(_shot(new Godot.Collections.Array<Vector3> { new Vector3(-30.0f, -1.6f, 2.0f), new Vector3(-29.0f, 0.7f, 1.2f) }, new Godot.Collections.Array<Vector3> { new Vector3(-22.0f, 0.0f, 5.0f), new Vector3(-16.0f, 1.2f, 6.0f) }, 6.0, 55.0, true));
        return shots;
    }

    public static List<Cinematic.Shot> nightfall()
    {
        List<Cinematic.Shot> shots = new List<Cinematic.Shot>();
        Cinematic.Shot lapse = _shot(new Godot.Collections.Array<Vector3> { new Vector3(7.0f, 1.55f, 7.0f), new Vector3(6.2f, 1.6f, 7.6f) }, _repeat(new Vector3(0.5f, 0.9f, -1.0f)), 14.0, 48.0);
        lapse.hour = 19.35;
        lapse.hour_end = 23.6;
        shots.Add(lapse);
        Cinematic.Shot orbit = _shot(_orbit(new Vector3(0.0f, 0.0f, 0.0f), 4.2, 1.25, 10.0, 150.0), _repeat(new Vector3(0.0f, 0.45f, 0.0f)), 10.0, 42.0);
        orbit.hour = 23.4;
        orbit.feed_fire = true;
        shots.Add(orbit);
        Cinematic.Shot tent = _shot(new Godot.Collections.Array<Vector3> { new Vector3(2.8f, 1.4f, 1.5f), new Vector3(4.8f, 1.3f, -1.2f) }, _repeat(new Vector3(7.5f, 0.9f, -4.5f)), 7.0, 44.0);
        tent.hour = 23.4;
        shots.Add(tent);
        Cinematic.Shot dock = _shot(new Godot.Collections.Array<Vector3> { new Vector3(-14.0f, 1.6f, 9.0f), new Vector3(-18.5f, 1.5f, 8.0f) }, new Godot.Collections.Array<Vector3> { new Vector3(-24.5f, 0.5f, 5.0f), new Vector3(-30.0f, 2.0f, 0.0f) }, 8.0, 50.0);
        dock.hour = 23.6;
        shots.Add(dock);
        return shots;
    }

    public static List<Cinematic.Shot> afterglow()
    {
        /// A compact film progressing from open water to the last light at camp.
        /// Each composition has one subject and enough lateral motion for parallax.
        List<Cinematic.Shot> shots = new List<Cinematic.Shot>();
        Cinematic.Shot lake = _shot(new Godot.Collections.Array<Vector3> { new Vector3(-15.5f, 1.8f, 9.0f), new Vector3(-14.7f, 1.9f, 10.3f) }, new Godot.Collections.Array<Vector3> { new Vector3(-39.0f, 1.0f, -1.0f), new Vector3(-37.0f, 1.2f, -2.0f) }, 7.0, 54.0);
        lake.label = "The still water";
        lake.hour = 19.05;
        lake.fade_in = 1.2;
        shots.Add(lake);
        Cinematic.Shot lilies = _shot(new Godot.Collections.Array<Vector3> { new Vector3(-27.0f, -0.15f, 11.2f), new Vector3(-28.0f, -0.23f, 11.8f) }, _repeat(new Vector3(-29.2f, -0.87f, 14.4f)), 6.0, 40.0, true);
        lilies.label = "Life at the edge";
        lilies.caption = "Gerstner waves with lily pads and stems riding the surface; pond simulation with persistent ripples";
        lilies.hour = 19.15;
        lilies.focus = true;
        shots.Add(lilies);
        Cinematic.Shot meadow = _shot(new Godot.Collections.Array<Vector3> { new Vector3(9.0f, 0.9f, 8.0f), new Vector3(7.5f, 1.05f, 8.4f) }, _repeat(new Vector3(-0.5f, 1.0f, -0.5f)), 6.0, 48.0);
        meadow.label = "Through the meadow";
        meadow.caption = "Geometry grass and wildflowers in one wind field; photoscanned ferns and shrubs; wildlife on cue";
        meadow.hour = 19.20;
        shots.Add(meadow);
        Cinematic.Shot kitchen = _shot(new Godot.Collections.Array<Vector3> { new Vector3(7.6f, 1.45f, 3.0f), new Vector3(8.1f, 1.42f, 3.1f) }, _repeat(new Vector3(9.1f, 0.93f, 1.1f)), 6.0, 43.0);
        kitchen.label = "Room for two";
        kitchen.caption = "Jolt soft-body tablecloth; generated props beside Poly Haven photoscans; bokeh depth of field";
        kitchen.hour = 19.35;
        kitchen.focus = true;
        shots.Add(kitchen);
        Cinematic.Shot fire = _shot(new Godot.Collections.Array<Vector3> { new Vector3(0.1f, 0.85f, 2.5f), new Vector3(0.45f, 0.90f, 2.35f) }, _repeat(new Vector3(0.0f, 0.68f, 0.0f)), 6.0, 45.0);
        fire.label = "The hearth";
        fire.hour = 20.20;
        fire.focus = true;
        shots.Add(fire);
        Cinematic.Shot ripples = _shot(new Godot.Collections.Array<Vector3> { new Vector3(-23.5f, 0.7f, 8.0f), new Vector3(-24.2f, 0.65f, 8.2f) }, _repeat(new Vector3(-28.5f, -0.75f, 3.8f)), 7.0, 50.0, true);
        ripples.label = "One last stone";
        ripples.hour = 20.35;
        ripples.skip_stone = true;
        shots.Add(ripples);
        Cinematic.Shot camp = _shot(new Godot.Collections.Array<Vector3> { new Vector3(6.3f, 1.5f, 7.2f), new Vector3(7.0f, 1.7f, 8.2f) }, _repeat(new Vector3(2.8f, 1.0f, -1.8f)), 7.0, 48.0);
        camp.label = "Stay a little longer";
        camp.hour = 21.20;
        camp.fade_out = 0.7;
        shots.Add(camp);
        Cinematic.Shot stars = _shot(new Godot.Collections.Array<Vector3> { new Vector3(-15.5f, 2.1f, 9.0f), new Vector3(-15.5f, 2.9f, 9.5f) }, new Godot.Collections.Array<Vector3> { new Vector3(-65.0f, 19.0f, 8.0f), new Vector3(-65.0f, 28.0f, 8.0f) }, 8.0, 60.0);
        stars.label = "Under the stars";
        stars.caption = "Star field and Milky Way; star reflections on still water as the storm clears";
        stars.hour = 23.4;
        stars.fade_in = 1.2;
        stars.fade_out = 1.8;
        shots.Add(stars);
        return shots;
    }

    public static List<Cinematic.Shot> sequence(string name)
    {
        switch (name)
        {
            case "showcase":
                return showcase();
            case "storm":
                List<Cinematic.Shot> weather_shots = new List<Cinematic.Shot>();
                foreach (Cinematic.Shot shot in one_night())
                {
                    if (new Godot.Collections.Array { "storm", "storm_short", "rain_detail", "shelter_rain" }.Contains(shot.weather))
                    {
                        weather_shots.Add(shot);
                    }
                }
                return weather_shots;
            case "one_night":
                return one_night();
            case "afterglow":
                return afterglow();
            case "arrival":
                return arrival();
            case "pond":
                return pond();
            case "nightfall":
                return nightfall();
            case "showreel":
                List<Cinematic.Shot> all = new List<Cinematic.Shot>();
                all.AddRange(arrival());
                all.AddRange(pond());
                all.AddRange(nightfall());
                return all;
            default:
                return new List<Cinematic.Shot>();
        }
    }

    public static double end_seconds(string name)
    {
        /// Closing card plus the credit roll: long enough to read every creator in
        /// the five-minute film, brisker after the short reel.
        switch (name)
        {
            case "one_night":
                return 62.0;
            case "showcase":
                return 38.0;
        }
        return END_SECONDS;
    }

    public static double duration(string name)
    {
        double seconds = TITLE_SECONDS + end_seconds(name);
        foreach (Cinematic.Shot shot in sequence(name))
        {
            seconds += shot.duration;
        }
        return seconds;
    }

    public static Godot.Collections.Array<Godot.Collections.Dictionary> score_cues(string _name)
    {
        // Retain the standalone synthesis study, but every film is nature-only.
        return new Godot.Collections.Array<Godot.Collections.Dictionary>();
    }

    public static Transform3D tent_transform(TerrainField field)
    {
        Vector2 at = TerrainField.TENT;
        return new Transform3D(Basis.LookingAt(new Vector3(-at.X, 0, -at.Y).Normalized(), Vector3.Up), new Vector3(at.X, (float)field.height(at.X, at.Y), at.Y));
    }

    public static List<Cinematic.Shot> one_night()
    {
        /// Five-minute camper's journey: each pause has a subject or a visible event.
        /// Five seconds of titles + 270 seconds of scenes + 62 seconds of card and credits.
        List<Cinematic.Shot> shots = new List<Cinematic.Shot>();
        TerrainField field = new TerrainField();
        Cinematic.Shot arrival = _shot(new Godot.Collections.Array<Vector3> { new Vector3(2, 1.64f, 34), new Vector3(1.5f, 1.64f, 20), new Vector3(1, 1.64f, 9) }, new Godot.Collections.Array<Vector3> { new Vector3(1.5f, 0.8f, 23), new Vector3(0.5f, 0.9f, 0), new Vector3(1.0f, 1.0f, -1.2f) }, 25.0, 58.0);
        arrival.label = "A clearing in the woods";
        arrival.caption = "Procedural woodland: 1,278 generated trees around the camp and 21,000 on the hills; SDFGI and volumetric fog";
        arrival.fov_end = 54.0;
        arrival.walk = true;
        arrival.lantern_state = 0;
        arrival.fire = 1.0;
        shots.Add(arrival);
        Cinematic.Shot meadow = _shot(new Godot.Collections.Array<Vector3> { new Vector3(6.8f, 0.72f, 9.4f), new Vector3(6.35f, 0.76f, 9.55f) }, new Godot.Collections.Array<Vector3> { new Vector3(4.3f, 0.78f, 10.2f), new Vector3(4.0f, 0.70f, 10.5f) }, 12.0, 44.0);
        meadow.label = "A breeze through the meadow";
        meadow.focus = true;
        meadow.wildlife_cue = "meadow_life";
        shots.Add(meadow);
        Cinematic.Shot kitchen = _shot(new Godot.Collections.Array<Vector3> { new Vector3(7.5f, 1.22f, 3.0f), new Vector3(7.75f, 1.10f, 2.85f) }, _repeat(new Vector3(9.18f, 1.08f, 1.16f)), 10.0, 43.0);
        kitchen.label = "A place for two";
        kitchen.focus = true;
        kitchen.focus_distance = 1.85;
        kitchen.focus_distance_end = 2.7;
        kitchen.exposure = 0.95;
        shots.Add(kitchen);
        Cinematic.Shot approach = _shot(new Godot.Collections.Array<Vector3> { new Vector3(-3, 1.64f, 0.8f), new Vector3(-6, 1.64f, -2.5f), new Vector3(-12, 1.64f, -0.5f), new Vector3(-14.5f, 1.64f, 3), new Vector3(-15.9f, 1.64f, 6.3f), new Vector3(-18.5f, 1.64f, 5.97f) }, new Godot.Collections.Array<Vector3> { new Vector3(-19, 0.6f, 2), new Vector3(-25, 0.3f, 4.0f), new Vector3(-30, 0.2f, 4) }, 23.0, 56.0);
        approach.label = "Down to the water";
        approach.caption = "Parallax-mapped terrain, shoreline wetness; a grounded first-person walk with surface-aware footsteps";
        approach.walk = true;
        approach.exposure = 0.90;
        shots.Add(approach);
        Cinematic.Shot birds = _shot(new Godot.Collections.Array<Vector3> { new Vector3(-16.5f, 1.0f, 7.7f), new Vector3(-16.7f, 1.02f, 7.75f) }, new Godot.Collections.Array<Vector3> { new Vector3(-19.723f, 0.21f, 6.612f), new Vector3(-25.5f, 0.8f, 4.2f) }, 13.0, 43.0, true);
        birds.label = "A visitor on the jetty";
        birds.caption = "Planar reflections from a mirrored camera; procedural birds; sky, fog and sun from one atmosphere model";
        birds.wildlife_cue = "pond_birds";
        birds.exposure = 0.95;
        shots.Add(birds);
        Cinematic.Shot lilies = afterglow()[1];
        lilies.label = "Life at the waterline";
        lilies.duration = 12.0;
        lilies.wildlife_cue = "pond_life";
        shots.Add(lilies);
        // Keep the complete, reviewed entry/immersion/breach path at its original
        // 28-second pace. The preceding stone throw ends at precisely this pose.
        Vector3 water_start = new Vector3(-20.4f, -0.35f, 7.8f);
        Vector3 water_look = new Vector3(-30.5f, -0.4f, 9.2f);
        Cinematic.Shot stone = _shot(new Godot.Collections.Array<Vector3> { new Vector3(-20.2f, -0.30f, 8.0f), water_start }, new Godot.Collections.Array<Vector3> { new Vector3(-30.3f, -0.35f, 9.4f), water_look }, 10.0, 64.0, true);
        stone.label = "Three skips";
        stone.caption = "Physics-driven skipping stone; ripples, foam and sun glitter on the water";
        stone.skip_stone = true;
        stone.stone_at = 3.0;
        stone.stone_origin = new Vector3(-21.2f, -0.1f, 7.9f);
        stone.stone_direction = new Vector3(-1.0f, 0.0f, 0.15f);
        stone.exposure = 0.9;
        shots.Add(stone);
        Cinematic.Shot water = _shot(new Godot.Collections.Array<Vector3> { water_start, new Vector3(-21.0f, -1.15f, 7.60f), new Vector3(-21.9f, -1.90f, 7.00f), new Vector3(-22.3f, -2.00f, 6.85f), new Vector3(-22.4f, -1.30f, 6.90f), new Vector3(-22.45f, -1.02f, 6.95f), new Vector3(-22.55f, -0.858f, 7.05f), new Vector3(-22.70f, -0.858f, 7.55f), new Vector3(-22.9f, -0.858f, 8.1f), new Vector3(-22.1f, -0.65f, 7.95f), new Vector3(-21.9f, -0.35f, 7.85f) }, new Godot.Collections.Array<Vector3> { water_look, new Vector3(-30, -1.3f, 8.4f), new Vector3(-30, -1.3f, 8.4f), new Vector3(-31.5f, 0.2f, 6.3f), new Vector3(-32, 0.5f, 3.2f) }, 28.0, 64.0, true);
        water.label = "Beneath the reflections";
        water.caption = "Underwater: caustics, sun shafts, silt and Snell's window through the moving surface";
        water.fov_end = 56.0;
        water.continuous_in = true;
        water.exposure = 0.9;
        water.pitch_envelope = new Godot.Collections.Array<Vector3> { new Vector3(11.8f, -11.0f, 0.0f), new Vector3(15.1f, -11.0f, 1.0f), new Vector3(17.8f, -11.0f, 1.0f), new Vector3(21.5f, -11.0f, 0.0f) };
        shots.Add(water);
        Cinematic.Shot sunset = _shot(new Godot.Collections.Array<Vector3> { new Vector3(-15.5f, 1.65f, 9), new Vector3(-15.15f, 1.66f, 9.25f) }, new Godot.Collections.Array<Vector3> { new Vector3(-39, 1.2f, -1), new Vector3(-37, 2.0f, -1.8f) }, 22.0, 54.0);
        sunset.label = "The last light";
        sunset.caption = "Single-scattering sky and sun glitter; the same world through a full day and night cycle";
        sunset.lantern_hour = 20.30;
        sunset.exposure = 0.85;
        sunset.exposure_end = 1.0;
        shots.Add(sunset);
        Cinematic.Shot returning = _shot(new Godot.Collections.Array<Vector3> { new Vector3(-8, 1.64f, 5.0f), new Vector3(-5, 1.64f, 4.1f), new Vector3(-3, 1.64f, 3.3f), new Vector3(-1.6f, 1.64f, 2.6f) }, new Godot.Collections.Array<Vector3> { new Vector3(0, 0.72f, 0), new Vector3(0, 0.72f, 0) }, 12.0, 52.0);
        returning.label = "Back to the fire";
        returning.caption = "Moonlight, fireflies and a warming fire; exposure follows the fading light";
        returning.walk = true;
        returning.walk_surface = "grass";
        returning.exposure = 1.0;
        returning.fire = 0.55;
        shots.Add(returning);
        Cinematic.Shot hearth = _shot(new Godot.Collections.Array<Vector3> { new Vector3(-1.6f, 1.64f, 2.6f), new Vector3(-1.1f, 1.15f, 2.3f), new Vector3(-0.7f, 1.0f, 2.1f) }, new Godot.Collections.Array<Vector3> { new Vector3(0, 0.72f, 0), new Vector3(0, 0.55f, 0) }, 14.0, 52.0);
        hearth.label = "Tending the hearth";
        hearth.caption = "Ray-marched volumetric flames; GPU sparks and lit smoke that wraps the pot; interactive fire";
        hearth.continuous_in = true;
        hearth.feed_fire = true;
        hearth.feed_at = 6.0;
        shots.Add(hearth);
        Cinematic.Shot tent_out = _shot(new Godot.Collections.Array<Vector3> { new Vector3(4.1f, 1.30f, 0.25f), new Vector3(4.6f, 1.30f, -0.10f) }, new Godot.Collections.Array<Vector3> { new Vector3(7.35f, 1.05f, -4.4f), new Vector3(7.65f, 1.05f, -4.5f) }, 10.0, 37.0);
        tent_out.label = "The shelter glows";
        tent_out.caption = "Canvas translucency and lantern light; SDFGI bounce across the camp";
        tent_out.weather = "gathering";
        shots.Add(tent_out);
        Cinematic.Shot storm = _shot(new Godot.Collections.Array<Vector3> { new Vector3(-16.8f, 1.38f, 9.0f), new Vector3(-17.1f, 1.4f, 9.15f) }, new Godot.Collections.Array<Vector3> { new Vector3(-34, 2.0f, 0.4f), new Vector3(-34, 5.0f, -0.2f) }, 20.0, 61.0);
        storm.label = "A storm across the pond";
        storm.caption = "Ray-marched cumulus; lightning lights the clouds in the same frame; world-space rain";
        storm.weather = "storm_short";
        shots.Add(storm);
        Cinematic.Shot wet_deck = _shot(new Godot.Collections.Array<Vector3> { new Vector3(-20.65f, -0.02f, 6.07f), new Vector3(-21.05f, 0.08f, 6.01f) }, new Godot.Collections.Array<Vector3> { new Vector3(-23.5f, -0.28f, 5.20f), new Vector3(-23.8f, -0.35f, 4.9f) }, 10.0, 48.0, true);
        wet_deck.label = "Rain on the timber";
        wet_deck.caption = "Wet materials; rain impacts and lantern reflections on the timber";
        wet_deck.weather = "rain_detail";
        shots.Add(wet_deck);
        Transform3D tent = tent_transform(field);
        Cinematic.Shot interior = _shot(new Godot.Collections.Array<Vector3> { tent * new Vector3(0.45f, 0.70f, -0.15f), tent * new Vector3(0.43f, 0.72f, -0.23f) }, _repeat(new Vector3(0, (float)(field.height(0, 0) + 0.9), 0)), 14.0, 64.0, true);
        interior.label = "Rain on the canvas";
        interior.caption = "Sheltered rain; translucent canvas and soft-body cloth; recorded thunder";
        interior.weather = "shelter_rain";
        interior.interior = true;
        interior.exposure = 0.9;
        shots.Add(interior);
        Vector3 star_pos = new Vector3(-23.7f, -0.30f, 9.6f);
        Godot.Collections.Array<Vector3> star_look = new Godot.Collections.Array<Vector3>();
        Vector3 reflection_dir = (new Vector3(-24.25f, -1.1f, 5.52f) - star_pos).Normalized();
        Vector3 sky_dir = (new Vector3(-58, 28, -4) - star_pos).Normalized();
        for (long i = 0; i < 7; i++)
        {
            star_look.Add(star_pos + reflection_dir.Slerp(sky_dir, (float)((double)i / 6.0)) * 50.0f);
        }
        Cinematic.Shot stars = _shot(_repeat(star_pos), star_look, 18.0, 60.0, true);
        stars.label = "While the world sleeps";
        stars.weather = "clearing";
        stars.fire_end = 0.12;
        shots.Add(stars);
        Cinematic.Shot dawn = _shot(new Godot.Collections.Array<Vector3> { new Vector3(1.5f, 1.64f, 20), new Vector3(1.0f, 1.64f, 11) }, new Godot.Collections.Array<Vector3> { new Vector3(1.0f, 1.0f, -1.2f), new Vector3(1.0f, 1.0f, -1.2f) }, 17.0, 54.0);
        dawn.label = "First light";
        dawn.caption = "Dawn fog and aerial perspective; the clearing again at first light";
        dawn.walk = true;
        dawn.weather = "dawn";
        dawn.fire_end = 0.08;
        dawn.exposure = 0.90;
        dawn.exposure_end = 1.0;
        shots.Add(dawn);
        Godot.Collections.Array hours = new Godot.Collections.Array { 17.00, 17.18, 17.23, 17.31, 17.44, 17.50, 17.60, 17.70, 17.85, 20.95, 21.07, 21.40, 21.55, 21.90, 21.95, 22.15, 23.15, 30.60 };
        Godot.Collections.Array winds = new Godot.Collections.Array { 0.75, 0.90, 1.05, 0.85, 0.70, 0.70, 0.65, 0.70, 0.70, 0.45, 0.55, 0.85, 1.30, 1.55, 1.55, 0.85, 0.25, 0.60 };
        for (long i2 = 0, i_end = (long)shots.Count; i2 < i_end; i2++)
        {
            Cinematic.Shot shot = shots[(int)i2];
            shot.hour = hours[(int)i2].AsDouble();
            shot.hour_end = hours[(int)(i2 + 1)].AsDouble();
            shot.wind = winds[(int)i2].AsDouble();
            shot.wind_end = winds[(int)(i2 + 1)].AsDouble();
            shot.clock = true;
            shot.fade_in = 0.8;
            shot.fade_out = 0.8;
            shot.hold_start = 0.8;
            shot.hold_end = 1.2;
            shot.ramp_seconds = 1.5;
        }
        arrival.fade_in = 1.25;
        arrival.hold_start = 0.7;
        arrival.hold_end = 1.5;
        approach.hold_start = 0.6;
        approach.hold_end = 0.8;
        birds.hold_start = 3.0;
        birds.hold_end = 1.0;
        returning.fade_out = 0.0;
        hearth.fade_in = 0.0;
        hearth.hold_end = 3.0;
        stone.fade_out = 0.0;
        stone.hold_end = 0.6;
        water.fade_in = 0.0;
        water.hold_start = 0.6;
        water.hold_end = 6.2;
        water.ramp_seconds = 2.5;
        water.rest_at = new Spline(water.path).progress_at_point(3);
        water.rest_seconds = 3.5;
        stars.hold_start = 1.0;
        stars.hold_end = 1.5;
        // An overnight fade is preferable to racing five hours of star motion.
        // Open in civil twilight so the closing walk actually carries first light:
        // the sky is already warming and the sun clears the trees before the end.
        dawn.hour = 29.55;
        dawn.fade_in = 1.4;
        dawn.fade_out = 1.6;
        return shots;
    }

    public static List<Cinematic.Shot> showcase()
    {
        /// Compact environmental study: real movement, a close subject, water,
        /// weather and the warm camp. Same world, native sound, no enlarged wildlife.
        List<Cinematic.Shot> shots = new List<Cinematic.Shot>();
        // Walk in toward the camp, as the film does, instead of turning to the pond.
        Cinematic.Shot arrival = _shot(new Godot.Collections.Array<Vector3> { new Vector3(1.6f, 1.64f, 24), new Vector3(1.2f, 1.64f, 16), new Vector3(1.0f, 1.64f, 10) }, new Godot.Collections.Array<Vector3> { new Vector3(1.0f, 0.9f, -1.2f), new Vector3(1.0f, 1.0f, -1.2f) }, 10.0, 56.0);
        arrival.label = "A clearing in the woods";
        arrival.caption = "Procedural woodland: 1,278 generated trees around the camp and 21,000 on the hills; SDFGI and volumetric fog";
        arrival.walk = true;
        arrival.hour = 18.1;
        arrival.lantern_state = 1;
        arrival.fire = 1.0;
        shots.Add(arrival);
        Cinematic.Shot close = one_night()[2];
        close.label = "A place to stay";
        close.caption = "Jolt soft-body tablecloth; generated props beside Poly Haven photoscans; bokeh depth of field";
        close.duration = 6.0;
        close.hour = 18.2;
        close.hour_end = NAN;
        close.hold_start = 0.5;
        close.hold_end = 0.5;
        close.focus_distance = 1.85;
        close.focus_distance_end = 2.4;
        shots.Add(close);
        Cinematic.Shot tent = _shot(new Godot.Collections.Array<Vector3> { new Vector3(4.4f, 1.35f, 0.4f), new Vector3(4.8f, 1.3f, -0.2f) }, new Godot.Collections.Array<Vector3> { new Vector3(7.35f, 1.05f, -4.4f), new Vector3(7.65f, 1.05f, -4.5f) }, 6.0, 40.0);
        tent.label = "The shelter";
        tent.caption = "Sewn canvas with translucency; lantern, woodpile and photoscanned camp props";
        tent.hour = 18.3;
        shots.Add(tent);
        Cinematic.Shot down = _shot(new Godot.Collections.Array<Vector3> { new Vector3(-12, 1.64f, -0.5f), new Vector3(-14.5f, 1.64f, 3), new Vector3(-15.9f, 1.64f, 6.3f), new Vector3(-18.5f, 1.64f, 5.97f) }, new Godot.Collections.Array<Vector3> { new Vector3(-19, 0.6f, 2), new Vector3(-25, 0.3f, 4.0f), new Vector3(-30, 0.2f, 4) }, 11.0, 56.0);
        down.label = "Down to the water";
        down.caption = "Parallax-mapped terrain, shoreline wetness; a grounded first-person walk with surface-aware footsteps";
        down.walk = true;
        down.hour = 18.5;
        down.exposure = 0.92;
        shots.Add(down);
        Cinematic.Shot pond_view = _shot(new Godot.Collections.Array<Vector3> { new Vector3(-15.1f, 1.65f, 9.5f), new Vector3(-15.7f, 1.65f, 9.15f) }, new Godot.Collections.Array<Vector3> { new Vector3(-37.5f, 0.2f, -1), new Vector3(-40, 0.2f, -2) }, 7.0, 57.0);
        pond_view.label = "A living shoreline";
        pond_view.caption = "Planar reflections from a mirrored camera; Gerstner waves; sky, fog and sun from one atmosphere model";
        pond_view.hour = 18.8;
        shots.Add(pond_view);
        Cinematic.Shot life = one_night()[4];
        life.duration = 7.0;
        life.hour = 18.0;
        life.hour_end = NAN;
        life.label = "The visitor";
        life.caption = "Procedural birds and fish on cue; lily pads riding the waves";
        shots.Add(life);
        Cinematic.Shot rain = one_night()[13];
        rain.duration = 6.0;
        rain.hour = 19.2;
        rain.hour_end = NAN;
        rain.label = "Rain on timber";
        rain.caption = "Wet materials; rain impacts and lantern reflections on the timber";
        shots.Add(rain);
        // Low in the wet grass behind scanned ferns, looking across the clearing to
        // the tent; the hero oak stands at the frame's right edge.
        Cinematic.Shot aftermath = _shot(new Godot.Collections.Array<Vector3> { new Vector3(21.2f, 1.3f, -10.6f), new Vector3(20.5f, 1.32f, -10.1f) }, new Godot.Collections.Array<Vector3> { new Vector3(12.0f, 1.9f, -5.8f), new Vector3(11.8f, 2.0f, -5.5f) }, 7.0, 50.0);
        aftermath.hour = 17.5;
        aftermath.weather = "dawn";
        aftermath.label = "After the rain";
        aftermath.caption = "Scene-wide wetness, low mist and sun shafts through the damp air";
        shots.Add(aftermath);
        Cinematic.Shot hearth = _shot(new Godot.Collections.Array<Vector3> { new Vector3(4.6f, 1.55f, 3.8f), new Vector3(4.1f, 1.5f, 4.1f) }, new Godot.Collections.Array<Vector3> { new Vector3(1.0f, 0.70f, -0.5f), new Vector3(2.0f, 0.85f, -1.6f) }, 8.0, 54.0);
        hearth.hour = 21.20;
        hearth.label = "Stay a little longer";
        hearth.caption = "Ray-marched volumetric flames; GPU sparks and lit smoke; fireflies and moonlight";
        hearth.feed_fire = true;
        hearth.feed_at = 2.0;
        shots.Add(hearth);
        foreach (Cinematic.Shot shot in shots)
        {
            shot.clock = true;
            shot.fade_in = 0.45;
            shot.fade_out = 0.45;
            shot.ramp_seconds = 1.1;
            shot.exposure = is_nan(shot.exposure) || shot.exposure == 1.0 ? 1.0 : shot.exposure;
            shot.exposure_end = NAN;
        }
        return shots;
    }
}
