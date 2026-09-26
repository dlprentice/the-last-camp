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

/// First-person controller: capsule physics against the terrain heightmap and
/// props, wading with a depth limit, crouch, sprint with a subtle FOV kick,
/// procedural head motion, surface-aware footsteps and an interaction ray.
public partial class Player : CharacterBody3D
{
    [Signal]
    public delegate void footstepEventHandler(StringName surface, bool running);
    [Signal]
    public delegate void wading_changedEventHandler(bool active, double speed);
    [Signal]
    public delegate void focus_changedEventHandler(Node target);

    public const double WALK_SPEED = 2.9;
    public const double RUN_SPEED = 5.6;
    public const double CROUCH_SPEED = 1.5;
    public const double WADE_FACTOR = 0.55;
    public const double MAX_WADE_DEPTH = 1.15;
    public const double ACCELERATION = 14.0;
    public const double FRICTION = 11.0;
    public const double AIR_CONTROL = 3.0;
    public const double GRAVITY = 9.8;
    public const double EYE_HEIGHT = 1.68;
    public const double CROUCH_EYE_HEIGHT = 1.02;
    public const double MOUSE_SENSITIVITY = 0.0019;
    public static readonly double MAX_PITCH = deg_to_rad(84.0);
    public const double BASE_FOV = 70.0;
    public const double SPRINT_FOV = 76.0;
    public const double INTERACT_RANGE = 3.2;
    public const double STEP_LENGTH_WALK = 1.75;
    public const double STEP_LENGTH_RUN = 2.3;

    public Node3D head;
    public Camera3D camera;
    public Node3D hand_socket;
    public CollisionShape3D collider;
    public RayCast3D interact_ray;
    public PhotoRig photo;
    public OmniLight3D hand_light;
    public Node3D hand_lantern;

    public bool enabled = false;
    public double yaw = 0.0;
    public double pitch = 0.0;
    public bool crouching = false;
    public bool running = false;
    public bool in_water = false;
    public double water_depth = 0.0;
    public StringName held_item = "";
    public bool lantern_on = false;

    public double _bob_phase = 0.0;
    public double _step_distance = 0.0;
    public double _eye_height = EYE_HEIGHT;
    public double _land_dip = 0.0;
    public bool _was_on_floor = true;
    public double _fall_speed = 0.0;
    public Node _focus;
    public double _lean = 0.0;
    public double _strafe = 0.0;

    public override void _Ready()
    {
        Game.Instance.player = this;
        _build_children();
        // Arrive on the trail south of the camp, facing the fire and the pond.
        GlobalPosition = new Vector3(3.0f, 0.0f, 30.0f);
        yaw = deg_to_rad(6.0);
        SetPhysicsProcess(true);
    }

    public void _build_children()
    {
        collider = new CollisionShape3D();
        CapsuleShape3D shape = new CapsuleShape3D();
        shape.Radius = 0.32f;
        shape.Height = 1.8f;
        collider.Shape = shape;
        collider.Position = new Vector3(0.0f, 0.9f, 0.0f);
        AddChild(collider);

        head = new Node3D();
        head.Name = "Head";
        head.Position = new Vector3(0.0f, (float)EYE_HEIGHT, 0.0f);
        AddChild(head);

        camera = new Camera3D();
        camera.Name = "Camera";
        camera.Fov = (float)BASE_FOV;
        camera.Near = 0.05f;
        camera.Far = 1600.0f;
        camera.CullMask = unchecked((uint)(0xFFFFF & ~(Pond.REFLECTION_LAYER | Pond.UNDERWATER_REFLECTION_LAYER)));
        head.AddChild(camera);

        hand_socket = new Node3D();
        hand_socket.Name = "HandSocket";
        hand_socket.Position = new Vector3(0.34f, -0.42f, -0.55f);
        camera.AddChild(hand_socket);

        interact_ray = new RayCast3D();
        interact_ray.TargetPosition = new Vector3(0.0f, 0.0f, (float)-INTERACT_RANGE);
        interact_ray.CollisionMask = unchecked((uint)(1 | 1L << 1));
        interact_ray.CollideWithAreas = true;
        interact_ray.Enabled = true;
        camera.AddChild(interact_ray);

        _build_hand_lantern();
        FloorMaxAngle = (float)deg_to_rad(52.0);
        FloorSnapLength = 0.4f;
        CollisionMask = 1;
        CollisionLayer = (uint)(1L << 2);
        CallDeferred("_add_photo_rig");
    }

    public void _add_photo_rig()
    {
        if (photo != null || GetParent() == null)
        {
            return;
        }
        photo = new PhotoRig();
        GetParent().AddChild(photo);
    }

