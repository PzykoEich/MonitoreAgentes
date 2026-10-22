namespace CentralCollector.Models;

public enum AlertSeverity
{
    Warning,
    Critical
}

public record ServerAlertEvent(
    string Hostname,
    DateTime TimestampUtc,
    AlertSeverity Severity,
    string MetricName,
    string Message,
    double CurrentValue,
    double ThresholdValue
);

    
public class CentralCollectorOptions
{
    public string ExpectedApiKey { get; set; } = string.Empty;
    public string TeamsWebhookUrl { get; set; } = string.Empty;
    public int CooldownMinutes { get; set; } = 10;
}