using MonitoringAgent.Models;

namespace MonitoringAgent.Services;

public class MetricsStateStore
{
    private SystemTelemetryReport? _latestReport;
    private SecurityUpdatesSnapshot? _latestSecuritySnapshot;
    private readonly object _lock = new object();

    public void Update(SystemTelemetryReport report)
    {
        lock (_lock)
        {
            _latestReport = report;
        }
    }

    public void UpdateSecuritySnapshot(SecurityUpdatesSnapshot snapshot)
    {
        lock(_lock) _latestSecuritySnapshot = snapshot;
    }

    public SecurityUpdatesSnapshot? GetLatestSecuritySnapshot()
    {
        lock(_lock) return _latestSecuritySnapshot;
    }

    public SystemTelemetryReport? GetLatest()
    {
        lock (_lock)
        {
            return _latestReport;
        }
    }
}