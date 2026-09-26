using Godot;

namespace LastCamp.Construction;

/// <summary>Load generated assets through Godot's native resource cache.</summary>
public static class Content
{
    public static T Load<T>(string path) where T : Resource => GD.Load<T>(path);
    public static Resource Load(string path) => GD.Load(path);
    public static bool Exists(string path) => ResourceLoader.Exists(path);
}
