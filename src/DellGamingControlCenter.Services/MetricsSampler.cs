namespace DellGamingControlCenter.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class MetricsSampler : IMetricsSampler
{
    private readonly ILoggingService _logger;
    private readonly IDiagnosticEngine _diagnosticEngine;
    private readonly ConcurrentQueue<DiagnosticSnapshot> _buffer = new();
    
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _isPaused;
    
    private const int MaxHistory = 300; // Store up to 5 minutes at 1s intervals

    public event EventHandler<DiagnosticSnapshot>? SnapshotCaptured;

    public MetricsSampler(ILoggingService logger, IDiagnosticEngine diagnosticEngine)
    {
        _logger = logger;
        _diagnosticEngine = diagnosticEngine;
    }

    public void Start(TimeSpan interval)
    {
        Stop();
        _cts = new CancellationTokenSource();
        _isPaused = false;
        _loopTask = Task.Run(() => SampleLoopAsync(interval, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _loopTask?.Wait(); } catch { }
        _cts?.Dispose();
        _cts = null;
    }

    public void Pause() => _isPaused = true;

    public void Resume() => _isPaused = false;

    public IEnumerable<DiagnosticSnapshot> GetHistory() => _buffer.ToList();

    public void Clear() => _buffer.Clear();

    private async Task SampleLoopAsync(TimeSpan interval, CancellationToken token)
    {
        _logger.Log("Metrics sampler started.", LogSeverity.Info, "Sampler");
        while (!token.IsCancellationRequested)
        {
            if (!_isPaused)
            {
                try
                {
                    var snapshot = await _diagnosticEngine.CaptureSnapshotAsync();
                    _buffer.Enqueue(snapshot);
                    
                    if (SnapshotCaptured != null)
                    {
                        foreach (Delegate handler in SnapshotCaptured.GetInvocationList())
                        {
                            try
                            {
                                var action = (EventHandler<DiagnosticSnapshot>)handler;
                                action(this, snapshot);
                            }
                            catch (Exception ex)
                            {
                                _logger.Log($"Snapshot handler error: {ex.Message}", LogSeverity.Warning, "Sampler");
                            }
                        }
                    }
                    
                    while (_buffer.Count > MaxHistory)
                    {
                        _buffer.TryDequeue(out _);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log($"Sampling error: {ex.Message}", LogSeverity.Warning, "Sampler");
                }
            }
            await Task.Delay(interval, token);
        }
    }
}
