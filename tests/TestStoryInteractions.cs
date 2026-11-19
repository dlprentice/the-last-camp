using System;
using System.Linq;
using Godot;
using LastCamp.Story;

namespace LastCamp.Tests;

/// The preconditions of the story's interactions, with the game's own rules (no
/// `RequireUnbuilt`): Tom's tin waits for the old ring, the loose step offers
/// "Lift" only after the player has stood on it and sat on the bench, the notch
/// means nothing before the tin, and the props start out offering nothing.
public partial class TestStoryInteractions : TestCase
{
    private static StoryState MarksEvening(bool tin)
    {
        var s = new StoryState();
        s.SetPosition(StoryScript.ChapterIndex(StoryScript.Marks), 0);
        if (tin)
        {
            s.Raise(StoryEvents.PlaceOldRing);
            s.Raise(StoryEvents.TinOpened);
        }
        s.SetPosition(StoryScript.ChapterIndex(StoryScript.Marks), 1);
        return s;
    }

    public void test_tin_before_the_ring_is_refused()
    {
        var s = new StoryState();
        assert_false(s.TinAvailable, "on the first evening the tin is not offered");
        s.SetPosition(StoryScript.ChapterIndex(StoryScript.Marks), 0);
        assert_false(s.TinAvailable, "on the second morning it waits for the old ring");
        assert_false(s.Raise(StoryEvents.TinOpened), "opening it before the ring moves nothing");
        assert_false(s.Facts.Contains(StoryEvents.TinOpened) || s.LinesPlayed.Contains("m7"), "and records and plays nothing");
        s.Raise(StoryEvents.PlaceOldRing);
        assert_true(s.TinAvailable, "after the old ring the tin is offered");
        assert_true(s.Raise(StoryEvents.TinOpened), "and opening it counts");
    }

    public void test_lift_before_standing_on_the_step_is_refused()
    {
        StoryState s = MarksEvening(tin: true);
        assert_false(CampSession.LiftAllowed(s, stoodOn: false, lifted: false), "nothing to lift before the notch, the bench or the step");
        s.Raise(StoryEvents.BlazeFound);
        s.Raise(StoryEvents.SatLookout);
        assert_false(CampSession.LiftAllowed(s, stoodOn: false, lifted: false), "with the notch and the bench done, still refused until the player has stood on it");
        assert_true(CampSession.LiftAllowed(s, stoodOn: true, lifted: false), "once stood on, the step offers Lift");
        assert_false(CampSession.LiftAllowed(s, stoodOn: true, lifted: true), "and never again once lifted");

        StoryState early = MarksEvening(tin: true);
        early.Raise(StoryEvents.BlazeFound);
        assert_false(CampSession.LiftAllowed(early, stoodOn: true, lifted: false), "standing on it before sitting on the bench is not enough");
        var morning = new StoryState();
        morning.SetPosition(StoryScript.ChapterIndex(StoryScript.Marks), 0);
        assert_false(CampSession.LiftAllowed(morning, stoodOn: true, lifted: false), "and it never lifts outside the climb's evening");
    }

    public void test_notch_before_the_tin_raises_nothing()
    {
        StoryState before = MarksEvening(tin: false);
        assert_false(CampSession.NotchCounts(before), "the notch means nothing before the tin");
        before.Raise(StoryEvents.BlazeFound);
        Objective blaze = before.Band.Objectives.First(o => o.Event == StoryEvents.BlazeFound);
        assert_eq(before.ProgressOf(blaze), 0, "even raised, it moves no objective");
        StoryState after = MarksEvening(tin: true);
        assert_true(CampSession.NotchCounts(after), "after the tin it counts");
        after.Raise(StoryEvents.BlazeFound);
        assert_eq(after.ProgressOf(after.Band.Objectives.First(o => o.Event == StoryEvents.BlazeFound)), 1, "and it is Tom's first blaze");
    }

