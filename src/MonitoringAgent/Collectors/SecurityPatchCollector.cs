using System.Diagnostics;
using MonitoringAgent.Models;

namespace MonitoringAgent.Collectors;

public static class SecurityPatchCollector
{
    public static async Task<SecurityUpdatesSnapshot> CheckUpdatesAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsLinux())
        {
            return new SecurityUpdatesSnapshot(false, 0, 0, 0, DateTime.UtcNow, "No soportado fuera de Linux");
        }

        // 1. Detectar si el sistema operativo usa DNF (Fedora/RHEL) o APT (Debian/Ubuntu)
        bool hasDnf = File.Exists("/usr/bin/dnf") || File.Exists("/bin/dnf");
        bool hasApt = File.Exists("/usr/bin/apt-get") || File.Exists("/bin/apt-get");

        if (hasDnf)
        {
            return await CheckDnfSecurityUpdatesAsync(ct);
        }

        if (hasApt)
        {
            return await CheckAptSecurityUpdatesAsync(ct);
        }

        return new SecurityUpdatesSnapshot(
            HasPendingSecurityUpdates: false,
            CriticalUpdatesCount: 0,
            ImportantUpdatesCount: 0,
            TotalSecurityUpdates: 0,
            LastCheckedUtc: DateTime.UtcNow,
            Details: "No se encontró un gestor de paquetes soportado (dnf o apt)."
        );
    }

    private static async Task<SecurityUpdatesSnapshot> CheckDnfSecurityUpdatesAsync(CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dnf",
                Arguments = "updateinfo summary --security",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

            string output = await proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            await proc.WaitForExitAsync(timeoutCts.Token);

            int critical = 0;
            int important = 0;

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                if (line.Contains("Critical", StringComparison.OrdinalIgnoreCase))
                    critical = ExtractNumber(line);
                else if (line.Contains("Important", StringComparison.OrdinalIgnoreCase))
                    important = ExtractNumber(line);
            }

            int total = critical + important;
            return new SecurityUpdatesSnapshot(
                HasPendingSecurityUpdates: total > 0,
                CriticalUpdatesCount: critical,
                ImportantUpdatesCount: important,
                TotalSecurityUpdates: total,
                LastCheckedUtc: DateTime.UtcNow,
                Details: total > 0
                    ? $"Parches DNF pendientes: {critical} Críticos, {important} Importantes."
                    : "Sistema Fedora/RHEL al día en parches de seguridad."
            );
        }
        catch (Exception ex)
        {
            return new SecurityUpdatesSnapshot(false, 0, 0, 0, DateTime.UtcNow, $"Fallo en DNF: {ex.Message}");
        }
    }

    private static async Task<SecurityUpdatesSnapshot> CheckAptSecurityUpdatesAsync(CancellationToken ct)
    {
        try
        {
            // Ejecutamos simulación (-s) para no alterar paquetes ni requerir root
            var psi = new ProcessStartInfo
            {
                FileName = "apt-get",
                Arguments = "-s upgrade",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

            string output = await proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            await proc.WaitForExitAsync(timeoutCts.Token);

            int securityCount = 0;
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("Inst ", StringComparison.Ordinal) &&
                    (line.Contains("security", StringComparison.OrdinalIgnoreCase) || line.Contains("-updates", StringComparison.OrdinalIgnoreCase)))
                {
                    securityCount++;
                }
            }

            return new SecurityUpdatesSnapshot(
                HasPendingSecurityUpdates: securityCount > 0,
                CriticalUpdatesCount: 0,
                ImportantUpdatesCount: securityCount,
                TotalSecurityUpdates: securityCount,
                LastCheckedUtc: DateTime.UtcNow,
                Details: securityCount > 0
                    ? $"Parches pendientes (APT): {securityCount} paquete(s) detectado(s)."
                    : "Entorno Debian/Ubuntu al día en actualizaciones."
            );
        }
        catch (Exception ex)
        {
            return new SecurityUpdatesSnapshot(false, 0, 0, 0, DateTime.UtcNow, $"Fallo en APT: {ex.Message}");
        }
    }

    private static int ExtractNumber(string line)
    {
        var span = line.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            if (char.IsDigit(span[i]))
            {
                int end = i;
                while (end < span.Length && char.IsDigit(span[end])) end++;
                if (int.TryParse(span.Slice(i, end - i), out int val))
                {
                    return val;
                }
            }
        }
        return 0;
    }
}