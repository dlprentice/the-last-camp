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

/// Persistent, local disturbances added to the pond's analytic base waves.
/// UV = (world_xz - bounds.position) / bounds.size; samples lie at texel centres.
/// RGBA = height (m), vertical velocity (m/s), foam [0,1], water mask (1 = water).
/// This is a damped wave equation, not a mass-conserving shallow-water/3D solver.
/// Main-RD ownership follows Godot's native compute texture example:
/// https://github.com/godotengine/godot-demo-projects/tree/master/compute/texture
/// https://docs.godotengine.org/en/stable/classes/class_renderingdevice.html#class-renderingdevice-method-buffer-get-data-async
/// The integration and solver below are original; no addon or copied demo shader.
public partial class PondSimulation : RefCounted
{
    [Signal]
    public delegate void ready_changedEventHandler(bool available);

    public const double FIXED_STEP = 1.0 / 120.0;
    public const long MAX_SUBSTEPS = 8;
    public const long MAX_IMPULSES = 32;
    public const long MAX_WAKES = 8;
    public const long MAX_PROBES = 32;
    public const double MAX_HEIGHT = 0.055;
    public const double MAX_VELOCITY = 0.8;
    public const double WAVE_SPEED = 1.65;
    public const double VELOCITY_DAMPING = 0.65;
    public const double HEIGHT_DAMPING = 0.04;
    public const double FOAM_DECAY = 0.42;
    public const double PROBE_INTERVAL = 1.0 / 30.0;
    public const string SIMULATION_SHADER = "res://shaders/compute/pond_simulation.glsl";
    public const string PROBE_SHADER = "res://shaders/compute/pond_probes.glsl";

    /// Set false before setup for an explicitly disabled, neutral fallback.
    public bool enabled = true;

    public Rect2 _bounds = new Rect2();
    public long _resolution = 256;
    public bool _active = false;
    public bool _ready = false;
    public long _session = 0;
    public double _accumulator = 0.0;
    public double _simulation_time = 0.0;
    public double _probe_elapsed = 0.0;
    public Godot.Collections.Array<Vector4> _impulses = new Godot.Collections.Array<Vector4>();
    public Godot.Collections.Array<Vector4> _wake_starts = new Godot.Collections.Array<Vector4>();
    public Godot.Collections.Array<Vector4> _wake_ends = new Godot.Collections.Array<Vector4>();
    public List<Vector2> _probe_positions = new List<Vector2>();
    public List<Vector2> _sampled_positions = new List<Vector2>();
    public List<float> _probe_heights = new List<float>();
    public long _probe_layout_id = 0;
    public long _probe_generation = 0;
    public long _probe_serial = 0;
    public long _sampled_serial = -1;
    public double _sampled_time = -1.0;
    public ImageTexture _neutral;
    public Texture2D _texture;
    public PondSimulation.GPUState _gpu;

    public PondSimulation()
    {
        Image zero = Image.CreateEmpty(1, 1, false, Image.Format.Rgbaf);
        zero.Fill(new Color(0, 0, 0, 0));
        _neutral = ImageTexture.CreateFromImage(zero);
        _texture = _neutral;
    }

