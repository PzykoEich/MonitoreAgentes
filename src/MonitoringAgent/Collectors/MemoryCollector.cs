using MonitoringAgent.Models;

namespace MonitoringAgent.Collectors;

public static class MemoryCollector
{
    private const string MemInfoPath = "/proc/meminfo";

    public static MemoryMetrics Collect()
    {
        //Futuro para Windows también
        if (!OperatingSystem.IsLinux())
        {
            return new MemoryMetrics(0, 0, 0);
        }

        long memTotalkb = 0;
        long memAvailablekb = 0;

        //Abrir descriptor de archivos virutal Solo lectora!!!! 
        using var stream = new FileStream(MemInfoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        string? line;

        while((line = reader.ReadLine()) != null)
        {
            //!!!! usar StringComparison.Ordinal evitar comprovaciones culturales
            if(line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                memTotalkb = ParseKilobytes(line);
            }
            else if(line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                memAvailablekb = ParseKilobytes(line);
            }

            //Evitar leer todo el archivo si ya tenemos los datos necesarios
            if(memTotalkb > 0 && memAvailablekb > 0) break;
        }
        double totalMb = memTotalkb / 1024.0;
        double availableMb = memAvailablekb / 1024.0;
        double usedPercent = totalMb > 0 ? (totalMb - availableMb) / totalMb * 100 : 0;


        return new MemoryMetrics(TotalMegabytes: Math.Round(totalMb, 2), AvailableMegaBytes: Math.Round(availableMb, 2), UsedPercent: Math.Round(usedPercent, 2));
    }

    private static long ParseKilobytes(string line)
    {
        ReadOnlySpan<char> span = line.AsSpan();
        int start = -1;
        int end = -1;

        for (int i = 0; i < span.Length; i++)
        {
            if (char.IsDigit(span[i]))
            {
                if (start == -1)
                {
                    start = i;
                }
                end = i;
            }
            else if (start != -1)
            {
                break;
            }
        }

        if(start != -1 && end != -1 && long.TryParse(span.Slice(start, end - start + 1), out long value))
        {
            return value;
        }
        return 0;
    }
}