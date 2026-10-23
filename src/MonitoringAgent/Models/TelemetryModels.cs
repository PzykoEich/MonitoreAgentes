namespace MonitoringAgent.Models;

public enum AlertSeverity
{
    Warning,
    Critical
}
public record MemoryMetrics(
    double TotalMegabytes,
    double AvailableMegaBytes,
    double UsedPercent
);

public record CpuMetrics(
    double UserPercent,
    double SystemPercent,
    double IdlePercent,
    double IoWaitPercent,
    double StealPercent,
    double TotalBusyPercent
);

public record LoadMetrics(
    double Load1Min,
    double Load5Min,
    double Load15Min,
    int LogicalCores,
    double SaturationPercent
);

public record DiskMetrics(
    string MountPoint,
    string DriveFormat,
    double TotalGigabytes,
    double FreeGigabytes,
    double SpaceUsedPercent,
    long TotalInodes,
    long FreeInodes,
    double InodesUsedPercent
);

public record ServiceStatus(
    string Name,
    bool IsActive,
    string Description
);

public record SystemTelemetryReport(
    string Hostname,
    DateTime TimestampUtc,
    MemoryMetrics Memory,
    CpuMetrics Cpu,
    LoadMetrics Load,
    List<DiskMetrics> Disks,
    List<ServiceStatus> Services,
    List<ThermalSensor>? Temperature = null
);

public record ServerAlertEvent(
    string Hostname,
    DateTime TimestampUtc,
    AlertSeverity Severity,
    string MetricName,
    string Message,
    double CurrentValue,
    double ThresholdValue
);

public record SecurityUpdatesSnapshot(
    bool HasPendingSecurityUpdates,
    int CriticalUpdatesCount,
    int ImportantUpdatesCount,
    int TotalSecurityUpdates,
    DateTime LastCheckedUtc,
    string Details
);

public record MonitoredProcessStatus(
    string TargetPatterns,
    bool IsRunning,
    int InstanceCount,
    List<int> ProcessIds,
    double TotalMemoryMegabytes
);

public record ThermalSensor(
    string DeviceName,
    string SensorLabel,
    double TemperatureCelsius,
    double? CriticalThresholdCelsius
);