    public bool setup(Rect2 bounds, Image solid_mask, long resolution = 256)
    {
        /// Red >= 0.5 in solid_mask denotes land/rock; black denotes water.
        /// Returns whether GPU setup was queued, not whether it has completed.
        /// A new setup is a fresh session: prior disturbances/probes are discarded.
        shutdown();
        _bounds = bounds;
        _resolution = clampi(resolution, 32, 512);
        if (!bounds.Position.IsFinite() || !bounds.Size.IsFinite() || bounds.Size.X <= 0.0 || bounds.Size.Y <= 0.0)
        {
            G.push_warning("PondSimulation: bounds must have finite, positive size.");
            return false;
        }
        if (!enabled || DisplayServer.GetName() == "headless" || RenderingServer.GetRenderingDevice() == null)
        {
            return false;
        }
        RDShaderFile simulation_file = Content.Load<RDShaderFile>(SIMULATION_SHADER);
        RDShaderFile probe_file = Content.Load<RDShaderFile>(PROBE_SHADER);
        if (simulation_file == null || probe_file == null)
        {
            G.push_warning("PondSimulation: compute shaders must be imported before use.");
            return false;
        }
        RDShaderSpirV simulation_spirv = simulation_file.GetSpirV();
        RDShaderSpirV probe_spirv = probe_file.GetSpirV();
        if (!(simulation_spirv.GetStageCompileError(RenderingDevice.ShaderStage.Compute).Length == 0) || !(probe_spirv.GetStageCompileError(RenderingDevice.ShaderStage.Compute).Length == 0))
        {
            G.push_warning("PondSimulation: a compute shader failed to compile; using neutral water.");
            return false;
        }
        Image mask = null;
        if (solid_mask != null && !solid_mask.IsEmpty())
        {
            mask = solid_mask.Duplicate() as Image;
            if (mask.GetWidth() != _resolution || mask.GetHeight() != _resolution)
            {
                mask.Resize((int)_resolution, (int)_resolution, Image.Interpolation.Nearest);
            }
        }
        List<float> initial = new List<float>();
        G.resize(initial, (int)(_resolution * _resolution * 4));
        for (long z = 0; z < _resolution; z++)
        {
            for (long x = 0; x < _resolution; x++)
            {
                double water = 1.0;
                if (mask != null && mask.GetPixel((int)x, (int)z).R >= 0.5)
                {
                    water = 0.0;
                }
                initial[(int)((z * _resolution + x) * 4 + 3)] = (float)water;
            }
        }
        _active = true;
        _gpu = new PondSimulation.GPUState();
        _gpu.owner = GodotObject.WeakRef(this);
        _gpu.session = _session;
        _gpu.bounds = _bounds;
        _gpu.resolution = _resolution;
        PondSimulation.GPUState state = _gpu;
        List<byte> bytes = G.to_byte_array(initial);
        RenderingServer.CallOnRenderThread(Callable.From(() => state.initialize(bytes, simulation_spirv, probe_spirv)));
        return true;
    }

    public bool is_ready()
    {
        return _ready;
    }

    public Texture2D get_texture()
    {
        /// Neutral until ready_changed(true); then this is one stable Texture2DRD.
        /// Rebind after setup becomes ready. Its RID does not rotate during stepping.
        return _texture;
    }

    public void queue_impulse(Vector2 world_xz, double radius, double velocity_impulse)
    {
        /// A signed velocity kick in m/s. A balanced radial profile limits level drift.
        /// Events exceeding a dispatch's capacity remain queued for the next dispatch.
        if (!_active || !world_xz.IsFinite() || !is_finite(radius) || !is_finite(velocity_impulse))
        {
            return;
        }
        double minimum_radius = maxf(_bounds.Size.X, _bounds.Size.Y) / (double)_resolution * 1.25;
        _impulses.Add(new Vector4(world_xz.X, world_xz.Y, (float)maxf(radius, minimum_radius), (float)clampf(velocity_impulse, -MAX_VELOCITY, MAX_VELOCITY)));
    }

    public void queue_wake(Vector2 from_xz, Vector2 to_xz, double radius, double strength)
    {
        /// Difference of two hull footprints: movement deposits a signed bow/stern wake.
        /// strength is m/s; the footprint difference already scales with travel distance.
        /// Do not call with an artificial oscillation when the hull has not moved.
        if (!_active || !from_xz.IsFinite() || !to_xz.IsFinite() || !is_finite(radius) || !is_finite(strength))
        {
            return;
        }
        if (from_xz.DistanceSquaredTo(to_xz) < 0.00000001)
        {
            return;
        }
        double minimum_radius = maxf(_bounds.Size.X, _bounds.Size.Y) / (double)_resolution * 1.5;
        _wake_starts.Add(new Vector4(from_xz.X, from_xz.Y, (float)maxf(radius, minimum_radius), (float)clampf(strength, -MAX_VELOCITY, MAX_VELOCITY)));
        _wake_ends.Add(new Vector4(to_xz.X, to_xz.Y, 0.0f, 0.0f));
    }

