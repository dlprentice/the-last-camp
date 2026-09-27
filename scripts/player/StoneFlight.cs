using System;
using Godot;

namespace LastCamp;

/// A small fixed-step ballistic model. Speed and the player's release angle
/// determine skips; a steep or slow contact sinks instead of awarding a
/// predetermined score. Rendering and contact sounds live in Pond.
public sealed class StoneFlight
{
    public const int MaxSkips = 7;
    public enum Contact { None, Skip, Sink, Ground }
    public Vector3 Position { get; private set; }
    public Vector3 Velocity { get; private set; }
    public bool Active { get; private set; } = true;
    public int Skips { get; private set; }
    private float _age;

    public StoneFlight(Vector3 position, Vector3 direction, float charge)
    {
        Position = position;
        Velocity = direction.Normalized() * Mathf.Lerp(7.5f, 18f, Mathf.Clamp(charge, 0, 1));
    }

    public Contact Step(float delta, float waterHeight, float groundHeight)
    {
        if (!Active) return Contact.None;
        _age += delta;
        Position += Velocity * delta + Vector3.Down * (4.9f * delta * delta);
        Velocity += Vector3.Down * (9.8f * delta);
        if (_age > 8 || Position.Y <= groundHeight)
        {
            Active = false;
            return Contact.Ground;
        }
        if (Position.Y > waterHeight || Velocity.Y >= 0) return Contact.None;
        Position = new Vector3(Position.X, waterHeight + 0.015f, Position.Z);
        float horizontal = new Vector2(Velocity.X, Velocity.Z).Length();
        if (horizontal < 4.5f || -Velocity.Y / Math.Max(horizontal, 0.01f) > 0.65f || Skips >= MaxSkips)
        {
            Active = false;
            return Contact.Sink;
        }
        Skips++;
        Velocity = new Vector3(Velocity.X * 0.77f, Mathf.Clamp(-Velocity.Y * 0.53f, 0.65f, 2.3f), Velocity.Z * 0.77f);
        return Contact.Skip;
    }

    public void Stop() => Active = false;
}
