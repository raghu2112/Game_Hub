namespace DellGamingControlCenter.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class UnavailableFpsProvider : IFpsProvider
{
    public bool IsAvailable()
    {
        return false;
    }

    public Task StartCaptureAsync(int processId)
    {
        return Task.CompletedTask;
    }

    public Task StopCaptureAsync()
    {
        return Task.CompletedTask;
    }

    public List<FpsSample> FlushSamples()
    {
        return new List<FpsSample>();
    }
    
    public FpsStatistics CalculateStatistics(IEnumerable<FpsSample> samples)
    {
        if (samples == null || !samples.Any()) return new FpsStatistics();

        var fpsList = samples.Select(s => s.Fps).OrderBy(f => f).ToList();
        var timesList = samples.Select(s => s.FrameTimeMs).ToList();

        int onePercentIndex = Math.Max(0, (int)(fpsList.Count * 0.01));
        int zeroOnePercentIndex = Math.Max(0, (int)(fpsList.Count * 0.001));

        return new FpsStatistics
        {
            AverageFPS = fpsList.Average(),
            MinFPS = fpsList.First(),
            MaxFPS = fpsList.Last(),
            OnePercentLow = fpsList[onePercentIndex],
            ZeroPointOnePercentLow = fpsList[zeroOnePercentIndex],
            AverageFrameTimeMs = timesList.Average(),
            FrameTimeVariance = timesList.Any() ? timesList.Max() - timesList.Min() : 0
        };
    }
}
