namespace DellGamingControlCenter.Core.Interfaces;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Models;

public interface IFpsProvider
{
    /// <summary>Returns true if the current system can provide FPS/Frame-time data.</summary>
    bool IsAvailable();

    /// <summary>Starts capturing frame data for the specified process.</summary>
    Task StartCaptureAsync(int processId);

    /// <summary>Stops capturing frame data.</summary>
    Task StopCaptureAsync();

    /// <summary>Retrieves all raw samples captured since the last flush.</summary>
    List<FpsSample> FlushSamples();
    
    /// <summary>Calculates statistics from a set of samples.</summary>
    FpsStatistics CalculateStatistics(IEnumerable<FpsSample> samples);
}