    public void test_notch_is_found_only_by_looking_at_it_up_close()
    {
        Vector3 notch = new(0, 0.3f, 0);
        Vector3 face = new Vector3(1, 0.55f, 0).Normalized();
        Vector3 eye = notch + new Vector3(1.0f, 1.38f, 0);
        assert_true(CampSession.NotchInView(eye, notch - eye, notch, face), "looking at it from 1.7 m on its cut side finds it");
        Vector3 far = notch + new Vector3(2.5f, 1.38f, 0);
        assert_false(CampSession.NotchInView(far, notch - far, notch, face), "from beyond 2 m it is not found");
        assert_false(CampSession.NotchInView(eye, Vector3.Forward, notch, face), "nor when looking elsewhere");
        Vector3 behind = notch + new Vector3(-1.0f, 1.0f, 0);
        assert_false(CampSession.NotchInView(behind, notch - behind, notch, face), "nor from the side away from the cut");
    }

    public void test_loose_step_rocks_a_few_degrees_about_its_back_edge_and_sounds_hollow()
    {
        Transform3D rest = new(TomsRock.summit_basis(), new Vector3(3, 1, 2));
        Transform3D rocked = TomsRock.rocked_pose(rest, TomsRock.ROCK_DEG);
        double tilt = Mathf.RadToDeg(rocked.Basis.Y.AngleTo(rest.Basis.Y));
        assert_true(tilt > 1.5 && tilt < 4.0, $"it rocks a few degrees ({tilt:F2})");
        Vector3 h = TomsRock.LOOSE_SIZE * 0.5f;
        Vector3 hinge = new(0, -h.Y, h.Z);
        assert_lt((rest * hinge).DistanceTo(rocked * hinge), 1e-4, "about its settled back edge");
        Vector3 front = new(0, h.Y, -h.Z);
        assert_lt((rocked * front).Y, (rest * front).Y, "its front goes down under the foot");
        assert_true(Sounds.Footsteps.ContainsKey("hollow"), "the hollow step has a footstep sound");

        var rock = new TomsRock(new TerrainField());
        rock._build_loose_step(new StandardMaterial3D());
        assert_eq(rock.loose_body.GetMeta("surface").AsString(), "hollow", "the footstone's surface is hollow");
        rock.update_rock(true, 10.0);
        assert_true(Math.Abs(rock.rock_angle - TomsRock.ROCK_DEG) < 0.01, "under a foot it eases to its rocked angle");
        rock.update_rock(false, 10.0);
        assert_lt(rock.rock_angle, 0.01, "and settles back when the foot leaves");
        rock.lift_step();
        assert_true(rock.step_lifted && rock.loose_step.Transform.Origin.DistanceTo(rock.lifted_pose().Origin) < 1e-4, "lifting lays it aside");
        assert_false(rock.is_on_loose_step(rock.lifted_pose().Origin), "a lifted step is not stood on");
        rock.Free();
    }

