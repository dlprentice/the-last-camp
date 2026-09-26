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

/// Source/resource contracts for close camp materials.
public partial class TestHeroMaterials : TestCase
{
    public void test_iron_uses_dedicated_material_shader()
    {
        ShaderMaterial material = PropMaterials.iron(new Color(0.12f, 0.13f, 0.14f));
        assert_true(material != null, "iron material builds");
        assert_true(material.Shader != null, "iron material has a shader");
        assert_eq(material.Shader.ResourcePath, "res://shaders/iron.gdshader", "camp hardware no longer uses the metallic-rock shortcut");
        assert_eq(material.GetShaderParameter("tint"), new Color(0.12f, 0.13f, 0.14f), "iron tint contract is preserved");
    }

    public void test_enamel_shader_exposes_age_and_chip_controls()
    {
        ShaderMaterial material = FieldKit.enamel(new Color(0.22f, 0.31f, 0.33f));
        assert_true(material != null && material.Shader != null, "enamel material builds");
        assert_eq(material.Shader.ResourcePath, "res://shaders/enamel.gdshader", "enamel shader path is stable");
        assert_true(material.GetShaderParameter("chip_amount").VariantType == Variant.Type.Float, "enamel shader exposes chip amount");
        assert_true(material.GetShaderParameter("age").VariantType == Variant.Type.Float, "enamel shader exposes age");
    }

    public void test_rope_uses_uv_aware_fibre_shader()
    {
        ShaderMaterial material = PropMaterials.rope();
        assert_true(material != null && material.Shader != null, "rope material builds");
        assert_eq(material.Shader.ResourcePath, "res://shaders/rope.gdshader", "rope no longer reuses triplanar canvas");
        assert_true(material.GetShaderParameter("lay_turns_per_metre").VariantType == Variant.Type.Float, "rope exposes physical-ish strand lay control");
    }

    public void test_shared_close_materials_compile_as_shader_resources()
    {
        foreach (Variant path in new Godot.Collections.Array { "res://shaders/enamel.gdshader", "res://shaders/iron.gdshader", "res://shaders/rope.gdshader", "res://shaders/wood_uv.gdshader", "res://shaders/canvas.gdshader" })
        {
            Shader shader = Content.Load<Shader>(path.AsString());
            assert_true(shader != null, G.format("%s loads as a Shader resource", path));
        }
    }
}