    public void set_probe_positions(List<Vector2> positions, long layout_id = 0)
    {
        /// Keep stable slots while moving. Increment layout_id when assigning slots to
        /// different bodies, even if the count is unchanged. Layout changes invalidate
        /// in-flight results; ordinary motion deliberately allows delayed GPU samples.
        long count = mini((long)positions.Count, MAX_PROBES);
        if (count != (long)_probe_positions.Count || layout_id != _probe_layout_id)
        {
            _probe_generation += 1;
            _probe_layout_id = layout_id;
            _sampled_positions = new List<Vector2>();
            _probe_heights = new List<float>();
            G.resize(_probe_heights, (int)count);
            _sampled_time = -1.0;
            _sampled_serial = -1;
        }
        _probe_positions = G.slice(positions, 0, count);
    }

    public List<float> get_probe_heights()
    {
        /// Residual heights only, in the same stable slot order; zero until sampled.
        return new List<float>(_probe_heights);
    }

    public List<Vector2> get_sampled_probe_positions()
    {
        return new List<Vector2>(_sampled_positions);
    }

    public double get_probe_sample_time()
    {
        /// Simulation time represented by the returned samples, or -1 before sampling.
        return _sampled_time;
    }

    public double get_simulation_time()
    {
        return _simulation_time;
    }

    public void step(double delta, Vector2? flow_opt = null)
    {
        Vector2 flow = flow_opt ?? Vector2.Zero;
        if (!_active || !is_finite(delta) || delta <= 0.0)
        {
            return;
        }
        _accumulator += delta;
        if (!_ready)
        {
            return;
        }
        long count = mini(floori((_accumulator + 0.000000001) / FIXED_STEP), MAX_SUBSTEPS);
        if (count == 0)
        {
            return;
        }
        // Eight steps cover 15 fps. Remainders and hitch backlog are retained rather
        // than changing the numerical timestep or dropping queued contact events.
        _accumulator = maxf(_accumulator - count * FIXED_STEP, 0.0);
        _simulation_time += count * FIXED_STEP;
        _probe_elapsed += count * FIXED_STEP;
        long impulse_count = mini((long)_impulses.Count, MAX_IMPULSES);
        long wake_count = mini((long)_wake_starts.Count, MAX_WAKES);
        List<float> inputs = new List<float>();
        G.resize(inputs, (int)((MAX_IMPULSES + MAX_WAKES * 2) * 4));
        for (long i = 0; i < impulse_count; i++)
        {
            _write_vector(inputs, i * 4, G.pop_front(_impulses));
        }
        for (long i2 = 0; i2 < wake_count; i2++)
        {
            _write_vector(inputs, (MAX_IMPULSES + i2) * 4, G.pop_front(_wake_starts));
            _write_vector(inputs, (MAX_IMPULSES + MAX_WAKES + i2) * 4, G.pop_front(_wake_ends));
        }
        List<Vector2> probes = new List<Vector2>();
        if (!(_probe_positions.Count == 0) && _probe_elapsed >= PROBE_INTERVAL)
        {
            _probe_elapsed = fmod(_probe_elapsed, PROBE_INTERVAL);
            probes = new List<Vector2>(_probe_positions);
            _probe_serial += 1;
        }
        Vector2 safe_flow = flow.IsFinite() ? flow.LimitLength(0.35f) : Vector2.Zero;
        long generation = _probe_generation;
        long serial = _probe_serial;
        double sample_time = _simulation_time;
        PondSimulation.GPUState state = _gpu;
        List<byte> bytes = G.to_byte_array(inputs);
        RenderingServer.CallOnRenderThread(Callable.From(() => state.advance(count, bytes, impulse_count, wake_count, safe_flow, probes, generation, serial, sample_time)));
    }

    public void shutdown()
    {
        bool was_ready = _ready;
        _active = false;
        _ready = false;
        _session += 1;
        _texture = _neutral;
        _accumulator = 0.0;
        _simulation_time = 0.0;
        _probe_elapsed = 0.0;
        _impulses.Clear();
        _wake_starts.Clear();
        _wake_ends.Clear();
        _probe_generation += 1;
        G.fill(_probe_heights, 0.0f);
        _sampled_positions = new List<Vector2>();
        _sampled_time = -1.0;
        _sampled_serial = -1;
        _release_gpu();
        if (was_ready)
        {
            EmitSignal(SignalName.ready_changed, false);
        }
    }