    public void test_reading_sheet_writes_in_each_hand_and_respects_plain_text()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        bool plain = GameSettings.Current.PlainText;
        var sheet = new ReadingSheet();
        tree.Root.AddChild(sheet);
        GameSettings.Current.PlainText = false;
        sheet.Read(StoryTexts.JuneLetter, null, june: true);
        assert_true(sheet.IsOpen && sheet.TextFont == UiTheme.JuneFace, "June's letter is in her script face");
        sheet.Close();
        sheet.Read(StoryTexts.TallyBook, null, june: false, StoryTexts.TallySketchPage);
        assert_true(sheet.TextFont == UiTheme.TomFace, "Tom's tally book is in his pencil hand");
        GameSettings.Current.PlainText = true;
        sheet.Next();
        assert_true(sheet.TextFont == UiTheme.Serif, "with plain text on, the next page is in the serif");
        sheet.Close();
        GameSettings.Current.PlainText = plain;
        foreach (string[] text in new[] { StoryTexts.JuneLetter, StoryTexts.TallyBook, StoryTexts.TomLetter })
            assert_false(text.Any(page => page.Contains('(') || page.Contains("sketch")), "no stage directions in the read texts");
        tree.Root.RemoveChild(sheet);
        sheet.Free();
    }

    public void test_build_workers_hand_back_plain_data_only()
    {
        // Worker threads build these; an engine object made or released there can
        // be finalized or freed off the main thread (the startup abort).
        assert_false(typeof(GodotObject).IsAssignableFrom(typeof(TerrainBuilder.Grid)), "the terrain grid is plain data");
        assert_false(typeof(GodotObject).IsAssignableFrom(typeof(GrassPlanter.Chunk)), "grass chunks are plain data");
        var grid = new TerrainBuilder.Grid
        {
            positions = new[] { new Vector3(0, 0, 0), new Vector3(1, 0.2f, 0), new Vector3(0, 0.1f, 1), new Vector3(1, 0.3f, 1) },
            normals = new[] { Vector3.Up, Vector3.Up, Vector3.Up, Vector3.Up },
            uvs = new Vector2[4], colors = new Color[4], indices = new[] { 0, 1, 3, 0, 3, 2 },
        };
        using ArrayMesh mesh = TerrainBuilder.mesh_from(grid, null);
        using ConcavePolygonShape3D shape = TerrainBuilder.collision_from(grid);
        Vector3[] faces = shape.Data, drawn = mesh.GetFaces();
        assert_true(faces.Length == drawn.Length && faces.Zip(drawn).All(f => f.First.IsEqualApprox(f.Second)),
            "the collision is built on the main thread from the same triangles as the mesh");
        assert_true(Firepit.scatter_clear(new TerrainField(), new Vector3(TerrainField.TRAIL[3].X, 0, TerrainField.TRAIL[3].Y)) == false, "scattered stones stay off the trail");
    }

    public void test_story_props_offer_nothing_until_asked()
    {
        var tent = new Tent(new TerrainField());
        tent._add_tackle_tin();
        tent._add_strike();
        assert_eq((long)tent.tin_body.CollisionLayer, 0L, "the tin offers nothing until the story does");
        assert_eq((long)tent.strike_body.CollisionLayer, 0L, "nor does striking the tent");
        tent.offer_tin(true);
        assert_eq((long)tent.tin_body.CollisionLayer, 2L, "offered, it is on the interaction layer only");
        assert_eq(tent.tin_body.prompt_text, "Open", "one verb");
        assert_eq(tent.strike_body.prompt_text, "Strike", "one verb");
        // The tin stands on the ground sheet: its box bottom just above the floor.
        var box = tent.tackle_tin.GetNode<MeshInstance3D>("TinBox");
        float bottom = box.Mesh.GetAabb().Position.Y + box.Position.Y + tent.tackle_tin.Position.Y;
        assert_true(bottom >= 0.0f && bottom < 0.01f, $"the tin is grounded on the tent floor ({bottom * 1000:F1} mm)");
        tent.open_tin();
        assert_true(tent.tin_open && tent.tin_lid.Rotation.X > 1.5f, "opening swings the lid back");
        tent.Free();

        var kitchen = new CampKitchen(new TerrainField());
        kitchen._add_letter();
        assert_eq(kitchen.letter_body.prompt_text, "Read", "the letter's one verb");
        float under = kitchen.letter.GetNode<MeshInstance3D>("LowerLeaf").Mesh.GetAabb().Position.Y + kitchen.letter.GetNode<MeshInstance3D>("LowerLeaf").Position.Y;
        assert_true(Math.Abs(kitchen.letter.Position.Y + under - (float)CampKitchen.CLOTH_TOP) < 0.001f, "the letter lies on the tablecloth");
        kitchen.take_letter();
        assert_false(kitchen.letter.Visible || kitchen.letter_body.CollisionLayer != 0, "read, it is taken from the table");
        kitchen.Free();
    }
}
