namespace DellGamingControlCenter.Core.Interfaces;

using System;
using System.Collections.Generic;
using DellGamingControlCenter.Core.Models;

public interface IMetricsSampler
{
    void Start(TimeSpan interval);
    void Stop();
    void Pause();
    void Resume();
    
    /// <summary>Gets the recent history buffer of snapshots.</summary>
    IEnumerable<DiagnosticSnapshot> GetHistory();
    
    /// <summary>Clears the buffer.</summary>
    void Clear();
}