    public void _build_hand_lantern()
    {
        hand_lantern = new Node3D();
        hand_lantern.Name = "HandLantern";
        hand_lantern.Visible = false;
        hand_lantern.Scale = Vector3.One * 0.62f;
        hand_lantern.Position = new Vector3(0.0f, -0.3f, 0.0f);
        hand_socket.AddChild(hand_lantern);
        MeshInstance3D metal = new MeshInstance3D();
        metal.Mesh = PropMeshes.hurricane_metal();
        metal.MaterialOverride = PropMaterials.iron(new Color(0.2f, 0.17f, 0.14f));
        metal.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        hand_lantern.AddChild(metal);
        MeshInstance3D glass = new MeshInstance3D();
        glass.Mesh = PropMeshes.hurricane_glass();
        StandardMaterial3D glass_mat = new StandardMaterial3D();
        glass_mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        glass_mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        glass_mat.AlbedoColor = new Color(1.0f, 0.9f, 0.7f, 0.28f);
        glass_mat.Roughness = 0.08f;
        glass_mat.EmissionEnabled = true;
        glass_mat.Emission = new Color(1.0f, 0.62f, 0.25f);
        glass_mat.EmissionEnergyMultiplier = 0.55f;
        glass.MaterialOverride = glass_mat;
        glass.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        hand_lantern.AddChild(glass);
        MeshInstance3D flame = new MeshInstance3D();
        SphereMesh flame_mesh = new SphereMesh();
        flame_mesh.Radius = 0.014f;
        flame_mesh.Height = 0.04f;
        flame_mesh.RadialSegments = 8;
        flame_mesh.Rings = 5;
        flame.Mesh = flame_mesh;
        StandardMaterial3D flame_mat = new StandardMaterial3D();
        flame_mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        flame_mat.AlbedoColor = new Color(1.0f, 0.75f, 0.4f);
        flame_mat.EmissionEnabled = true;
        flame_mat.Emission = new Color(1.0f, 0.72f, 0.35f);
        flame_mat.EmissionEnergyMultiplier = 6.0f;
        flame.MaterialOverride = flame_mat;
        Vector3 _t1 = flame.Position;
        _t1.Y = (float)CampLantern.FLAME_HEIGHT;
        flame.Position = _t1;
        flame.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        hand_lantern.AddChild(flame);
        hand_light = new OmniLight3D();
        hand_light.LightColor = new Color(1.0f, 0.74f, 0.4f);
        hand_light.LightEnergy = 1.7f;
        hand_light.OmniRange = 7.5f;
        hand_light.OmniAttenuation = 1.3f;
        hand_light.ShadowEnabled = false;
        hand_light.LightVolumetricFogEnergy = 0.35f;
        hand_light.Position = new Vector3(0.0f, (float)(CampLantern.FLAME_HEIGHT + 0.02), 0.0f);
        hand_lantern.AddChild(hand_light);
    }

    public void begin()
    {
        /// Hands control to the player (after the intro or when photo mode ends).
        enabled = true;
        snap_to_ground();
        camera.MakeCurrent();
        if (Game.Instance.world != null)
        {
            camera.Attributes = Game.Instance.world.attributes;
        }
        Game.Instance.mode = Game.Mode.PLAY;
    }

    public void snap_to_ground()
    {
        if (Game.Instance.camp == null)
        {
            return;
        }
        Vector3 _t1 = GlobalPosition;
        _t1.Y = (float)(Game.Instance.camp.height_at(GlobalPosition.X, GlobalPosition.Z) + 0.12);
        GlobalPosition = _t1;
    }

    public void toggle_lantern()
    {
        lantern_on = !lantern_on;
        if (hand_lantern != null)
        {
            hand_lantern.Visible = lantern_on;
        }
        if (Game.Instance.audio != null)
        {
            Game.Instance.audio.play_interact("lantern_toggle");
        }
    }

