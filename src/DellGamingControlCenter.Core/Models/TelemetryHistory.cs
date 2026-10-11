namespace DellGamingControlCenter.Core.Models;

using System;

/// <summary>
/// Fixed-capacity ring buffer of telemetry samples for live UI graphs.
/// Push is called from the authoritative <c>MetricsSampler</c> pipeline only —
/// this class never samples on its own and never fabricates values.
/// <c>null</c> means "unavailable at this tick" (telemetry returned null) and is
/// rendered as a gap, never as 0%.
/// Allocation profile: one buffer allocated at construction; Push and CopyTo
/// allocate nothing, so ticking never churns the GC on low-power CPUs.
/// </summary>
public sealed class TelemetryHistory
{
    private readonly double?[] _buffer;
    private int _head;      // next write index
    private int _count;
    private readonly object _lock = new();

    public int Capacity { get; }

    /// <summary>Raised after each Push. Subscribers (graph controls) marshal to the UI thread themselves.</summary>
    public event Action? Updated;

    public TelemetryHistory(int capacity)
    {
        if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
        _buffer = new double?[capacity];
    }

    /// <summary>Record one sample. <paramref name="value"/> may be null (telemetry unavailable).</summary>
    public void Push(double? value)
    {
        lock (_lock)
        {
            _buffer[_head] = value;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++;
        }
        Updated?.Invoke();
    }

    /// <summary>
    /// Copy samples oldest → newest into <paramref name="target"/>. Returns the number
    /// of samples written (≤ target.Length). Values may be null (gap).
    /// </summary>
    public int CopyTo(double?[] target)
    {
        if (target.Length < _buffer.Length) throw new ArgumentOutOfRangeException(nameof(target));
        lock (_lock)
        {
            var start = _count < _buffer.Length ? 0 : _head;
            for (int i = 0; i < _count; i++)
            {
                target[i] = _buffer[(start + i) % _buffer.Length];
            }
            return _count;
        }
    }

    /// <summary>Most recent non-null value, or null when nothing is available.</summary>
    public double? Latest()
    {
        lock (_lock)
        {
            for (int i = 1; i <= _count; i++)
            {
                var v = _buffer[(_head - i + _buffer.Length) % _buffer.Length];
                if (v.HasValue) return v;
            }
        }
        return null;
    }
}
