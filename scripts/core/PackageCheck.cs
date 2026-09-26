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

/// Checks the executable's embedded credits and writes its own engine notices.
/// Invoked through -- --package-check --notices=<fresh file>; release templates
/// do not support the editor-only --script command line option.
public partial class PackageCheck
{
    public static long run(string notice_path)
    {
        Godot.Collections.Array surfaces = Cinematic._credit_rows("res://textures/SOURCES.md", 1, 2, 4);
        Godot.Collections.Array models = Cinematic._credit_rows("res://models/SOURCES.md", 1, 2, 4);
        Godot.Collections.Array sounds = Cinematic._audio_credit_rows();
        if ((long)surfaces.Count != 9 || (long)models.Count != 35 || (long)sounds.Count != 6)
        {
            G.push_error(G.format("Export omitted complete runtime credits: %s/%s/%s", new Godot.Collections.Array { (long)surfaces.Count, (long)models.Count, (long)sounds.Count }));
            return 1;
        }
        foreach (Variant path in new Godot.Collections.Array { "res://LICENSE", "res://THIRD_PARTY_NOTICES.md" })
        {
            if (!FileAccess.FileExists(path.AsString()))
            {
                G.push_error(G.op("+", "Export omitted ", path));
                return 1;
            }
        }
        foreach (Variant path2 in new Godot.Collections.Array { "res://AGENTS.md", "res://docs/development.md" })
        {
            if (FileAccess.FileExists(path2.AsString()))
            {
                G.push_error(G.op("+", "Export contains development-only notes: ", path2));
                return 1;
            }
        }
        if ((notice_path.Length == 0) || FileAccess.FileExists(notice_path))
        {
            G.push_error("Provide --notices with a fresh output filename");
            return 1;
        }
        FileAccess file = FileAccess.Open(notice_path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            G.push_error("Cannot write exported engine notices");
            return 1;
        }
        file.StoreString(G.op("+", G.op("+", "Godot Engine ", Engine.GetVersionInfo()["string"]), "\nhttps://godotengine.org/license/\n\n").AsString());
        file.StoreString(Engine.GetLicenseText() + "\n\nThird-party copyrights and component licenses\n\n");
        file.StoreString(Json.Stringify(Engine.GetCopyrightInfo(), "\t") + "\n\nLicense texts\n\n");
        file.StoreString(Json.Stringify(Engine.GetLicenseInfo(), "\t") + "\n");
        file.Close();
        G.print("EXPORT_CHECK result=PASS surfaces=9 models=35 recordings=6 notices=complete");
        return 0;
    }
}
