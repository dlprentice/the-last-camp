namespace LastCamp.Tests;

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Godot;

/// <summary>A canonical text of what a scene or resource holds: every node in tree order (class, name, scene file,
/// groups, persistent connections, stored properties) and every stored property of each resource it reaches, with
/// values bit-exact (var_to_bytes). Sub-resources are written out in full; a resource with its own path is named by
/// the path. Two sources with the same text build the same thing, so the SHA-256 pins a recipe however it is written.</summary>
public static class SceneCanon
{
    public static string Scene(PackedScene scene)
    {
        Node root = scene.Instantiate();
        var text = new StringBuilder();
        WriteNode(root, root, text);
        root.Free();
        return text.ToString();
    }

    public static string Resource(Resource resource)
    {
        var text = new StringBuilder();
        text.Append(resource.GetClass()).Append(' ').Append(Properties(resource, null, new HashSet<ulong>())).Append('\n');
        return text.ToString();
    }

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static void WriteNode(Node node, Node root, StringBuilder text)
    {
        text.Append("node ").Append(root.GetPathTo(node)).Append(' ').Append(node.GetClass());
        if (!string.IsNullOrEmpty(node.SceneFilePath))
            text.Append(" scene=").Append(node.SceneFilePath);
        if (node.Owner != null)
            text.Append(" owner=").Append(root.GetPathTo(node.Owner));
        foreach (StringName group in node.GetGroups())
            text.Append(" group=").Append(group);
        text.Append('\n').Append("  ").Append(Properties(node, root, new HashSet<ulong>())).Append('\n');
        foreach (Godot.Collections.Dictionary signal in node.GetSignalList())
        {
            foreach (Godot.Collections.Dictionary connection in node.GetSignalConnectionList(signal["name"].AsStringName()))
            {
                long flags = connection["flags"].AsInt64();
                if ((flags & (long)GodotObject.ConnectFlags.Persist) == 0)
                    continue;
                Callable callable = connection["callable"].AsCallable();
                string target = callable.Target is Node targetNode ? root.GetPathTo(targetNode).ToString() : callable.Target?.GetClass() ?? "?";
                text.Append("  connect ").Append(signal["name"]).Append(" -> ").Append(target).Append("::").Append(callable.Method)
                    .Append(" flags=").Append(flags).Append('\n');
            }
        }
        foreach (Node child in node.GetChildren())
            WriteNode(child, root, text);
    }

    private static string Properties(GodotObject owner, Node? root, HashSet<ulong> visiting)
    {
        var text = new StringBuilder("{");
        foreach (Godot.Collections.Dictionary property in owner.GetPropertyList())
        {
            long usage = property["usage"].AsInt64();
            string name = property["name"].AsString();
            if ((usage & (long)PropertyUsageFlags.Storage) == 0 || (usage & (long)PropertyUsageFlags.ScriptVariable) != 0 || name == "resource_path")
                continue;
            Variant value = owner.Get(name);
            if (value.VariantType == Variant.Type.Callable)
                continue;
            text.Append(name).Append('=').Append(Value(value, root, visiting)).Append(';');
        }
        return text.Append('}').ToString();
    }

    private static string Value(Variant value, Node? root, HashSet<ulong> visiting)
    {
        switch (value.VariantType)
        {
            case Variant.Type.Nil:
                return "nil";
            case Variant.Type.Object:
                GodotObject? obj = value.AsGodotObject();
                if (obj == null)
                    return "null";
                if (obj is Node node)
                    return root != null ? "node:" + root.GetPathTo(node) : "node:" + node.Name;
                if (obj is Script script)
                    return "script:" + script.ResourcePath.GetFile().GetBaseName();
                if (obj is Resource resource)
                {
                    string path = resource.ResourcePath;
                    if (!string.IsNullOrEmpty(path) && !path.Contains("::"))
                        return "ext:" + path;
                    if (!visiting.Add(obj.GetInstanceId()))
                        return "cycle";
                    string inner = resource.GetClass() + Properties(resource, root, visiting);
                    visiting.Remove(obj.GetInstanceId());
                    return "sub:" + inner;
                }
                return "obj:" + obj.GetClass();
            case Variant.Type.Array:
                var items = new StringBuilder("[");
                foreach (Variant item in value.AsGodotArray())
                    items.Append(Value(item, root, visiting)).Append(',');
                return items.Append(']').ToString();
            case Variant.Type.Dictionary:
                var pairs = new StringBuilder("{");
                foreach (KeyValuePair<Variant, Variant> pair in value.AsGodotDictionary())
                    pairs.Append(Value(pair.Key, root, visiting)).Append(':').Append(Value(pair.Value, root, visiting)).Append(',');
                return pairs.Append('}').ToString();
            case Variant.Type.Callable:
            case Variant.Type.Signal:
            case Variant.Type.Rid:
                return value.VariantType.ToString();
            default:
                byte[] bytes = GD.VarToBytes(value);
                return bytes.Length <= 64 ? Convert.ToHexString(bytes) : "sha:" + Convert.ToHexString(SHA256.HashData(bytes));
        }
    }
}
