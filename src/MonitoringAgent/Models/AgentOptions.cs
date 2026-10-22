using System.Data.SqlTypes;

namespace MonitoringAgent.Models;

public class SecurityOptions
{
    public string MonitoringToken { get; set; } = string.Empty;
}

public class ThresholdOptions
{
    public double MemoryUsedPercent { get; set; } = 90.0;
    public double CpuBusyPercent { get; set; } = 85.0;
    public double CpuIoWaitPercent { get; set; } = 30.0;
    public double LoadSaturationPercent { get; set; } = 150.0;
    public double DiskSpaceUsedPercent { get; set; } = 85.0;
    public double DiskInodesUsedPercent { get; set; } = 80.0;
    public double ProcessMemoryMb { get; set; } = 1024.0;
}

public class MonitoringOptions
{
    public int IntervalSeconds { get; set; } = 5;
    public SecurityOptions Security { get; set; } = new();
    public ThresholdOptions Thresholds { get; set; } = new();
    public List<string> MonitoredServices { get; set; } = new();
    public CentralServerOptions CentralServer {get;set;} = new();
    //Para pruebas de parches de seguridad -_- no e usado git 
    public SecurityPatchAuditOptions SecurityAudit { get; set; } = new ();
    public List<string> MonitoredProcesses { get; set; } = new();
}

public class CentralServerOptions
{
    public string IngressUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

//Pruebas para los parches de seguridad

public class SecurityPatchAuditOptions
{
    public bool Enable { get; set; } = true;
    public int CheckIntervalHours { get; set; } = 720;
}

public class MonitoredProcessRule
{
    public string Name { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public double MaxMemoryMegabytes { get; set; } = 1024.0;
    public bool AlertIfDown { get; set; } = true;
}