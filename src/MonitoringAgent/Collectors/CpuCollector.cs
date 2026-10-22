using MonitoringAgent.Models;

namespace MonitoringAgent.Collectors;

public record RawCpuSnapshot(
    long User,
    long Nice,
    long System,
    long Idle,
    long IoWait,
    long Irq,
    long SoftIrq,
    long Steal
)
{
    public long Total => User + Nice + System + Idle + IoWait + Irq + SoftIrq + Steal;
}

public static class CpuCollector
{
    private const string StatPath = "/proc/stat";

    public static RawCpuSnapshot ReadSnapshot()
    {
        //checamos para windows.... muy pronto :D
        if (!OperatingSystem.IsLinux())
        {
            return new RawCpuSnapshot(0, 0, 0, 0, 100, 0, 0, 0);
        }

        using var stream = new FileStream(StatPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        string? line = reader.ReadLine();
    
        if (string.IsNullOrEmpty(line) || !line.StartsWith("cpu ", StringComparison.Ordinal))
        {
            return new RawCpuSnapshot(0, 0, 0, 0, 0, 0, 0, 0);
        }

        //Saltamos los primeros 4 caracteres "cpu "
        ReadOnlySpan<char> span = line.AsSpan(4).TrimStart();

        //Creamos un stackalloc para evitar asignaciones en el heap => Trucaso
        Span<long> fields = stackalloc long[8];
        int fieldIndex = 0;

        while(!span.IsEmpty && fieldIndex < fields.Length)
        {
            int nextSpace = span.IndexOf(' ');
            ReadOnlySpan<char> token = nextSpace == -1 ? span : span.Slice(0, nextSpace); 

            if (long.TryParse(token, out long value))
            {
                fields[fieldIndex++] = value;
            }
            if (nextSpace == -1)
            {
                break;
            }
            span = span.Slice(nextSpace).TrimStart();
        }

        return new RawCpuSnapshot(
            User: fields[0],
            Nice: fields[1],
            System: fields[2],
            Idle: fields[3],
            IoWait: fields[4],
            Irq: fields[5],
            SoftIrq: fields[6],
            Steal: fields[7]
        );

    }

    public static CpuMetrics CalculateUsage(RawCpuSnapshot previous, RawCpuSnapshot current)
    {
        long deltaTotal = current.Total - previous.Total;

        if (deltaTotal <= 0)
        {
            return new CpuMetrics(0, 0, 100, 0, 0, 0);
        }

        long dUser = (current.User + current.Nice) - (previous.User + previous.Nice);
        long dSystem = (current.System + current.Irq + current.SoftIrq) - (previous.System + previous.Irq + previous.SoftIrq);
        long dIdle = current.Idle - previous.Idle;
        long dIoWait = current.IoWait - previous.IoWait;
        long dSteal = current.Steal - previous.Steal;

        double userPct = (dUser * 100.0) / deltaTotal;
        double sysPct = (dSystem * 100.0) / deltaTotal;
        double idlePct = (dIdle * 100.0) / deltaTotal;
        double ioWaitPct = (dIoWait * 100.0) / deltaTotal;
        double stealPct = (dSteal * 100.0) / deltaTotal;
        double TotalBusyPercent = 100.0 - idlePct - ioWaitPct;

        return new CpuMetrics(
            UserPercent: Math.Round(Math.Clamp(userPct, 0, 100), 2),
            SystemPercent: Math.Round(Math.Clamp(sysPct, 0, 100), 2),
            IdlePercent: Math.Round(Math.Clamp(idlePct, 0, 100), 2),
            IoWaitPercent: Math.Round(Math.Clamp(ioWaitPct, 0, 100), 2),
            StealPercent: Math.Round(Math.Clamp(stealPct, 0, 100), 2),
            TotalBusyPercent: Math.Round(Math.Clamp(TotalBusyPercent, 0, 100), 2)
        );
    }

}