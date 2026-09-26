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

/// Bakes every near-tree variant into six-view albedo and normal atlases on
/// the GPU while the loading screen is up, then supplies the quad mesh and
/// materials that stand in for a hero tree beyond the switch distance. The
/// alpha-tested leaf cards of the treeline were the largest cost in the frame;
/// a lit, normal-mapped billboard costs two triangles.
public partial class TreeImpostors : Node3D
{
    public const long TILES = 6;
    public const long TILE_HEIGHT = 384;
    public const double SWITCH_DISTANCE = 45.0;
    public const double SWITCH_MARGIN = 4.0;

    public partial class Baked : RefCounted
    {
        public ImageTexture albedo;
        public ImageTexture normal;
        public Vector2 extent;
        public double base_y;
        public ShaderMaterial material;
        public ShaderMaterial shadow_material;
        public ArrayMesh quad;
    }

    public Godot.Collections.Dictionary baked = new Godot.Collections.Dictionary();
    public double bake_seconds = 0.0;
    public SubViewport _viewport;
    public Camera3D _camera;
    public Node3D _stage;
    public Shader _shader = _preload_tree_impostor;

    public static string key_for(TreeSpecies.Kind kind, long index)
    {
        return G.format("%d_%d", new Godot.Collections.Array { (long)kind, index });
    }