    public override void _Notification(int what)
    {
        if (what == GodotObject.NotificationPredelete && _gpu != null)
        {
            // During RefCounted predelete, calling another method through self may
            // already see a null instance. Retain the independent state directly.
            PondSimulation.GPUState state = _gpu;
            _gpu = null;
            RenderingServer.CallOnRenderThread(Callable.From(() => state.dispose()));
        }
    }

    public void _release_gpu()
    {
        if (_gpu == null)
        {
            return;
        }
        // The closure retains only GPUState, not this possibly departing scene owner.
        PondSimulation.GPUState state = _gpu;
        _gpu = null;
        RenderingServer.CallOnRenderThread(Callable.From(() => state.dispose()));
    }

    public void _on_ready(long session, bool available)
    {
        if (session != _session || !_active)
        {
            return;
        }
        _ready = available;
        if (available)
        {
            _texture = _gpu.display;
        }
        else
        {
            _active = false;
            G.push_warning("PondSimulation: GPU setup unavailable; using neutral water.");
        }
        EmitSignal(SignalName.ready_changed, available);
    }

    public void _on_probes(long session, long generation, long serial, double sample_time, List<Vector2> positions, List<byte> bytes)
    {
        if (!_active || session != _session || generation != _probe_generation || serial <= _sampled_serial)
        {
            return;
        }
        if ((long)bytes.Count != (long)positions.Count * 4)
        {
            return;
        }
        _probe_heights = G.to_float32_array(bytes);
        _sampled_positions = positions;
        _sampled_serial = serial;
        _sampled_time = sample_time;
    }

    public static void _write_vector(List<float> data, long offset, Vector4 value)
    {
        data[(int)offset] = value.X;
        data[(int)(offset + 1)] = value.Y;
        data[(int)(offset + 2)] = value.Z;
        data[(int)(offset + 3)] = value.W;
    }

    /// All mutable members below belong to the render thread. Callbacks use a weak
    /// scene owner; queued jobs hold this state alive through setup and disposal.
    public partial class GPUState
    {
        public WeakRef owner;
        public long session;
        public Rect2 bounds;
        public long resolution;
        public Texture2Drd display = new Texture2Drd();
        public RenderingDevice rd;
        public Godot.Collections.Array<Rid> resources = new Godot.Collections.Array<Rid>();
        public Godot.Collections.Array<Rid> textures = new Godot.Collections.Array<Rid>();
        public Godot.Collections.Array<Rid> sets = new Godot.Collections.Array<Rid>();
        public Godot.Collections.Array<Rid> probe_sets = new Godot.Collections.Array<Rid>();
        public Rid pipeline = new Rid();
        public Rid probe_pipeline = new Rid();
        public Rid interactions = new Rid();
        public Rid probe_input = new Rid();
        public Rid probe_output = new Rid();
        public long current = 0;
        public bool disposed = false;
        public double wave_speed = PondSimulation.WAVE_SPEED;
        public Vector2 cell = Vector2.One;

