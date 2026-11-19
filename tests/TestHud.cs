#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LastCamp;
using LastCamp.Story;

namespace LastCamp.Tests;

/// The shell: settings persist, tool runs skip the title, the journal shows the
/// right list for a story state, the dot says one verb, the credits read the
/// source tables, and nothing in the interface draws with Godot's default theme.
public partial class TestHud : TestCase
{
    private static IEnumerable<Control> Controls(Node node)
    {
        if (node is Control c) yield return c;
        foreach (Node child in node.GetChildren())
            foreach (Control d in Controls(child))
                yield return d;
    }

    private void AssertKit(Node root, string what)
    {
        List<Control> controls = Controls(root).ToList();
        assert_gt(controls.Count, 0, $"{what} has controls");
        List<string> off = controls.Where(c => !ReferenceEquals(UiTheme.EffectiveTheme(c), UiTheme.Kit))
            .Select(c => $"{c.GetPath()} ({c.GetClass()})").ToList();
        assert_eq(off.Count, 0, $"every control in {what} draws with the kit's theme: {string.Join(", ", off.Take(5))}");
    }

    public void test_settings_round_trip()
    {
        string path = $"user://settings-test-{Guid.NewGuid():N}.cfg";
        var s = new GameSettings
        {
            Preset = "medium", Fov = 84, HeadBob = false, Master = 0.6, Ambience = 0.25, Effects = 0.0, Voice = 0.9,
            Subtitles = false, PlainText = true, MouseSensitivity = 1.75, InvertY = true,
        };
        assert_eq((long)s.Save(path), (long)Error.Ok, "settings save");
        GameSettings back = GameSettings.Load(path);
        assert_eq(back.Preset, "medium", "preset persists");
        assert_near(back.Fov, 84, 1e-9, "field of view persists");
        assert_false(back.HeadBob, "head-bob persists");
        assert_near(back.Master, 0.6, 1e-9, "master volume persists");
        assert_near(back.Ambience, 0.25, 1e-9, "ambience volume persists");
        assert_near(back.Effects, 0.0, 1e-9, "effects volume persists");
        assert_near(back.Voice, 0.9, 1e-9, "voice volume persists");
        assert_false(back.Subtitles, "subtitles setting persists");
        assert_true(back.PlainText, "plain-text hands persist");
        assert_near(back.MouseSensitivity, 1.75, 1e-9, "mouse sensitivity persists");
        assert_true(back.InvertY, "invert Y persists");
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));

        GameSettings missing = GameSettings.Load($"user://no-such-settings-{Guid.NewGuid():N}.cfg");
        assert_eq(missing.Preset, "auto", "a missing file gives the defaults");
        assert_true(missing.Subtitles && missing.HeadBob && !missing.InvertY, "defaults: subtitles and head-bob on, look not inverted");

        var cfg = new ConfigFile();
        cfg.SetValue("graphics", "fov", 400.0);
        cfg.SetValue("graphics", "preset", "cinema");
        cfg.SetValue("audio", "master", -3.0);
        string bad = $"user://settings-bad-{Guid.NewGuid():N}.cfg";
        cfg.Save(bad);
        GameSettings clamped = GameSettings.Load(bad);
        assert_near(clamped.Fov, GameSettings.FovMax, 1e-9, "an out-of-range field of view is clamped");
        assert_eq(clamped.Preset, "auto", "an unknown preset falls back to automatic");
        assert_near(clamped.Master, 0, 1e-9, "a negative volume is clamped");
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(bad));
    }

    public void test_title_bypassed_for_every_tool_flag()
    {
        string[] tools = { "session-check", "benchmark", "profile", "traverse", "cinematic", "capture", "review-route", "review-assets", "package-check" };
        foreach (string flag in tools)
        {
            assert_false(LoadingScreen.ShowsTitle(Game.parse_user_args(new List<string> { "--" + flag })), $"--{flag} skips the title");
            assert_false(LoadingScreen.ShowsTitle(Game.parse_user_args(new List<string> { $"--{flag}=res://local-data/x", "--quality=high" })), $"--{flag}=value skips the title");
        }
        assert_true(LoadingScreen.ShowsTitle(Game.parse_user_args(new List<string>())), "a plain launch shows the title");
        assert_true(LoadingScreen.ShowsTitle(Game.parse_user_args(new List<string> { "--quality=low", "--intro" })), "player options still show the title");
    }

    public void test_journal_lists_tasks_for_story_state()
    {
        var story = new StoryState { RequireUnbuilt = true };
        List<JournalTask> tasks = JournalContent.Tasks(story);
        assert_eq(string.Join("|", tasks.Select(t => t.Text)), string.Join("|", StoryScript.Chapters[0].Bands[0].Objectives.Select(o => o.Journal)),
            "a new story lists the first evening's three tasks");
        assert_false(tasks.Any(t => t.Done), "nothing is crossed out at the start");

        story.Raise(StoryEvents.LetterRead);
        story.Raise(StoryEvents.LogCarried);
        tasks = JournalContent.Tasks(story);
        assert_true(tasks[0].Done, "reading the letter crosses it out");
        assert_false(tasks[1].Done, "one log of three is not yet done");
        story.Raise(StoryEvents.LogCarried);
        story.Raise(StoryEvents.LogCarried);
        story.Raise(StoryEvents.StoneSkips + ":3");
        assert_true(JournalContent.Tasks(story).All(t => t.Done), "the evening's list is all crossed out");
        assert_true(JournalContent.NextStep(story).Contains("pier"), "a done band says how to go on");

        story.Advance(StoryEvents.SatPier);
        tasks = JournalContent.Tasks(story);
        assert_eq((long)tasks.Count, 5L, "night keeps the evening's crossed-out tasks and adds its own");
        assert_true(tasks.Take(3).All(t => t.Done) && tasks.Skip(3).All(t => !t.Done), "evening done, night open");
        assert_eq(tasks[3].Text, StoryScript.Chapters[0].Bands[1].Objectives[0].Journal, "the night's first task follows");

        story.Raise(StoryEvents.FireLit);
        story.Raise(StoryEvents.LanternLit);
        story.Advance(StoryEvents.TurnedIn);
        tasks = JournalContent.Tasks(story);
        assert_eq(string.Join("|", tasks.Select(t => t.Text)), string.Join("|", StoryScript.Chapters[1].Bands[0].Objectives.Select(o => o.Journal)),
            "a new chapter starts a new list");

        assert_false(JournalContent.TallyOpen(story), "no tally book before the tin");
        story.Raise(StoryEvents.PlaceOldRing);
        story.Raise(StoryEvents.TinOpened);
        assert_true(JournalContent.TallyOpen(story), "the tin's tally book is in the journal once the tin is open");
        assert_false(JournalContent.LetterFound(story), "Tom's letter only once found");
        story.AddItem("tom_letter");
        assert_true(JournalContent.LetterFound(story), "Tom's letter after the step");

        // The game's own rule: unbuilt places are not asked for.
        var game = new StoryState();
        assert_eq((long)JournalContent.Tasks(game).Count, (long)game.Required(game.Band).Count(),
            "the game's journal leaves out tasks nothing can do yet");

        var voice = VoiceScript.Parse("{\"paragraphs\":[{\"id\":\"e1-p1\",\"line\":\"e1\",\"text\":\"Well, you found the table.\"}," +
            "{\"id\":\"m2-p1\",\"line\":\"m2\",\"text\":\"Take them from the top.\"}]}");
        story.LinesHeard.Add("e1");
        story.LinesHeard.Add("m2");
        List<JournalBlock> left = JournalContent.LeftPages(story, voice);
        List<JournalBlock> right = JournalContent.RightPages(story, voice);
        assert_true(left.Any(b => b.Text == "Well, you found the table."), "an entry heard is written on the left page");
        assert_true(right.Any(b => b.Face == JournalFace.June && b.Text == "Take them from the top."), "a margin note heard is in June's hand on the right");
        assert_eq((long)right.Count(b => b.Face == JournalFace.Tom), 5L, "the tally book's four pages and the letter, in Tom's hand");

        List<List<JournalBlock>> pages = JournalContent.Paginate(right, _ => 100, 450);
        assert_true(pages.All(p => p.Count > 0), "no empty page");
        assert_true(pages.Count >= 6, "each tally page and the letter start a fresh page");
    }

    public void test_verbs_are_one_word()
    {
        assert_eq(Hud.Verb("Take a log"), "Take", "woodpile");
        assert_eq(Hud.Verb("Take a skipping stone"), "Take", "stones");
        assert_eq(Hud.Verb("Add a log"), "Feed", "the fire with a log");
        assert_eq(Hud.Verb("Rest by the fire"), "Rest", "the fire empty-handed");
        assert_eq(Hud.Verb("Turn in for the night"), "Rest", "the tent at night");
        assert_eq(Hud.Verb("Light lantern"), "Light", "the lantern");
        assert_eq(Hud.Verb("Read the letter"), "Read", "a letter");
        assert_eq(Hud.Verb(""), "", "nothing aimed at");
    }

    public void test_credits_read_the_source_tables()
    {
        List<string> rows = CreditsData.TableRows("| Set | Asset | Creator credit | Provider | License |\n|---|---|---|---|---|\n" +
            "| `grass` | [leafy_grass](https://example.org) | Charlotte Baglioni | Poly Haven | [CC0 1.0](https://example.org/cc0) |\n");
        assert_eq((long)rows.Count, 1L, "one row");
        assert_eq(rows[0], "leafy_grass · Charlotte Baglioni · Poly Haven · CC0 1.0", "links and code marks are stripped");
        List<CreditSection> credits = CreditsData.Build();
        CreditSection models = credits.FirstOrDefault(s => s.Heading == "Photoscanned models");
        assert_true(models != null && models.Lines.Count >= 30, "every credited model is in the roll");
        CreditSection materials = credits.FirstOrDefault(s => s.Heading == "Materials");
        assert_true(materials != null && materials.Lines.Count == 9, "the nine material sets are in the roll");
        CreditSection fonts = credits.FirstOrDefault(s => s.Heading == "Fonts");
        assert_true(fonts != null && fonts.Lines.Count == 3, "the three typefaces are in the roll");
        CreditSection sound = credits.FirstOrDefault(s => s.Heading == "Sound");
        assert_true(sound != null && sound.Lines.Any(l => l.StartsWith("Interface · ")) && sound.Lines.Any(l => l.StartsWith("Footsteps · ")),
            "the manifest's sounds are listed by group");
    }

    public void test_no_control_uses_the_default_theme()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        GameSettings saved = GameSettings.Current;
        Hud hud = new Hud();
        tree.Root.AddChild(hud);
        AssertKit(hud, "the HUD");
        // The journal with content, the chapter card, settings, the ending.
        var story = new StoryState { RequireUnbuilt = true };
        story.LinesHeard.Add("e1");
        story.LinesHeard.Add("m1");
        story.AddItem("tally_book");
        story.AddItem("tom_letter");
        hud.Journal.Open(story);
        hud.Journal.Turn(1);
        hud.ShowChapterCard(0, "Arrival", false);
        hud._settings.Open();
        AssertKit(hud, "the HUD with the journal, card and settings open");
        hud.ShowEnding(true);
        AssertKit(hud, "the HUD with the credits");
        var postcard = new Postcard(false);
        hud._root.AddChild(postcard);
        AssertKit(hud, "the HUD with the postcard");
        hud.Free();

        var loading = new LoadingScreen();
        tree.Root.AddChild(loading);
        AssertKit(loading, "the title");
        loading._menu.OpenSettings();
        loading._menu.OpenCredits();
        AssertKit(loading, "the title with settings and credits");
        loading.Free();
        GameSettings.Current = saved;

        var voice = new VoicePlayer();
        tree.Root.AddChild(voice);
        AssertKit(voice, "the subtitles");
        voice.Free();
    }

    public void test_command_line_preset_wins_over_settings()
    {
        var args = Game.parse_user_args(new List<string> { "--quality=medium" });
        assert_true(GameSettings.PresetFromCommandLine(args), "--quality is a command-line preset");
        assert_false(GameSettings.PresetFromCommandLine(Game.parse_user_args(new List<string>())), "a plain launch has none");
        Quality quality = Quality.Instance;
        QualityPreset.Tier tier = quality.current.tier;
        bool wasExplicit = quality.@explicit;
        var saved = new GameSettings { Preset = tier == QualityPreset.Tier.ULTRA ? "low" : "ultra" };
        saved.ApplyPreset(quality, args);
        assert_eq((long)quality.current.tier, (long)tier, "the saved preset does not replace the command line's");
        assert_true(quality.@explicit, "auto-tune stays off when the command line gives a preset");
        new GameSettings { Preset = "auto" }.ApplyPreset(quality, args);
        assert_true(quality.@explicit, "an automatic saved preset does not turn auto-tune back on");
        quality.@explicit = wasExplicit;
    }

    public void test_longest_texts_fit_a_page()
    {
        GameSettings saved = GameSettings.Current;
        VoiceScript voice = VoiceScript.Load();
        string longest = StoryScript.Lines.Where(l => l.Kind == LineKind.Entry)
            .Select(l => JournalContent.LineText(voice, l.Id, l.Id == "e7" ? "went_out" : ""))
            .OrderByDescending(t => t.Length).First();
        assert_gt(longest.Length, 200, "the voice script's entries are loaded");
        foreach (bool plain in new[] { false, true })
        {
            GameSettings.Current = new GameSettings { PlainText = plain };
            string how = plain ? "in plain text" : "in the hands";
            float letter = JournalBook.Measure(new JournalBlock(JournalFace.Tom, JournalContent.TomLetter));
            assert_lt(letter, JournalBook.ContentHeight, $"Tom's letter fits one page {how} ({letter:0} px)");
            float entry = JournalBook.Measure(new JournalBlock(JournalFace.June, longest));
            assert_lt(entry, JournalBook.ContentHeight, $"the longest entry fits one page in June's hand {how} ({entry:0} px)");
            foreach (string page in JournalContent.TallyPages)
                assert_lt(JournalBook.Measure(new JournalBlock(JournalFace.Tom, page)), JournalBook.ContentHeight, $"each tally page fits {how}");
        }
        GameSettings.Current = saved;
        float one = JournalBook.Measure(new JournalBlock(JournalFace.Print, "One line."));
        float two = JournalBook.Measure(new JournalBlock(JournalFace.Print, "One line.\nTwo lines."));
        assert_gt(two - one, UiTheme.Serif.GetHeight(UiTheme.BODY) + 1, "the measure counts the kit's line spacing between lines");
    }

    public void test_play_shows_no_notice_text()
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree;
        Hud hud = new Hud();
        tree.Root.AddChild(hud);
        hud.notify("J opens your camp journal.");
        assert_false(Controls(hud).OfType<Label>().Any(l => l.Text.Contains("journal")), "a notice puts no text on the play screen");
        assert_true(Controls(hud).OfType<KitButton>().Any(b => b.Text == "Return to camp"), "the pause menu offers Return to camp");
        hud.Free();
    }

    public void test_kit_is_paper_and_ink()
    {
        assert_eq(UiTheme.PAPER.ToHtml(false), "efe7d6", "paper");
        assert_eq(UiTheme.INK.ToHtml(false), "2a2622", "ink");
        assert_eq(UiTheme.RUST.ToHtml(false), "9a4a2a", "rust");
        assert_eq(UiTheme.FADED.ToHtml(false), "6e655a", "faded ink");
        assert_true(UiTheme.Serif is FontFile, "Noto Serif is the bundled font, not a system font");
        assert_true(UiTheme.JuneFace is FontFile && UiTheme.TomFace is FontFile, "both hands are bundled");
        assert_eq(UiTheme.Kit.DefaultFont, UiTheme.Serif, "the kit's default font is the serif");
        assert_eq((long)UiTheme.Kit.DefaultFontSize, 22L, "body size 22 at 1080p");
        Image paper = UiTheme.PaperImage(64, 64, 1);
        assert_lt(paper.GetPixel(0, 0).A, 0.05, "the panel's edge fades out");
        assert_gt(paper.GetPixel(32, 32).A, 0.99, "the panel's middle is solid paper");
    }
}
