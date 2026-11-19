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
        // The worker fills plain floats; the Image and texture are made on the main thread.
        _rain_heights = null;
        _rain_thread = new GodotThread();
        Error error = _rain_thread.Start(Callable.From(() => { _rain_heights = _rain_height_values(field); }));
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
        _rain_thread.WaitToFinish();
        _rain_thread = null;
        using Image image = _rain_heights != null ? _rain_height_image(_rain_heights) : null;
        _rain_heights = null;
        if (GodotObject.IsInstanceValid(_rain_material) && image != null && !image.IsEmpty())
        {
            ImageTexture ground_height = ImageTexture.CreateFromImage(image);
            _rain_material.SetShaderParameter(NativeArgs.Name("ground_height"), ground_height);
            GC.KeepAlive(ground_height);
        }
        SetProcess(false);
    }

    private float[] _rain_heights;

    public static Image _build_rain_height_image(TerrainField field) => _rain_height_image(_rain_height_values(field));

    /// The rain-height samples, row by row: plain data, safe on a worker thread.
    public static float[] _rain_height_values(TerrainField field)
    {
        int n = (int)RAIN_HEIGHT_RESOLUTION;
        var values = new float[n * n];
        double span = RAIN_HEIGHT_EXTENT * 2.0;
        for (long y = 0; y < RAIN_HEIGHT_RESOLUTION; y++)
        {
            for (long x = 0; x < RAIN_HEIGHT_RESOLUTION; x++)
            {
                Vector2 pos = new Vector2(x, y) / (float)(double)(RAIN_HEIGHT_RESOLUTION - 1) * (float)span - Vector2.One * (float)RAIN_HEIGHT_EXTENT;
                values[y * n + x] = (float)field.height(pos.X, pos.Y);
            }
        }
        return values;
    }

    /// The samples as a single-channel float image (main thread).
    public static Image _rain_height_image(float[] values)
    {
        int n = (int)RAIN_HEIGHT_RESOLUTION;
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Image.CreateFromData(n, n, false, Image.Format.Rf, bytes);
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