    public void suspend()
    {
        enabled = false;
        Velocity = Vector3.Zero;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("photo_mode"))
        {
            if (Game.Instance.mode == Game.Mode.PHOTO && photo != null)
            {
                photo.exit();
            }
            else if (Game.Instance.mode == Game.Mode.PLAY && enabled && photo != null)
            {
                photo.enter(camera);
            }
            return;
        }
        if (!enabled || Game.Instance.mode != Game.Mode.PLAY)
        {
            return;
        }
        if (@event is InputEventMouseMotion)
        {
            InputEventMouseMotion motion = ((InputEventMouseMotion)@event);
            yaw -= motion.Relative.X * MOUSE_SENSITIVITY;
            pitch = clampf(pitch - motion.Relative.Y * MOUSE_SENSITIVITY, -MAX_PITCH, MAX_PITCH);
        }
        else if (@event.IsActionPressed("interact"))
        {
            if (_focus != null && _focus.HasMethod("interact"))
            {
                _focus.Call("interact", this);
            }
        }
        else if (@event.IsActionPressed("toggle_lantern"))
        {
            toggle_lantern();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Game.Instance.camp == null)
        {
            return;
        }
        _update_water_state();
        Vector2 input = Vector2.Zero;
        if (enabled && Game.Instance.mode == Game.Mode.PLAY)
        {
            input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
            crouching = Input.IsActionPressed("crouch");
            running = Input.IsActionPressed("sprint") && !crouching && input.Length() > 0.1;
        }
        else
        {
            running = false;
        }

        double speed = crouching ? CROUCH_SPEED : running ? RUN_SPEED : WALK_SPEED;
        if (in_water)
        {
            speed *= lerpf(1.0, WADE_FACTOR, clampf(water_depth / 0.8, 0.0, 1.0));
        }

        Vector3 forward = new Vector3((float)-sin(yaw), 0.0f, (float)-cos(yaw));
        Vector3 right = new Vector3((float)cos(yaw), 0.0f, (float)-sin(yaw));
        Vector3 wish = input.Length() > 0.01 ? (right * input.X + forward * -input.Y).Normalized() : Vector3.Zero;
        Vector3 desired = wish * (float)speed;
        _strafe = input.X;

        bool on_floor = IsOnFloor();
        double accel = on_floor ? (wish != Vector3.Zero ? ACCELERATION : FRICTION) : AIR_CONTROL;
        Vector3 horizontal = new Vector3(Velocity.X, 0.0f, Velocity.Z).MoveToward(desired, (float)(accel * delta));
        horizontal = _limit_wading(horizontal, delta);
        Vector3 _t1 = Velocity;
        _t1.X = horizontal.X;
        Velocity = _t1;
        Vector3 _t2 = Velocity;
        _t2.Z = horizontal.Z;
        Velocity = _t2;
        if (!on_floor)
        {
            Vector3 _t3 = Velocity;
            _t3.Y = (float)(Velocity.Y - GRAVITY * delta);
            Velocity = _t3;
            _fall_speed = Velocity.Y;
        }
        else if (Velocity.Y < 0.0)
        {
            Vector3 _t4 = Velocity;
            _t4.Y = 0.0f;
            Velocity = _t4;
        }
        MoveAndSlide();

        if (on_floor && !_was_on_floor && _fall_speed < -3.0)
        {
            _land_dip = clampf(-_fall_speed * 0.02, 0.0, 0.14);
        }
        _was_on_floor = on_floor;

