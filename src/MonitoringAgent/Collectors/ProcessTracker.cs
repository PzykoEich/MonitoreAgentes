using System.IO;
using MonitoringAgent.Models;

namespace MonitoringAgent.Collectors;

public static class ProcessTracker
{
    public static MonitoredProcessStatus CheckProcess(string pattern)
    {
        if(string.IsNullOrWhiteSpace(pattern)) return new MonitoredProcessStatus(pattern, false, 0, new List<int>(), 0);

        var pids = new List<int>();
        double totalMemoryMb = 0;

        try
        {
            var procDir = new DirectoryInfo("/proc");

            //Recorrer únicamente directorios númericos
            foreach(var dir in procDir.EnumerateDirectories())
            {
                if(!int.TryParse(dir.Name, out int pid)) continue;

                string cmdlinePath = Path.Combine(dir.FullName, "cmdline");
                if(!File.Exists(cmdlinePath)) continue;

                try
                {
                    //Separar los argumentos por bytes nulos \0
                    string cmdline = File.ReadAllText(cmdlinePath).Replace('\0',' ');

                    //Evaluar Coincidencias (dbserver, serversas....)
                    if(cmdline.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        pids.Add(pid);
                        totalMemoryMb += ReadProcessRssMb(dir.FullName);
                    }
                }
                catch
                {
                    continue;
                }
            }
        }
        catch
        {
            // Protección contra excepciones al listar /proc Aquí no paso nada °-°
        }
        return new MonitoredProcessStatus(
            TargetPatterns: pattern,
            IsRunning: pids.Count > 0,
            InstanceCount: pids.Count,
            ProcessIds: pids,
            TotalMemoryMegabytes: Math.Round(totalMemoryMb,2)
        );
    }

    //Leer la memoria RAM real Usada VMRSS => /proc/pid/status

    private static double ReadProcessRssMb(string procDirPath)
    {
        string statusPath = Path.Combine(procDirPath, "status");
        if(!File.Exists(statusPath)) return 0;

        try
        {
            foreach(var line in File.ReadLines(statusPath))
            {
                if(line.StartsWith("VmRSSS", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);


                    if(parts.Length >=2 && long.TryParse(parts[1], out long kb)) return kb / 1024.0;
                }
            }
        }
        catch
        {
            
        }
        return 0;
    }
}