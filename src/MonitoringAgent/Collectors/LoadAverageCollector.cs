using MonitoringAgent.Models;

namespace MonitoringAgent.Collectors;

public static class LoadAverageCollector
{
    private const string LoadAveragePath = "/proc/loadavg";

    public static LoadMetrics Collect()
    {
        int cores = Environment.ProcessorCount;

        //Ya te la sabes 
        if(!OperatingSystem.IsLinux())
        {
            return new LoadMetrics(0, 0, 0, cores, 0);
        }

        using var stream = new FileStream(LoadAveragePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream);

        string? line = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
        {
            return new LoadMetrics(0, 0, 0, cores, 0);
        }
        ReadOnlySpan<char> span =  line.AsSpan().Trim();
        //Cargar load 1 min
        int spaceIdx = span.IndexOf(' ');
        double.TryParse(span.Slice(0, spaceIdx), out double load1Min);
        span = span.Slice(spaceIdx + 1).Trim();
        //Cargar load 5 min
        spaceIdx = span.IndexOf(' ');
        double.TryParse(span.Slice(0, spaceIdx), out double load5Min);
        span = span.Slice(spaceIdx + 1).Trim();
        //Cargar load 15 min
        spaceIdx = span.IndexOf(' ');
        ReadOnlySpan<char> load15minPart = spaceIdx == -1 ? span : span.Slice(0, spaceIdx);
        double.TryParse(load15minPart, out double load15Min);

        //Calcular saturación NORMALIZADA -_-
        double saturation = cores > 0 
            ? Math.Round((load1Min / cores) * 100, 2)
            : 0.0;

        return new LoadMetrics(
            Load1Min: Math.Round(load1Min, 2),
            Load5Min: Math.Round(load5Min, 2),
            Load15Min: Math.Round(load15Min, 2),
            LogicalCores: cores,
            SaturationPercent: saturation
        );
    }
}