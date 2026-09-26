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

/// Runs during child _ready(), before Main._ready() calls Camp.build(). Replaces
/// Camp's field with a subclass so every later system receives one consistent
/// enhanced surface; no visual-only displacement or duplicate collision exists.
///
/// Weather is built by the World sibling before Camp._ready(), so its rain
/// occlusion texture has already sampled the base TerrainField. Rebind that one
/// derived artifact here as well; otherwise raised shoulders could receive rain
/// through terrain even though mesh/collision/foliage all use the enhanced field.
/// The replacement image is generated on a worker while the normal loading
/// stages run, then uploaded on the main thread when ready.
public partial class TerrainFieldDirector : Node
{
    public const long RAIN_HEIGHT_RESOLUTION = 512;
    public const double RAIN_HEIGHT_EXTENT = 128.0;

    public GodotThread _rain_thread;
    public ShaderMaterial _rain_material;

    public override void _Ready()
    {
        Camp camp = GetParent().GetNodeOrNull<Camp>("Camp");
        if (camp == null)
        {
            G.push_error("TerrainFieldDirector could not find Camp");
            return;
        }
        TerrainFieldEnhanced field = new TerrainFieldEnhanced();
        camp.field = field;
        _begin_weather_rain_sync(field);
    }

    public void _begin_weather_rain_sync(TerrainField field)
    {
        WorldController world = GetParent().GetNodeOrNull<WorldController>("World");
        if (world == null || world.weather == null || world.weather._rain_mesh == null)
        {
            return;
        }
        _rain_material = world.weather._rain_mesh.MaterialOverride as ShaderMaterial;
        if (_rain_material == null)
        {
            return;
        }
        _rain_thread = new GodotThread();
        Error error = _rain_thread.Start(Callable.From(() => _build_rain_height_image(field)));
        if (error != Error.Ok)
        {
            G.push_warning(G.format("Could not start enhanced rain-height bake: %s", error_string((long)error)));
            _rain_thread = null;
            return;
        }
        SetProcess(true);
    }

    public override void _Process(double _delta)
    {
        if (_rain_thread == null)
        {
            SetProcess(false);
            return;
        }
        if (_rain_thread.IsAlive())
        {
            return;
        }
        Image image = _rain_thread.WaitToFinish().As<Image>();
        _rain_thread = null;
        if (GodotObject.IsInstanceValid(_rain_material) && image != null && !image.IsEmpty())
        {
            _rain_material.SetShaderParameter("ground_height", ImageTexture.CreateFromImage(image));
        }
        SetProcess(false);
    }

    public static Image _build_rain_height_image(TerrainField field)
    {
        Image heights = Image.CreateEmpty((int)RAIN_HEIGHT_RESOLUTION, (int)RAIN_HEIGHT_RESOLUTION, false, Image.Format.Rf);
        double span = RAIN_HEIGHT_EXTENT * 2.0;
        for (long y = 0; y < RAIN_HEIGHT_RESOLUTION; y++)
        {
            for (long x = 0; x < RAIN_HEIGHT_RESOLUTION; x++)
            {
                Vector2 pos = new Vector2(x, y) / (float)(double)(RAIN_HEIGHT_RESOLUTION - 1) * (float)span - Vector2.One * (float)RAIN_HEIGHT_EXTENT;
                heights.SetPixel((int)x, (int)y, new Color((float)field.height(pos.X, pos.Y), 0.0f, 0.0f));
            }
        }
        return heights;
    }

    public override void _ExitTree()
    {
        if (_rain_thread != null && _rain_thread.IsStarted())
        {
            _rain_thread.WaitToFinish();
        }
        _rain_thread = null;
    }
}
