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

namespace LastCamp.Tools;

/// Prints what Godot imported from every models/<name>/<name>.gltf: mesh
/// instances, surfaces, triangle counts, LOD levels and material textures.
///   godot --headless --path . --script res://tools/InspectModels.cs
public partial class InspectModels : SceneTree
{
    public override void _Initialize()
    {
        DirAccess dir = DirAccess.Open("res://models");
        Godot.Collections.Array<string> names = new Godot.Collections.Array<string>();
        foreach (string sub in dir.GetDirectories())
        {
            if (FileAccess.FileExists(G.format("res://models/%s/%s.gltf", new Godot.Collections.Array { sub, sub })))
            {
                names.Add(sub);
            }
        }
        G.sort(names);
        foreach (string f in names)
        {
            PackedScene scene = Content.Load<PackedScene>(G.format("res://models/%s/%s.gltf", new Godot.Collections.Array { f, f }));
            if (scene == null)
            {
                G.print(G.format("%-28s FAILED to load", f));
                continue;
            }
            Node root = scene.Instantiate();
            Godot.Collections.Array<MeshInstance3D> meshes = new Godot.Collections.Array<MeshInstance3D>();
            _collect(root, meshes);
            foreach (MeshInstance3D mi in meshes)
            {
                Mesh mesh = mi.Mesh;
                string line = G.format("%-28s node=%s surfaces=%d", new Godot.Collections.Array { f, (StringName)mi.Name, mesh.GetSurfaceCount() });
                Aabb aabb = mesh.GetAabb();
                line += G.format(" size=(%.2f %.2f %.2f)", new Godot.Collections.Array { aabb.Size.X, aabb.Size.Y, aabb.Size.Z });
                for (long s = 0, s_end = mesh.GetSurfaceCount(); s < s_end; s++)
                {
                    Godot.Collections.Array arrays = mesh.SurfaceGetArrays((int)s);
                    List<int> idx = G.ListFromVariant<int>(arrays[(int)Mesh.ArrayType.Index]);
                    long tris = (long)idx.Count > 0 ? (long)idx.Count / 3 : (long)G.ListFromVariant<Vector3>(arrays[(int)Mesh.ArrayType.Vertex]).Count / 3;
                    long lods = 0;
                    if (mesh is ArrayMesh)
                    {
                        lods = ((ArrayMesh)mesh).HasMethod("surface_get_lod_count") ? ((ArrayMesh)mesh).Call("surface_get_lod_count", s).AsInt64() : -1;
                    }
                    Material mat = mesh.SurfaceGetMaterial((int)s);
                    string mat_desc = "none";
                    if (mat is BaseMaterial3D)
                    {
                        BaseMaterial3D sm = ((BaseMaterial3D)mat);
                        bool rough_or_orm = sm is OrmMaterial3D ? (sm as OrmMaterial3D).OrmTexture != null : (sm as StandardMaterial3D).RoughnessTexture != null;
                        mat_desc = G.format("%s albedo=%s normal=%s rough/orm=%s alpha=%d cull=%d", new Godot.Collections.Array { ((BaseMaterial3D)mat).GetClass(), sm.AlbedoTexture != null, sm.NormalEnabled, rough_or_orm, (long)sm.Transparency, (long)sm.CullMode });
                    }
                    line += G.format("\n    surface %d: tris=%d lods=%d %s", new Godot.Collections.Array { s, tris, lods, mat_desc });
                }
                G.print(line);
            }
            root.Free();
        }
        Quit();
    }

    public void _collect(Node node, Godot.Collections.Array<MeshInstance3D> @out)
    {
        if (node is MeshInstance3D)
        {
            @out.Add(((MeshInstance3D)node));
        }
        foreach (Node c in node.GetChildren())
        {
            _collect(c, @out);
        }
    }

    public override void _Finalize()
    {
        G.drain_finalizers();
    }
}