        public void initialize(List<byte> initial, RDShaderSpirV shader_spirv, RDShaderSpirV probe_spirv)
        {
            rd = RenderingServer.GetRenderingDevice();
            if (rd == null)
            {
                _notify_ready(false);
                return;
            }
            cell = bounds.Size / (float)(double)resolution;
            // Conservative CFL bound for a rectangular five-point wave stencil.
            wave_speed = minf(PondSimulation.WAVE_SPEED, 0.65 / (PondSimulation.FIXED_STEP * sqrt(1.0 / ((double)cell.X * cell.X) + 1.0 / ((double)cell.Y * cell.Y))));
            Rid shader = _own(rd.ShaderCreateFromSpirV(shader_spirv));
            Rid probe_shader = _own(rd.ShaderCreateFromSpirV(probe_spirv));
            if (!shader.IsValid || !probe_shader.IsValid)
            {
                _fail();
                return;
            }
            pipeline = _own(rd.ComputePipelineCreate(shader));
            probe_pipeline = _own(rd.ComputePipelineCreate(probe_shader));
            RDTextureFormat format = new RDTextureFormat();
            format.Format = RenderingDevice.DataFormat.R32G32B32A32Sfloat;
            format.TextureType = RenderingDevice.TextureType.Type2D;
            format.Width = unchecked((uint)(resolution));
            format.Height = unchecked((uint)(resolution));
            format.Depth = unchecked((uint)(1));
            format.ArrayLayers = unchecked((uint)(1));
            format.Mipmaps = unchecked((uint)(1));
            format.UsageBits = (RenderingDevice.TextureUsageBits)((long)RenderingDevice.TextureUsageBits.StorageBit | (long)RenderingDevice.TextureUsageBits.SamplingBit | (long)RenderingDevice.TextureUsageBits.CanCopyFromBit | (long)RenderingDevice.TextureUsageBits.CanCopyToBit);
            if (!rd.TextureIsFormatSupportedForUsage(format.Format, format.UsageBits))
            {
                _fail();
                return;
            }
            for (long i = 0; i < 3; i++)
            {
                textures.Add(_own(rd.TextureCreate(format, new RDTextureView(), new Godot.Collections.Array<byte[]> { initial.ToArray() })));
            }
            interactions = _own(rd.StorageBufferCreate((uint)((PondSimulation.MAX_IMPULSES + PondSimulation.MAX_WAKES * 2) * 16)));
            probe_input = _own(rd.StorageBufferCreate((uint)(PondSimulation.MAX_PROBES * 8)));
            probe_output = _own(rd.StorageBufferCreate((uint)(PondSimulation.MAX_PROBES * 4)));
            if (!pipeline.IsValid || !probe_pipeline.IsValid || !interactions.IsValid || !probe_input.IsValid || !probe_output.IsValid || G.any(textures, (Rid value) => !value.IsValid))
            {
                _fail();
                return;
            }
            for (long i2 = 0; i2 < 2; i2++)
            {
                sets.Add(_own(rd.UniformSetCreate(new Godot.Collections.Array<RDUniform> { _uniform(0, (long)RenderingDevice.UniformType.Image, textures[(int)i2]), _uniform(1, (long)RenderingDevice.UniformType.Image, textures[(int)(1 - i2)]), _uniform(2, (long)RenderingDevice.UniformType.StorageBuffer, interactions) }, shader, 0)));
                probe_sets.Add(_own(rd.UniformSetCreate(new Godot.Collections.Array<RDUniform> { _uniform(0, (long)RenderingDevice.UniformType.Image, textures[(int)i2]), _uniform(1, (long)RenderingDevice.UniformType.StorageBuffer, probe_input), _uniform(2, (long)RenderingDevice.UniformType.StorageBuffer, probe_output) }, probe_shader, 0)));
            }
            if (G.any(sets, (Rid value2) => !value2.IsValid) || G.any(probe_sets, (Rid value3) => !value3.IsValid))
            {
                _fail();
                return;
            }
            display.TextureRdRid = textures[2];
            _notify_ready(true);
        }

