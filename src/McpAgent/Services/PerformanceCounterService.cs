using System.Diagnostics;
using McpAgent.Models;
using Microsoft.Extensions.Logging;

namespace McpAgent.Services;

public class PerformanceCounterService : IPerformanceCounterService
{
    private readonly ILogger<PerformanceCounterService> _logger;
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _memoryCounter;
    
    public PerformanceCounterService(ILogger<PerformanceCounterService> logger)
    {
        _logger = logger;
        InitializeCounters();
    }
    
    private void InitializeCounters()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Performance counters not available on this system");
        }
    }
    
    public async Task<SystemMetrics> GetCurrentMetricsAsync()
    {
        await Task.Delay(100); // Performance counter 안정화
        
        var metrics = new SystemMetrics();
        
        try
        {
            if (_cpuCounter != null)
            {
                metrics.CpuUsagePercentage = _cpuCounter.NextValue();
            }
            
            if (_memoryCounter != null)
            {
                metrics.AvailableMemoryMB = (long)_memoryCounter.NextValue();
            }
            
            // 플랫폼 독립적인 메트릭
            metrics.TotalMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024);
            metrics.RunningProcessCount = Process.GetProcesses().Length;
            metrics.SystemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error collecting system metrics");
        }
        
        return metrics;
    }
    
    public void Dispose()
    {
        _cpuCounter?.Dispose();
        _memoryCounter?.Dispose();
    }
}