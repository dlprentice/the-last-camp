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

/// Smoothed free camera. WASD/Space/C move, wheel changes speed; middle click
/// locks/unlocks centre-ray focus. Focus is sampled at 12 Hz, not every frame.
public partial class PhotoRig : Node3D
{
    public const double MIN_SPEED = 0.8;
    public const double MAX_SPEED = 18.0;
    public const double FOCUS_INTERVAL = 1.0 / 12.0;
    public Camera3D camera;
    public double speed = 4.5;
    public double yaw = 0.0;
    public double pitch = 0.0;
    public bool _active = false;
    public Vector3 _velocity = Vector3.Zero;
    public double _focus_distance = 8.0;
    public double _target_focus = 8.0;
    public double _focus_timer = 0.0;
    public bool focus_locked = false;
    public Godot.Collections.Dictionary _saved_focus = new Godot.Collections.Dictionary();

    public override void _Ready()
    {
        ProcessPriority = -10;
        camera = new Camera3D();
        camera.Name = "PhotoCamera";
        camera.Fov = 55.0f;
        camera.Near = 0.05f;
        camera.Far = 1600.0f;
        camera.CullMask = unchecked((uint)(0xFFFFF & ~(Pond.REFLECTION_LAYER | Pond.UNDERWATER_REFLECTION_LAYER)));
        AddChild(camera);
        Game.Instance.photo_camera = camera;
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    public void enter(Camera3D from)
    {
        if (from != null)
        {
            GlobalPosition = from.GlobalPosition;
            Vector3 forward = -from.GlobalBasis.Z;
            yaw = atan2(-forward.X, -forward.Z);
            pitch = asin(clampf(forward.Y, -1.0, 1.0));
            camera.Fov = from.Fov;
        }
        camera.Rotation = new Vector3((float)pitch, (float)yaw, 0);
        _velocity = Vector3.Zero;
        _focus_timer = FOCUS_INTERVAL;
        focus_locked = false;
        camera.MakeCurrent();
        if (Game.Instance.world != null)
        {
            camera.Attributes = Game.Instance.world.attributes;
            CameraAttributesPractical attributes = Game.Instance.world.attributes;
            foreach (Variant key in new Godot.Collections.Array { (StringName)"dof_blur_far_enabled", (StringName)"dof_blur_near_enabled", (StringName)"dof_blur_far_distance", (StringName)"dof_blur_near_distance", (StringName)"dof_blur_far_transition", (StringName)"dof_blur_near_transition", (StringName)"dof_blur_amount" })
            {
                _saved_focus[key] = attributes.Get(key.AsStringName());
            }
        }
        if (Game.Instance.player != null)
        {
            Game.Instance.player.suspend();
        }
        Game.Instance.mode = Game.Mode.PHOTO;
        _active = true;
        SetProcess(true);
        SetPhysicsProcess(true);
    }

    public void exit()
    {
        _active = false;
        SetProcess(false);
        SetPhysicsProcess(false);
        _velocity = Vector3.Zero;
        if (Game.Instance.world != null)
        {
            foreach (Variant key in _saved_focus.Keys)
            {
                Game.Instance.world.attributes.Set(key.AsStringName(), _saved_focus[key]);
            }
        }
        _saved_focus.Clear();
        if (Game.Instance.player != null)
        {
            Game.Instance.player.begin();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_active)
        {
            return;
        }
        if (@event is InputEventMouseMotion)
        {
            InputEventMouseMotion motion = ((InputEventMouseMotion)@event);
            yaw -= motion.Relative.X * 0.0022;
            pitch = clampf(pitch - motion.Relative.Y * 0.0022, -deg_to_rad(85.0), deg_to_rad(85.0));
        }
        else if (@event is InputEventMouseButton && ((InputEventMouseButton)@event).IsPressed())
        {
            InputEventMouseButton button = ((InputEventMouseButton)@event);
            if (button.ButtonIndex == MouseButton.WheelUp)
            {
                speed = minf(speed * 1.15, MAX_SPEED);
            }
            else if (button.ButtonIndex == MouseButton.WheelDown)
            {
                speed = maxf(speed / 1.15, MIN_SPEED);
            }
            else if (button.ButtonIndex == MouseButton.Middle)
            {
                focus_locked = !focus_locked;
            }
        }
    }

    public static Vector3 move_direction(Basis frame, Vector3 wish)
    {
        // Input.get_axis(forward, back) is negative for W. Godot forward is -Z;
        // another minus sign here reversed W/S in the original free camera.
        return (frame.X * wish.X + Vector3.Up * wish.Y + frame.Z * wish.Z).LimitLength(1.0f);
    }

    public static double focus_step(double current, double target, double delta)
    {
        return lerpf(current, target, 1.0 - exp(-maxf(delta, 0.0) * 5.0));
    }

    public override void _Process(double delta)
    {
        if (!_active)
        {
            return;
        }
        double look_blend = 1.0 - exp(-delta * 22.0);
        camera.Rotation = new Vector3((float)lerpf(camera.Rotation.X, pitch, look_blend), (float)lerp_angle(camera.Rotation.Y, yaw, look_blend), 0);
        Vector3 wish = new Vector3(Input.GetAxis("move_left", "move_right"), Input.GetAxis("crouch", "fly_up"), Input.GetAxis("move_forward", "move_back"));
        double sprint = Input.IsActionPressed("sprint") ? 2.1 : 1.0;
        Vector3 target = move_direction(camera.Basis, wish) * (float)speed * (float)sprint;
        // Exact integration of first-order velocity response for constant input:
        // smooth starts/stops without travel changing with capture frame rate.
        double decay = exp(-delta * 9.0);
        GlobalPosition += target * (float)delta + (_velocity - target) * (float)(1.0 - decay) / 9.0f;
        _velocity = target + (_velocity - target) * (float)decay;
        _focus_distance = focus_step(_focus_distance, _target_focus, delta);
        if (Game.Instance.world != null)
        {
            CameraAttributesPractical a = Game.Instance.world.attributes;
            a.DofBlurFarEnabled = Quality.Instance.current.dof;
            a.DofBlurNearEnabled = Quality.Instance.current.dof;
            a.DofBlurFarDistance = (float)(_focus_distance * 1.15);
            a.DofBlurFarTransition = (float)maxf(0.8, _focus_distance * 2.2);
            a.DofBlurNearDistance = (float)maxf(0.03, _focus_distance * 0.55);
            a.DofBlurNearTransition = (float)maxf(0.25, _focus_distance * 0.4);
            a.DofBlurAmount = 0.035f;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_active || focus_locked)
        {
            return;
        }
        _focus_timer += delta;
        if (_focus_timer < FOCUS_INTERVAL)
        {
            return;
        }
        _focus_timer = fmod(_focus_timer, FOCUS_INTERVAL);
        Vector3 origin = camera.GlobalPosition;
        Vector3 direction = -camera.GlobalBasis.Z;
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(origin, origin + direction * 120.0f, 1);
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        double distance = !(hit.Count == 0) ? origin.DistanceTo(hit["position"].AsVector3()) : 80.0;
        // Water has no collider. Focus its visible surface rather than the distant
        // pond bed when the centre ray crosses open water before a solid object.
        if (direction.Y < -0.001 && origin.Y > TerrainField.WATER_LEVEL && Game.Instance.camp != null)
        {
            double water_distance = (TerrainField.WATER_LEVEL - origin.Y) / direction.Y;
            Vector3 at = origin + direction * (float)water_distance;
            if (water_distance > 0.0 && water_distance < distance && Game.Instance.camp.field.water_depth(at.X, at.Z) > 0.0)
            {
                distance = water_distance;
            }
        }
        _target_focus = clampf(distance, 0.30, 80.0);
    }
}
