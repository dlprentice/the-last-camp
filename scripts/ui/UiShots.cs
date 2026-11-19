using System;
using System.Threading.Tasks;
using Godot;
using LastCamp.Story;

namespace LastCamp;

/// `--ui-shots[=dir]`: walks every screen of the shell once in a single launch and
/// saves a still of each (default `res://local-data/ui-shots`): the title while
/// the world builds and live, its settings, the chapter card, play with the dot
/// and a subtitle, pause, settings, the journal's spreads in both hands, the
/// credits, the postcard, and the title again after returning to it. Then quits.
/// It runs on the title path, so it never uses the player's own files when run
/// through the offscreen wrapper (which isolates the user directory).
public partial class UiShots : Node
{
    private static int _pass;
    private string _dir = "";

    public UiShots()
    {
        Name = "UiShots";
        ProcessMode = ProcessModeEnum.Always;
    }

    public override async void _Ready()
    {
        _dir = Game.Instance.arg_value("ui-shots", "res://local-data/ui-shots");
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(_dir));
        _pass++;
        try
        {
            if (_pass == 1) await FirstPass();
            else
            {
                await Wait(3.0);
                await Shot("16_title_after_return");
                Game.Instance.quit_cleanly(0);
            }
        }
        catch (Exception error)
        {
            GD.PushError($"UI_SHOTS failed: {error}");
            Game.Instance.quit_cleanly(1);
        }
    }

    private async Task FirstPass()
    {
        var loading = GetParent().GetNode<LoadingScreen>("Loading");
        // Main adds this node once the world is ready, so the "building" still is
        // taken from the title's own first frame logged in TITLE_SHOWN; here the live view.
        await Wait(3.5);
        await Shot("02_title_live");
        loading._menu.OpenSettings();
        await Wait(0.6);
        await Shot("03_title_settings");
        loading._menu.SettingsPanel.Close();
        loading._menu.OpenCredits();
        await Wait(1.2);
        loading._menu.Credits?.Seek(0.3);
        await Wait(0.3);
        await Shot("04_title_credits");
        loading._menu.Credits?.End();
        await Wait(0.8);

        loading.Begin(true);
        await Wait(1.6);
        await Shot("05_chapter_card");
        await Wait(3.2);
        Hud hud = Game.Instance.hud;
        Player player = Game.Instance.player;
        Interactable wood = Game.Instance.camp.campsite.woodpile;
        Vector3 away = (new Vector3(0, 0, 0) - wood.GlobalPosition) with { Y = 0 };
        Vector3 stand = wood.GlobalPosition + away.Normalized() * 1.9f;
        stand.Y = (float)Game.Instance.camp.height_at(stand.X, stand.Z) + 0.1f;
        player.GlobalPosition = stand;
        player.Velocity = Vector3.Zero;
        await Wait(0.4);
        Vector3 direction = wood.GlobalPosition + Vector3.Up * 0.28f - player.eye_position();
        player.yaw = Math.Atan2(-direction.X, -direction.Z);
        player.pitch = Math.Atan2(direction.Y, new Vector2(direction.X, direction.Z).Length());
        GetTree().Root.FindChild("Voice", true, false)?.Call(VoicePlayer.MethodName.Enqueue, "m2", "");
        await Wait(1.5);
        await Shot("06_play_dot_subtitle");

        hud._toggle_pause();
        await Wait(0.6);
        await Shot("07_pause");
        hud._open_settings();
        await Wait(0.6);
        await Shot("08_pause_settings");
        hud._settings.Close();
        hud._toggle_pause();

        // A story far enough on for every part of the book to have something in it.
        StoryState story = Game.Instance.session.Story;
        story.Raise(StoryEvents.LogCarried);
        foreach (string line in new[] { "e1", "e2", "m1", "m2", "m3", "m4" }) story.LinesHeard.Add(line);
        story.AddItem("tally_book");
        story.Facts.Add(StoryEvents.TinOpened);
        story.AddItem("tom_letter");
        hud._open_journal(fromPause: false);
        await Wait(0.8);
        await Shot("09_journal");
        for (int i = 2; i <= 4; i++)
        {
            hud.Journal.Turn(1);
            await Wait(0.6);
            await Shot($"{8 + i:00}_journal_spread{i}");
        }
        GameSettings.Current.PlainText = true;
        hud.Journal.Open(story);
        await Wait(0.6);
        await Shot("13_journal_plain_text");
        GameSettings.Current.PlainText = false;
        hud._close_journal(false);
        await Wait(0.5);

        hud.ShowEnding(true);
        await Wait(1.5);
        await Shot("14_ending_credits");
        (hud._root.FindChild("Credits", true, false) as CreditsRoll)?.End();
        await Wait(2.5);
        await Shot("15_postcard");
        (hud._root.FindChild("Postcard", true, false) as Postcard)?.Heard();
        await Wait(0.5);
        await Shot("15b_postcard_home");
        GD.Print("UI_SHOTS first pass done; returning to the title");
        LoadingScreen.ReturnToTitle(GetTree());
    }

    /// The title's first still, taken while the world is still being built.
    public static async void ShotAfter(Node from, string name, double seconds)
    {
        if (_pass > 0) return;
        await from.ToSignal(from.GetTree().CreateTimer(seconds, true, false, true), SceneTreeTimer.SignalName.Timeout);
        if (!GodotObject.IsInstanceValid(from) || !from.IsInsideTree()) return;
        string dir = Game.Instance.arg_value("ui-shots", "res://local-data/ui-shots");
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
        await from.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = from.GetViewport().GetTexture().GetImage();
        Error err = image.SavePng(ProjectSettings.GlobalizePath($"{dir}/{name}.png"));
        GD.Print($"UI_SHOT {name} {(err == Error.Ok ? "saved" : err.ToString())}");
    }

    private async Task Wait(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds, true, false, true), SceneTreeTimer.SignalName.Timeout);

    private async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        string path = ProjectSettings.GlobalizePath($"{_dir}/{name}.png");
        Error err = image.SavePng(path);
        GD.Print($"UI_SHOT {name} {(err == Error.Ok ? "saved" : err.ToString())} {image.GetWidth()}x{image.GetHeight()}");
    }
}