        _update_head(delta, horizontal.Length(), on_floor);
        _update_focus();
        Vector3 _t5 = Rotation;
        _t5.Y = (float)yaw;
        Rotation = _t5;
        Vector3 _t6 = head.Rotation;
        _t6.X = (float)pitch;
        head.Rotation = _t6;
        Vector3 _t7 = head.Rotation;
        _t7.Z = (float)_lean;
        head.Rotation = _t7;
    }

    public void _update_water_state()
    {
        // Depth is how far the feet are under the surface, never more than the
        // pond is deep there: standing on the dock over deep water is dry, wading
        // on the bed reads the bed. The origin sits 0.12 m above the support.
        double bed_depth = Game.Instance.camp.field.water_depth(GlobalPosition.X, GlobalPosition.Z);
        double submerged = TerrainField.WATER_LEVEL - (GlobalPosition.Y - 0.12);
        double depth = clampf(minf(bed_depth, submerged), 0.0, bed_depth);
        bool was = in_water;
        water_depth = depth;
        in_water = depth > 0.12;
        double planar_speed = new Vector2(Velocity.X, Velocity.Z).Length();
        if (in_water != was || in_water && planar_speed > 0.2)
        {
            EmitSignal(SignalName.wading_changed, in_water, in_water ? planar_speed : 0.0);
        }
    }

    public Vector3 _limit_wading(Vector3 horizontal, double _delta)
    {
        /// Stops the player wading past chest depth: movement towards deeper water is
        /// cancelled and the shallows pull gently in the direction the bed rises.
        if (water_depth <= MAX_WADE_DEPTH)
        {
            return horizontal;
        }
        Vector3 shallow = _shallow_direction();
        double deeper = -horizontal.Dot(shallow);
        if (deeper > 0.0)
        {
            horizontal += shallow * (float)deeper;
        }
        double excess = clampf((water_depth - MAX_WADE_DEPTH) * 3.0, 0.0, 1.0);
        double wanted = 1.4 * excess;
        double current = horizontal.Dot(shallow);
        if (current < wanted)
        {
            horizontal += shallow * (float)(wanted - current);
        }
        return horizontal;
    }

    public Vector3 _shallow_direction()
    {
        /// Horizontal direction in which the pond bed rises (the gradient of the
        /// ground height), falling back to "away from the pond centre".
        TerrainField field = Game.Instance.camp.field;
        double x = GlobalPosition.X;
        double z = GlobalPosition.Z;
        double step = 0.75;
        Vector3 dir = new Vector3((float)(field.height(x + step, z) - field.height(x - step, z)), 0.0f, (float)(field.height(x, z + step) - field.height(x, z - step)));
        if (dir.LengthSquared() < 1e-6)
        {
            dir = new Vector3((float)(x - TerrainField.POND_CENTRE.X), 0.0f, (float)(z - TerrainField.POND_CENTRE.Y));
        }
        if (dir.LengthSquared() < 1e-6)
        {
            dir = Vector3.Right;
        }
        return dir.Normalized();
    }

    public void _update_head(double delta, double speed, bool on_floor)
    {
        double target_eye = crouching ? CROUCH_EYE_HEIGHT : EYE_HEIGHT;
        if (in_water)
        {
            target_eye -= minf(water_depth * 0.15, 0.12);
        }
        _eye_height = lerpf(_eye_height, target_eye, 1.0 - exp(-delta * 9.0));
        _land_dip = lerpf(_land_dip, 0.0, 1.0 - exp(-delta * 7.0));

        bool moving = speed > 0.35 && on_floor;
        if (moving)
        {
            double stride = running ? STEP_LENGTH_RUN : STEP_LENGTH_WALK;
            _bob_phase += speed / stride * PI * delta;
            _step_distance += speed * delta;
            if (_step_distance >= stride * 0.5)
            {
                _step_distance = 0.0;
                EmitSignal(SignalName.footstep, (StringName)_current_surface(), running);
            }
        }
        else
        {
            _bob_phase = lerp_angle(_bob_phase, roundf(_bob_phase / PI) * PI, 1.0 - exp(-delta * 6.0));
        }

        double amp = clampf(speed / RUN_SPEED, 0.0, 1.0) * (crouching ? 0.55 : 1.0);
        double bob_y = sin(_bob_phase * 2.0) * 0.024 * amp;
        double bob_x = sin(_bob_phase) * 0.016 * amp;
        head.Position = new Vector3((float)bob_x, (float)(_eye_height + bob_y - _land_dip), 0.0f);
        _lean = lerpf(_lean, -_strafe * 0.012, 1.0 - exp(-delta * 5.0));

        double target_fov = running ? SPRINT_FOV : BASE_FOV;
        camera.Fov = (float)lerpf(camera.Fov, target_fov, 1.0 - exp(-delta * 5.0));
    }

    public StringName _current_surface()
    {
        if (in_water)
        {
            return "water";
        }
        if (IsOnFloor())
        {
            GodotObject collider_node = GetSlideCollisionCount() > 0 ? GetLastSlideCollision().GetCollider() : null;
            if (collider_node != null && collider_node.HasMeta("surface"))
            {
                return collider_node.GetMeta("surface").AsStringName();
            }
        }
        Color mask = Game.Instance.camp.field.material_mask(GlobalPosition.X, GlobalPosition.Z);
        if (mask.A > 0.45)
        {
            return "dirt";
        }
        if (mask.B > 0.5)
        {
            return "dirt";
        }
        return "grass";
    }

    public void _update_focus()
    {
        Node target = null;
        if (enabled && Game.Instance.mode == Game.Mode.PLAY && interact_ray.IsColliding())
        {
            GodotObject hit = interact_ray.GetCollider();
            if (hit != null)
            {
                target = hit as Node;
                while (target != null && !target.HasMethod("interact"))
                {
                    target = target.GetParent();
                }
            }
        }
        if (target != _focus)
        {
            _focus = target;
            EmitSignal(SignalName.focus_changed, target);
        }
    }

    public Node focus()
    {
        return _focus;
    }

    public Vector3 eye_position()
    {
        return camera.GlobalPosition;
    }
    public override void _ExitTree()
    {
        if (Game.Instance != null && ReferenceEquals(Game.Instance.player, this)) Game.Instance.player = null;
    }

}
