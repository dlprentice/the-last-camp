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

/// Full-screen grade drawn after the 3D scene has been tonemapped and before
/// the HUD (see shaders/post/grade.gdshader). A fragment pass over the back
/// buffer costs a fraction of a millisecond and needs no compute dispatch.
public partial class PostStack : CanvasLayer
{
    public const long LAYER = 5;

    public ShaderMaterial material;
    public ColorRect rect;

    public override void _Ready()
    {
        Name = "PostStack";
        Layer = (int)LAYER;
        ProcessMode = Node.ProcessModeEnum.Always;
        material = new ShaderMaterial();
        material.Shader = Content.Load<Shader>("res://shaders/post/grade.gdshader");
        rect = new ColorRect();
        rect.Name = "Grade";
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        rect.MouseFilter = Control.MouseFilterEnum.Ignore;
        rect.Material = material;
        AddChild(rect);
    }

    public void set_param(StringName param, Variant value)
    {
        material.SetShaderParameter(param, value);
    }

    public void set_enabled(bool enabled)
    {
        rect.Visible = enabled;
    }
}
