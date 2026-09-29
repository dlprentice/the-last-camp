using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace LastCamp;

/// Wall-clock frame intervals and native/managed counters for an uncapped run.
/// Keep render-thread CPU time separate from the game thread and subview GPU
/// timings separate from the main view; none is interchangeable with frame time.
public sealed class FrameMetrics
{
    private readonly List<float> _frames = new(4096);
    private readonly int[] _collections = { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
    private readonly long _allocated = GC.GetTotalAllocatedBytes(false);
    private readonly Rid _main;
    private readonly Rid[] _water;
    private readonly double[] _waterGpu = new double[3];
    private ulong _previous;
    private double _gpu, _renderCpu, _process, _physics, _draws, _triangles;
    private double _vram, _engineMemory, _textureMemory;
    private ulong _streamedMemory;
    private long _managedPeak;

    public FrameMetrics(Viewport viewport, Pond pond)
    {
        _main = viewport.GetViewportRid();
        _water = new[] { pond.reflection_viewport.GetViewportRid(), pond.underwater_viewport.GetViewportRid(), pond.transmission_viewport.GetViewportRid() };
        RenderingServer.ViewportSetMeasureRenderTime(_main, true);
        foreach (Rid rid in _water) RenderingServer.ViewportSetMeasureRenderTime(rid, true);
        _previous = Time.GetTicksUsec();
    }

    public void Sample()
    {
        ulong now = Time.GetTicksUsec();
        _frames.Add((now - _previous) / 1000.0f);
        _previous = now;
        _gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(_main);
        _renderCpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(_main);
        for (int i = 0; i < _water.Length; i++)
            _waterGpu[i] += RenderingServer.ViewportGetMeasuredRenderTimeGpu(_water[i]);
        _process += Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000;
        _physics += Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000;
        _draws += RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        _triangles += RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
        _vram = Math.Max(_vram, Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed));
        _textureMemory = Math.Max(_textureMemory, Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed));
        _streamedMemory = Math.Max(_streamedMemory, TextureStreaming.GetMemoryBudgetBytesUsed());
        _engineMemory = Math.Max(_engineMemory, Performance.GetMonitor(Performance.Monitor.MemoryStatic));
        _managedPeak = Math.Max(_managedPeak, GC.GetTotalMemory(false));
    }

    public Godot.Collections.Dictionary Report()
    {
        var result = CaptureTool.summarise(_frames);
        int n = Math.Max(_frames.Count, 1);
        result["main_view_gpu_ms"] = _gpu / n;
        result["render_thread_cpu_ms"] = _renderCpu / n;
        result["process_monitor_ms"] = _process / n;
        result["physics_monitor_ms"] = _physics / n;
        result["mean_draw_calls"] = _draws / n;
        result["mean_primitives"] = _triangles / n;
        result["reflection_gpu_ms_last_submitted"] = _waterGpu[0] / n;
        result["underwater_gpu_ms_last_submitted"] = _waterGpu[1] / n;
        result["transmission_gpu_ms_last_submitted"] = _waterGpu[2] / n;
        result["peak_render_memory_bytes"] = _vram;
        result["peak_texture_memory_bytes"] = _textureMemory;
        result["peak_streamed_texture_memory_bytes"] = _streamedMemory;
        result["peak_engine_static_memory_bytes"] = _engineMemory;
        result["peak_managed_memory_bytes"] = _managedPeak;
        result["managed_allocated_bytes_per_frame"] = (GC.GetTotalAllocatedBytes(false) - _allocated) / n;
        result["gc_collections"] = new int[] { GC.CollectionCount(0) - _collections[0], GC.CollectionCount(1) - _collections[1], GC.CollectionCount(2) - _collections[2] };
        result["frames_over_33ms"] = _frames.FindAll(f => f > 33.333f).Count;
        result["frames_over_50ms"] = _frames.FindAll(f => f > 50).Count;
        result["frames_over_100ms"] = _frames.FindAll(f => f > 100).Count;
        using Process process = Process.GetCurrentProcess();
        result["working_set_bytes"] = process.WorkingSet64;
        result["peak_working_set_bytes"] = process.PeakWorkingSet64;
        result["frame_intervals_ms"] = _frames.ToArray();
        return result;
    }
}
