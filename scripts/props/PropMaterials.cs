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

/// Material factory for the camp's built props. Every prop shader takes the
/// same baked PBR sets (textures/<set>_albedo|normal|orm.png) plus the shared
/// noise texture; these helpers keep the binding in one place.
public partial class PropMaterials
{
    public const string NOISE = "res://textures/noise_rgba.png";

    public static ShaderMaterial triplanar(string set_name, Color? tint_opt = null, double tile = 0.55, double moss = 0.0, double metallic = 0.0)
    {
        Color tint = tint_opt ?? Colors.White;
        /// World-space triplanar PBR (rocks, logs, canvas): no UVs needed.
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>("res://shaders/prop.gdshader");
        Camp.bind_prop_pbr(mat, set_name);
        Camp.bind_texture(mat, "noise_tex", NOISE);
        mat.SetShaderParameter("tint", tint);
        mat.SetShaderParameter("tile", tile);
        mat.SetShaderParameter("moss_amount", moss);
        mat.SetShaderParameter("metallic", metallic);
        return mat;
    }

    public static ShaderMaterial wood(Color? tint_opt = null, double weathering = 0.3, double algae = 0.6, double roughness_scale = 1.0, string set_name = "wood")
    {
        Color tint = tint_opt ?? Colors.White;
        /// UV-mapped wood whose grain follows the piece (planks, piles, hulls).
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>("res://shaders/wood_uv.gdshader");
        Camp.bind_prop_pbr(mat, set_name);
        Camp.bind_texture(mat, "noise_tex", NOISE);
        mat.SetShaderParameter("tint", tint);
        mat.SetShaderParameter("weathering", weathering);
        mat.SetShaderParameter("algae_amount", algae);
        mat.SetShaderParameter("roughness_scale", roughness_scale);
        return mat;
    }

    public static ShaderMaterial bark(string set_name = "bark_oak", double moss = 0.25)
    {
        /// Bark for felled timber (the tree bark shader without wind or tint).
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>("res://shaders/bark.gdshader");
        Camp.bind_prop_pbr(mat, set_name);
        Camp.bind_texture(mat, "noise_tex", NOISE);
        mat.SetShaderParameter("moss_amount", moss);
        mat.SetShaderParameter("use_instance_custom", false);
        mat.SetShaderParameter("wind_response", 0.0);
        return mat;
    }

    public static ShaderMaterial iron(Color? tint_opt = null)
    {
        Color tint = tint_opt ?? new Color(0.16f, 0.15f, 0.14f);
        /// Dull blackened iron / weathered steel. Hardware now has its own material
        /// response rather than borrowing a rock photoscan and turning it metallic.
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>("res://shaders/iron.gdshader");
        Camp.bind_texture(mat, "noise_tex", NOISE);
        mat.SetShaderParameter("tint", tint);
        return mat;
    }

    public static ShaderMaterial rope()
    {
        /// Natural-fibre rope. Procedural tube geometry carries U around the cord and V
        /// along arc length, so the material can render actual twisted strands.
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = Content.Load<Shader>("res://shaders/rope.gdshader");
        mat.SetShaderParameter("lay_turns_per_metre", 5.4);
        mat.SetShaderParameter("strand_count", 3.0);
        mat.SetShaderParameter("groove_depth", 0.18);
        return mat;
    }
}
