#nullable enable
using System;
using Godot;

namespace LastCamp;

/// The one way geometry reaches the engine. Since Godot 4.8 beta2 the generated C# bindings
/// keep only `this` alive across a native call, so a collection argument (including the
/// hidden default `blendShapes` and `lods` temporaries) can be finalized while the engine is
/// still reading it: the surface is silently dropped (`p_arrays.size() != ARRAY_MAX`), or
/// the engine crashes (upstream godotengine/godot#125198). Every commit here passes
/// long-lived empty collections for the defaults, keeps each collection argument alive until
/// the call has returned, and checks that exactly one surface was added.
public static class MeshCommit
{
    // Shared read-only empties: the engine only reads them, from any thread.
    private static readonly Godot.Collections.Array<Godot.Collections.Array> NoBlendShapes = ReadOnly(new Godot.Collections.Array<Godot.Collections.Array>());
    private static readonly Godot.Collections.Dictionary NoLods = ReadOnly(new Godot.Collections.Dictionary());
    private static readonly Godot.Collections.Array NoBoneTransforms = ReadOnly(new Godot.Collections.Array());

    /// Tests set this: a short surface count throws at once instead of being retried, so a
    /// lost surface cannot hide behind the retry.
    public static bool Strict;

    /// ArrayMesh.add_surface_from_arrays. Returns the new surface's index.
    public static int AddSurface(ArrayMesh mesh, Godot.Collections.Array arrays, Godot.Collections.Dictionary? lods = null,
        Mesh.ArrayFormat flags = 0, Mesh.PrimitiveType primitive = Mesh.PrimitiveType.Triangles)
    {
        Godot.Collections.Dictionary lodTable = lods ?? NoLods;
        int before = mesh.GetSurfaceCount();
        for (int attempt = 0; ; attempt++)
        {
            mesh.AddSurfaceFromArrays(primitive, arrays, NoBlendShapes, lodTable, flags);
            GC.KeepAlive(arrays);
            GC.KeepAlive(lodTable);
            int after = mesh.GetSurfaceCount();
            if (after == before + 1)
                return before;
            Short("ArrayMesh.AddSurfaceFromArrays", before, after, attempt);
        }
    }

    /// ImporterMesh.add_surface, for meshes that go on to generate LODs. Returns the new surface's index.
    public static int AddSurface(ImporterMesh importer, Godot.Collections.Array arrays, Material? material = null,
        string name = "", ulong flags = 0, Mesh.PrimitiveType primitive = Mesh.PrimitiveType.Triangles)
    {
        int before = importer.GetSurfaceCount();
        for (int attempt = 0; ; attempt++)
        {
            importer.AddSurface(primitive, arrays, NoBlendShapes, NoLods, material, name, flags);
            GC.KeepAlive(arrays);
            int after = importer.GetSurfaceCount();
            if (after == before + 1)
                return before;
            Short("ImporterMesh.AddSurface", before, after, attempt);
        }
    }

    /// ImporterMesh.generate_lods without a temporary bone-transform array.
    public static void GenerateLods(ImporterMesh importer, float normalMergeAngle, float normalSplitAngle)
    {
        importer.GenerateLods(normalMergeAngle, normalSplitAngle, NoBoneTransforms);
        GC.KeepAlive(NoBoneTransforms);
    }

    /// Reports a commit that did not add exactly one surface. Returns to retry once (outside
    /// tests); throws when strict, when the retry also came up short or when surfaces were added.
    private static void Short(string call, int before, int after, int attempt)
    {
        string message = $"{call} added {after - before} surfaces (expected 1, had {before}); attempt {attempt + 1}";
        GD.PushError(message);
        if (Strict || attempt > 0 || after != before)
            throw new InvalidOperationException(message);
        GD.Print("MESH_COMMIT_RETRY ", call);
    }

    private static T ReadOnly<T>(T collection) where T : class
    {
        switch (collection)
        {
            case Godot.Collections.Array<Godot.Collections.Array> typed: typed.MakeReadOnly(); break;
            case Godot.Collections.Array array: array.MakeReadOnly(); break;
            case Godot.Collections.Dictionary dict: dict.MakeReadOnly(); break;
        }
        return collection;
    }
}
