namespace GdRuntime;

using Godot;

/// <summary>A plain C# object (a translated inner class that never needed to be a Godot object) carried in a Variant,
/// so a reflective read such as obj.get(name) can still reach its fields.</summary>
public sealed partial class GdBox : RefCounted
{
    public object? Value;

    public GdBox()
    {
    }

    public GdBox(object value) => Value = value;
}