    public async Task bake(Forest forest)
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }
        long t0 = (long)Time.GetTicksMsec();
        _setup_viewport();
        // Freeze the sway while baking; the world controller writes the real gust
        // back into the global every frame once the build continues.
        RenderingServer.GlobalShaderParameterSet("wind_strength", 0.0);
        foreach (Variant kind_key in forest.variants.Keys)
        {
            TreeSpecies.Kind kind = (TreeSpecies.Kind)kind_key.AsInt64();
            TreeSpecies species = TreeSpecies.by_kind(kind);
            Godot.Collections.Array list = forest.variants[(long)kind].AsGodotArray();
            for (long index = 0, index_end = (long)list.Count; index < index_end; index++)
            {
                await _bake_variant(forest, species, index, list[(int)index].As<TreeGenerator.Result>());
            }
        }
        RenderingServer.GlobalShaderParameterSet("impostor_bake", 0);
        RenderingServer.GlobalShaderParameterSet("wind_strength", 1.0);
        _viewport.QueueFree();
        _viewport = null;
        bake_seconds = ((long)Time.GetTicksMsec() - t0) / 1000.0;
        G.print(G.format("Tree impostors: %d variants baked in %.1f s", new Godot.Collections.Array { (long)baked.Count, bake_seconds }));
    }

    public void _setup_viewport()
    {
        _viewport = new SubViewport();
        _viewport.Name = "ImpostorBake";
        _viewport.OwnWorld3D = true;
        _viewport.TransparentBg = true;
        _viewport.Msaa3D = Viewport.Msaa.Disabled;
        _viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
        _viewport.UseDebanding = false;
        _viewport.PositionalShadowAtlasSize = 0;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        AddChild(_viewport);
        _stage = new Node3D();
        _viewport.AddChild(_stage);
        _camera = new Camera3D();
        // Only the plain layer: with the mirror and underwater layer bits set, the
        // tree shaders' reflection clipping discards every fragment of the bake.
        _camera.CullMask = unchecked((uint)(1));
        _camera.Projection = Camera3D.ProjectionType.Orthogonal;
        _camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
        _camera.Near = 0.1f;
        _camera.Far = 400.0f;
        Environment env = new Environment();
        env.BackgroundMode = Environment.BGMode.Color;
        env.BackgroundColor = new Color(0, 0, 0, 0);
        env.AmbientLightSource = Environment.AmbientSource.Disabled;
        env.ReflectedLightSource = Environment.ReflectionSource.Disabled;
        env.TonemapMode = Environment.ToneMapper.Linear;
        env.TonemapExposure = 1.0f;
        env.FogEnabled = false;
        env.VolumetricFogEnabled = false;
        env.GlowEnabled = false;
        env.SsaoEnabled = false;
        env.SsilEnabled = false;
        env.SdfgiEnabled = false;
        env.SsrEnabled = false;
        _camera.Environment = env;
        _viewport.AddChild(_camera);
        _camera.MakeCurrent();
    }

    public async Task _bake_variant(Forest forest, TreeSpecies species, long index, TreeGenerator.Result result)
    {
        Aabb bounds = result.bark.GetAabb();
        if (result.leaves != null)
        {
            bounds = bounds.Merge(forest.leaf_bounds(result));
        }
        double width = clampf(maxf(bounds.Size.X, bounds.Size.Z) * 1.04, 0.5, 40.0);
        double base_y = clampf(minf(bounds.Position.Y, 0.0), -2.0, 0.0);
        double height = clampf(bounds.End.Y + 0.2 - base_y, 1.0, 60.0);
        // Tiles are clamped so an odd variant cannot request a giant viewport.
        long tile_width = clampi((long)ceil(TILE_HEIGHT * width / height), 32, 512);
        _viewport.Size = new Vector2I((int)(tile_width * TILES), (int)TILE_HEIGHT);
        foreach (Node child in _stage.GetChildren())
        {
            child.Free();
        }
        for (long i = 0; i < TILES; i++)
        {
            Node3D root = new Node3D();
            root.Position = new Vector3((float)(((double)i + 0.5 - TILES * 0.5) * width), 0.0f, 0.0f);
            Vector3 _t1 = root.Rotation;
            _t1.Y = (float)(-(double)i / (double)TILES * TAU);
            root.Rotation = _t1;
            _stage.AddChild(root);
            MeshInstance3D bark = new MeshInstance3D();
            bark.Mesh = result.bark;
            bark.MaterialOverride = forest.bark_materials[species.bark_set].As<Material>();
            bark.SetInstanceShaderParameter("tree_height", result.height);
            bark.SetInstanceShaderParameter("tint", Colors.White);
            root.AddChild(bark);
            if (result.leaves != null)
            {
                MeshInstance3D leaves = new MeshInstance3D();
                leaves.Mesh = result.leaves;
                leaves.MaterialOverride = forest.leaf_materials[species.leaf_atlas].As<Material>();
                leaves.CustomAabb = forest.leaf_bounds(result);
                leaves.SetInstanceShaderParameter("tree_height", result.height);
                leaves.SetInstanceShaderParameter("crown", new Color(result.crown_center.X, result.crown_center.Y, result.crown_center.Z, (float)result.crown_radius));
                leaves.SetInstanceShaderParameter("tint", species.leaf_tint);
                root.AddChild(leaves);
            }
        }
        _camera.Size = (float)height;
        _camera.Position = new Vector3(0.0f, (float)(base_y + height * 0.5), 120.0f);
        Image albedo = await _render_pass(1);
        Image normal = await _render_pass(2);
        // --impostor-dump=DIR writes every atlas as PNG for review.
        string dump = Game.Instance.arg_value("impostor-dump", "");
        if (dump != "")
        {
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dump));
            albedo.SavePng(dump.PathJoin(G.format("%s_%d_albedo.png", new Godot.Collections.Array { species.name, index })));
            normal.SavePng(dump.PathJoin(G.format("%s_%d_normal.png", new Godot.Collections.Array { species.name, index })));
        }
        TreeImpostors.Baked b = new TreeImpostors.Baked();
        b.extent = new Vector2((float)width, (float)height);
        b.base_y = base_y;
        b.albedo = ImageTexture.CreateFromImage(albedo);
        b.normal = ImageTexture.CreateFromImage(normal);
        b.quad = _quad(width, height, base_y);
        b.material = _material(b, species, false);
        b.shadow_material = _material(b, species, true);
        baked[key_for(species.kind, index)] = b;
    }

    public async Task<Image> _render_pass(long mode)
    {
        RenderingServer.GlobalShaderParameterSet("impostor_bake", mode);
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await new Signal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
        Image image = _viewport.GetTexture().GetImage();
        if (mode == 1)
        {
            Rect2I used = image.GetUsedRect();
            if (used.Size.X == 0)
            {
                G.push_warning(G.format("Impostor bake produced an empty atlas (%s)", _viewport.Size));
            }
        }
        image.GenerateMipmaps();
        return image;
    }

    public static ArrayMesh _quad(double width, double height, double base_y)
    {
        List<Vector3> vertices = new List<Vector3>(new List<Vector3> { new Vector3((float)(-width * 0.5), (float)base_y, 0.0f), new Vector3((float)(width * 0.5), (float)base_y, 0.0f), new Vector3((float)(width * 0.5), (float)(base_y + height), 0.0f), new Vector3((float)(-width * 0.5), (float)(base_y + height), 0.0f) });
        List<Vector2> uvs = new List<Vector2>(new List<Vector2> { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) });
        List<Vector3> normals = new List<Vector3>(new List<Vector3> { Vector3.Back, Vector3.Back, Vector3.Back, Vector3.Back });
        List<int> indices = new List<int>(new List<int> { 0, 1, 2, 0, 2, 3 });
        Godot.Collections.Array arrays = new Godot.Collections.Array();
        G.resize(arrays, (int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = Variant.From(vertices.ToArray());
        arrays[(int)Mesh.ArrayType.TexUV] = Variant.From(uvs.ToArray());
        arrays[(int)Mesh.ArrayType.Normal] = Variant.From(normals.ToArray());
        arrays[(int)Mesh.ArrayType.Index] = Variant.From(indices.ToArray());
        ArrayMesh mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.CustomAabb = new Aabb(new Vector3((float)(-width * 0.5), (float)base_y, (float)(-width * 0.5)), new Vector3((float)width, (float)height, (float)width));
        return mesh;
    }

    public ShaderMaterial _material(TreeImpostors.Baked b, TreeSpecies species, bool face_sun)
    {
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = _shader;
        mat.SetShaderParameter("albedo_atlas", b.albedo);
        mat.SetShaderParameter("normal_atlas", b.normal);
        mat.SetShaderParameter("tiles", TILES);
        mat.SetShaderParameter("face_sun", face_sun);
        bool conifer = species.kind == TreeSpecies.Kind.SPRUCE || species.kind == TreeSpecies.Kind.PINE;
        mat.SetShaderParameter("translucency", conifer ? 0.12 : 0.22);
        mat.SetShaderParameter("roughness", conifer ? 0.66 : 0.6);
        return mat;
    }
    private static Shader _preload_tree_impostor => _preload_tree_impostor_cache ??= Content.Load<Shader>("res://shaders/tree_impostor.gdshader");
    private static Shader _preload_tree_impostor_cache;
}
