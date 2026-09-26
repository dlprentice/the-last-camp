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

/// A collider the player can use. `prompt_text` is shown on the HUD; `on_interact`
/// is invoked with the player node.
public partial class Interactable : StaticBody3D
{
    public string prompt_text = "";
    public Callable on_interact;

    public string prompt()
    {
        if (G.is_valid(on_interact) && (prompt_text.Length == 0) == false)
        {
            return prompt_text;
        }
        return prompt_text;
    }

    public void interact(Node player)
    {
        if (G.is_valid(on_interact))
        {
            on_interact.Call(player);
        }
    }
}
