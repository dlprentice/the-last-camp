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

/// Leaving photo mode or the pause menu returns to play; the title card must
/// not replay over the game each time (it did whenever it had faded out).
public partial class TestHud : TestCase
{
    public void test_title_card_plays_only_once()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        Hud hud = new Hud();
        tree.Root.AddChild(hud);
        Game.Mode previous = Game.Instance.mode;
        Game.Instance.mode = Game.Mode.INTRO;
        Game.Instance.mode = Game.Mode.PLAY;
        assert_true(hud.title_played, "first entry into play starts the title card");
        long running = (long)tree.GetProcessedTweens().Count;
        Color _t1 = hud._title.Modulate;
        _t1.A = 0.0f;
        hud._title.Modulate = _t1;
        Color _t2 = hud._hints.Modulate;
        _t2.A = 0.0f;
        hud._hints.Modulate = _t2;
        Game.Instance.mode = Game.Mode.PHOTO;
        Game.Instance.mode = Game.Mode.PLAY;
        assert_eq((long)tree.GetProcessedTweens().Count, running, "no fresh title tween after leaving photo mode");
        Game.Instance.mode = Game.Mode.PAUSED;
        Game.Instance.mode = Game.Mode.PLAY;
        assert_eq((long)tree.GetProcessedTweens().Count, running, "no fresh title tween after the menu");
        assert_eq(hud._title.Modulate.A, 0.0, "title stays hidden when play resumes");
        Game.Instance.mode = previous;
        hud.Free();
    }
}
