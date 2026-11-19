using System;
using System.Collections.Generic;
using System.Linq;
using LastCamp.Story;

namespace LastCamp;

/// One line of June's list for the chapter: crossed out once done.
public readonly record struct JournalTask(string Text, bool Done);

/// One block of a journal page, in a face: print (the serif), June's hand or Tom's pencil.
public enum JournalFace { Print, Heading, Small, June, Tom }
public readonly record struct JournalBlock(JournalFace Face, string Text, bool Struck = false, bool NewPage = false);

/// What the journal shows for a story state. Pure data, so tests can ask it what
/// the book holds without building the book. The texts are docs/script.md's.
public static class JournalContent
{
    /// Entry headings, as June wrote them at the top of each page.
    public static readonly IReadOnlyDictionary<string, string> EntryTitles = new Dictionary<string, string>
    {
        ["e1"] = "For when you find the table",
        ["e2"] = "For when you get three",
        ["e3"] = "For the first fire",
        ["e4"] = "For the old ring",
        ["e5"] = "For the lookout",
        ["e6"] = "For a bad night",
        ["e7"] = "The postcard",
    };

    public static readonly string[] TallyPages =
    {
        "Herons, 2014\n\nJune 14  ||\nJune 21  ||\nJune 28  |||\nJuly 2  ||\nJuly 9  ||\nJuly 16  |\nJuly 23  ||\nJuly 30  |\nAug 6  |\nAug 13  |\nAug 19  |",
        "July 9\n\n2 herons, the old one and a young one.\n\n\n                        steps: 6 of 20",
        "July 30\n\nOld one only.\n\n\n                        steps: 14 of 20\n\nfrom the old ring, up the south slope.\nfirst one is the notch.",
        "August 19\n\n20th step goes under the bench.\nShe'll find it when she sits.",
    };

    public const string TomLetter =
        "June,\n\nForty years. I wanted to give you something you couldn't carry home, so I made you " +
        "a way up. Twenty steps, one for every two years, and a bench at the top facing the " +
        "pond, the right height for you.\n\nI know your knees. Take them slowly. The last step is the one under the bench, and " +
        "when you sit, this will be right under your feet.\n\nI've loved every one of these summers. You were the reason for all of them.\n\nTom";

    public static readonly string[] ChapterNumbers = { "Chapter One", "Chapter Two", "Epilogue" };

    public static string ChapterNumber(int index) => index >= 0 && index < ChapterNumbers.Length ? ChapterNumbers[index] : "";

    /// The chapter's list so far: every required task of its bands up to the
    /// current one, in order, crossed out where done. Later bands stay unwritten.
    public static List<JournalTask> Tasks(StoryState story)
    {
        var tasks = new List<JournalTask>();
        Chapter chapter = story.Chapter;
        for (int b = 0; b <= story.BandIndex && b < chapter.Bands.Length; b++)
        {
            Band band = chapter.Bands[b];
            foreach (Objective objective in story.Required(band))
            {
                bool done = story.ProgressOf(chapter, band, objective) >= objective.Count;
                tasks.Add(new JournalTask(objective.Journal, done || story.Finished));
            }
        }
        return tasks;
    }

    /// The line June's list ends with once the band's tasks are done: how to move on.
    public static string NextStep(StoryState story)
    {
        if (story.Finished) return "The camp is left the way you found it.";
        if (!story.BandDone) return "";
        EndAct? act = story.EndActs.FirstOrDefault();
        string how = act?.Event switch
        {
            StoryEvents.TurnedIn => "turn in at the tent",
            StoryEvents.RestBedroll => "rest at the tent",
            StoryEvents.RestFire => "rest by the fire",
            StoryEvents.SatPier => "sit at the end of the pier",
            StoryEvents.SatLookout => "sit on his bench",
            StoryEvents.StepLifted => "see what he left",
            StoryEvents.WalkedOut => "walk out past the trailhead post",
            _ => "move on",
        };
        return $"When you are ready, {how}.";
    }

    /// June's lines heard to the end, of one kind, in the script's order.
    public static List<string> Heard(StoryState story, LineKind kind) =>
        StoryScript.Lines.Where(l => l.Kind == kind && story.LinesHeard.Contains(l.Id)).Select(l => l.Id).ToList();

    public static bool TallyOpen(StoryState story) =>
        story.Facts.Contains(StoryEvents.TinOpened) || story.Items.Contains("tally_book");

    public static bool LetterFound(StoryState story) => story.Items.Contains("tom_letter");

    public static string LineText(VoiceScript voice, string line, string variant) =>
        string.Join("\n\n", voice.For(line, variant).Select(p => p.Text));

    /// The left page's stream: the chapter heading, June's list, then the entries heard.
    public static List<JournalBlock> LeftPages(StoryState story, VoiceScript voice)
    {
        var blocks = new List<JournalBlock>
        {
            new(JournalFace.Small, ChapterNumber(story.ChapterIndex)),
            new(JournalFace.Heading, story.Chapter.Title),
        };
        foreach (JournalTask task in Tasks(story)) blocks.Add(new(JournalFace.June, task.Text, task.Done));
        string next = NextStep(story);
        if (next.Length > 0) blocks.Add(new(JournalFace.June, next));
        foreach (string id in Heard(story, LineKind.Entry))
        {
            string text = LineText(voice, id, id == "e7" ? story.StormOutcome : "");
            if (text.Length == 0) continue;
            blocks.Add(new(JournalFace.Heading, EntryTitles.TryGetValue(id, out string? title) ? title : id, NewPage: true));
            foreach (string paragraph in text.Split("\n\n")) blocks.Add(new(JournalFace.Print, paragraph));
        }
        return blocks;
    }

    /// The right page's stream: June's margin notes, then Tom's tally book once the
    /// tin is open, then his letter once it is found.
    public static List<JournalBlock> RightPages(StoryState story, VoiceScript voice)
    {
        var blocks = new List<JournalBlock> { new(JournalFace.Small, "In the margins") };
        List<string> notes = Heard(story, LineKind.MarginNote);
        if (notes.Count == 0) blocks.Add(new(JournalFace.Small, "Nothing yet."));
        foreach (string id in notes)
        {
            string text = LineText(voice, id, "");
            if (text.Length > 0) blocks.Add(new(JournalFace.June, text));
        }
        if (TallyOpen(story))
        {
            for (int i = 0; i < TallyPages.Length; i++)
            {
                blocks.Add(new(JournalFace.Small, $"Tom's tally book, page {i + 1}", NewPage: true));
                blocks.Add(new(JournalFace.Tom, TallyPages[i]));
            }
        }
        if (LetterFound(story))
        {
            blocks.Add(new(JournalFace.Small, "From the tin under the twentieth step, August 2014", NewPage: true));
            blocks.Add(new(JournalFace.Tom, TomLetter));
        }
        return blocks;
    }

    /// Lays blocks out on pages of `height`: a block that does not fit starts the
    /// next page, and a block marked `NewPage` always does. `measure` gives a
    /// block's height (with its spacing) at the page width.
    public static List<List<JournalBlock>> Paginate(IReadOnlyList<JournalBlock> blocks, Func<JournalBlock, float> measure, float height)
    {
        var pages = new List<List<JournalBlock>> { new() };
        float used = 0;
        foreach (JournalBlock block in blocks)
        {
            float h = measure(block);
            List<JournalBlock> page = pages[^1];
            if (page.Count > 0 && (block.NewPage || used + h > height))
            {
                pages.Add(new List<JournalBlock>());
                used = 0;
            }
            pages[^1].Add(block);
            used += h;
        }
        return pages;
    }
}