        public void advance(long steps, List<byte> inputs, long impulse_count, long wake_count, Vector2 flow, List<Vector2> probes, long generation, long serial, double sample_time)
        {
            if (disposed || !pipeline.IsValid)
            {
                return;
            }
            rd.BufferUpdate(interactions, 0, (uint)(long)inputs.Count, inputs.ToArray());
            List<float> probe_bytes = new List<float>();
            foreach (Vector2 point in probes)
            {
                probe_bytes.Add(point.X);
                probe_bytes.Add(point.Y);
            }
            if (!(probe_bytes.Count == 0))
            {
                rd.BufferUpdate(probe_input, 0, (uint)((long)probe_bytes.Count * 4), G.to_byte_array(probe_bytes).ToArray());
            }
            List<float> push = new List<float>(new List<float> { bounds.Position.X, bounds.Position.Y, bounds.Size.X, bounds.Size.Y, (float)PondSimulation.FIXED_STEP, (float)(wave_speed * wave_speed), (float)PondSimulation.VELOCITY_DAMPING, (float)PondSimulation.HEIGHT_DAMPING, (float)PondSimulation.FOAM_DECAY, (float)PondSimulation.MAX_HEIGHT, (float)PondSimulation.MAX_VELOCITY, 0.0f, flow.X, flow.Y, cell.X, cell.Y, (float)(double)resolution, (float)(double)impulse_count, (float)(double)wake_count, 0.0f });
            long groups = ceili((double)resolution / 8.0);
            long compute = rd.ComputeListBegin();
            rd.ComputeListBindComputePipeline(compute, pipeline);
            for (long substep = 0; substep < steps; substep++)
            {
                // A point impulse is a velocity kick, not acceleration: apply it once.
                push[17] = (float)(substep == 0 ? (double)impulse_count : 0.0);
                push[18] = (float)(substep == 0 ? (double)wake_count : 0.0);
                rd.ComputeListBindUniformSet(compute, sets[(int)current], 0);
                rd.ComputeListSetPushConstant(compute, G.to_byte_array(push).ToArray(), 80);
                rd.ComputeListDispatch(compute, (uint)groups, (uint)groups, 1);
                rd.ComputeListAddBarrier(compute);
                current = 1 - current;
            }
            if (!(probes.Count == 0))
            {
                List<float> probe_push = new List<float>(new List<float> { bounds.Position.X, bounds.Position.Y, bounds.Size.X, bounds.Size.Y, (float)(double)resolution, (float)(double)(long)probes.Count, 0.0f, 0.0f });
                rd.ComputeListBindComputePipeline(compute, probe_pipeline);
                rd.ComputeListBindUniformSet(compute, probe_sets[(int)current], 0);
                rd.ComputeListSetPushConstant(compute, G.to_byte_array(probe_push).ToArray(), 32);
                rd.ComputeListDispatch(compute, 1, 1, 1);
            }
            rd.ComputeListEnd();
            // One stable display image avoids Texture2DRD/RID rebinding every frame.
            rd.TextureCopy(textures[(int)current], textures[2], Vector3.Zero, Vector3.Zero, new Vector3(resolution, resolution, 1), 0, 0, 0, 0);
            if (!(probes.Count == 0))
            {
                WeakRef weak_owner = owner;
                long request_session = session;
                rd.BufferGetDataAsync(probe_output, Callable.From((Variant bytes) =>
{
    Variant target = weak_owner.GetRef();
    if (target.VariantType != Variant.Type.Nil)
    {
        G.Call(target, "call_deferred", "_on_probes", request_session, generation, serial, sample_time, Variant.From(probes.ToArray()), bytes);
    }
}), 0, (uint)((long)probes.Count * 4));
            }
        }

        public void dispose()
        {
            // The main RenderingDevice submits normally. Never submit()/sync() it here.
            if (disposed)
            {
                return;
            }
            disposed = true;
            display.TextureRdRid = new Rid();
            if (rd != null)
            {
                // Reverse creation order releases sets before buffers/textures/shaders.
                for (long i = (long)resources.Count - 1; i > -1; i += -1)
                {
                    rd.FreeRid(resources[(int)i]);
                }
            }
            resources.Clear();
            textures.Clear();
            sets.Clear();
            probe_sets.Clear();
            pipeline = new Rid();
            probe_pipeline = new Rid();
            rd = null;
        }

        public Rid _own(Rid rid)
        {
            if (rid.IsValid)
            {
                resources.Add(rid);
            }
            return rid;
        }

        public RDUniform _uniform(long binding, long type, Rid rid)
        {
            RDUniform uniform = new RDUniform();
            uniform.Binding = (int)binding;
            uniform.UniformType = (RenderingDevice.UniformType)type;
            uniform.AddId(rid);
            return uniform;
        }

        public void _notify_ready(bool available)
        {
            Variant target = owner.GetRef();
            if (target.VariantType != Variant.Type.Nil)
            {
                G.Call(target, "call_deferred", "_on_ready", session, available);
            }
        }

        public void _fail()
        {
            dispose();
            _notify_ready(false);
        }
    }
}
