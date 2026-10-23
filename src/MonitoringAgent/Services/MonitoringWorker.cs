using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.Metadata;
using MonitoringAgent.Collectors;
using MonitoringAgent.Models;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace MonitoringAgent.Services;

public class MonitoringWorker : BackgroundService
{
    private readonly MetricsStateStore _stateStore;
    private readonly ILogger<MonitoringWorker> _logger;
    //private readonly IConfiguration _config;
    private readonly MonitoringOptions _options;
    private RawCpuSnapshot _previousCpuSnapshot;
    private readonly AlertDispatcherService _alertDispatcher;

    private DateTime _lastSecurityAuditUtc = DateTime.MinValue;
    public MonitoringWorker(
        MetricsStateStore stateStore,
        ILogger<MonitoringWorker> logger,
        IOptions<MonitoringOptions> options,
        AlertDispatcherService alertDispatcher
    )
    {
        _stateStore = stateStore;
        _logger = logger;
        //_config = config;
        _options = options.Value;
        _previousCpuSnapshot = CpuCollector.ReadSnapshot();
        _alertDispatcher = alertDispatcher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int interval = Math.Max(1,_options.IntervalSeconds);
        _logger.LogInformation($"Iniciando ciclo de monitore en segundo plano : {DateTimeOffset.Now}");

        //Leemos los servicios de systemd que vamos a vigilar desde appsettings.json
        // var servicesToWatch = _config.GetSection("ServicesToMonitor").Get<string[]>() ?? ["sshd", "cron"]; se trae del modelo ahora, más nice 

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                //Valcular detal contra las muestras de hace 5 segundos
                var currentCpuSnapshot = CpuCollector.ReadSnapshot();
                var cpuMetrics = CpuCollector.CalculateUsage(_previousCpuSnapshot, currentCpuSnapshot);
                _previousCpuSnapshot = currentCpuSnapshot;


                await CheckSecurityPatchesIfDueAsync(stoppingToken);

                var thermalSensors = ThermalCollector.Collect();

                // Memoria - carga - discos
                var memoryMetrics = MemoryCollector.Collect();
                var loadMetrics = LoadAverageCollector.Collect();
                var diskMetrics = DiskCollector.Collect();

                //Verificar servicios => cargados de appsettings.json
                var serviceStatuses = new List<ServiceStatus>();
                foreach(var sName in _options.MonitoredServices)
                {
                    bool active = IsServiceActive(sName);
                    serviceStatuses.Add(new ServiceStatus(
                        Name: sName,
                        IsActive: active,
                        Description: active ? "Active (Corriendo)" : "Inactivo / Fallo"
                    ));
                }
                /*
                * Pruebas
                */
                var processStatuses = new List<MonitoredProcessStatus>();
                if (_options.MonitoredProcesses != null && _options.MonitoredProcesses.Count > 0)
                {
                    foreach (var pattern in _options.MonitoredProcesses)
                    {
                        if (string.IsNullOrWhiteSpace(pattern)) continue;
                        processStatuses.Add(ProcessTracker.CheckProcess(pattern));
                    }
                }
                

                /*
                * FinPruebas
                */

                //generar el reporte
                var report = new SystemTelemetryReport(
                    Hostname: Environment.MachineName,
                    TimestampUtc: DateTime.UtcNow,
                    Memory: memoryMetrics,
                    Cpu: cpuMetrics,
                    Load: loadMetrics,
                    Disks: diskMetrics,
                    Services: serviceStatuses,
                    Temperatures: thermalSensors
                );

                //Actualizar la memoria compartida 
                _stateStore.Update(report);
                // Evaluación de alertas críticas locales
                await EvaluateThresholds(report,  stoppingToken, processStatuses);
                //Evaluación de alertas críticas locales
                await EvaluateThresholds(report,stoppingToken);
            }catch(Exception e)
            {
                _logger.LogError(e, "Error en la recoleción de métricas");
            }
            //Intervalo de descanso
            await Task.Delay(TimeSpan.FromSeconds(interval),stoppingToken);
        }
    }

    private async Task CheckSecurityPatchesIfDueAsync(CancellationToken ct)
    {
        var auditOpts = _options.SecurityAudit;

        if(!auditOpts.Enable) return;

        var now = DateTime.UtcNow;
        var interval = TimeSpan.FromHours(Math.Max(1,auditOpts.CheckIntervalHours));

        //Verificar el intervalo
        if(now - _lastSecurityAuditUtc >= interval)
        {
            _lastSecurityAuditUtc = now;
            _logger.LogInformation("Ejecutando auditoria programada de parches de seguridad");

            var snapshot = await SecurityPatchCollector.CheckUpdatesAsync(ct);
            //Guardamos en memoria el ultimo para endpoint
            _stateStore.UpdateSecuritySnapshot(snapshot);

            if (snapshot.HasPendingSecurityUpdates)
            {
                _logger.LogInformation($"[ALERTA SEURIDAD] {snapshot.Details}");

                await _alertDispatcher.DispatchAlertAsync(new ServerAlertEvent(
                    Hostname: Environment.MachineName,
                    TimestampUtc: now,
                    Severity: snapshot.CriticalUpdatesCount > 0 ? AlertSeverity.Critical : AlertSeverity.Warning,
                    MetricName: "Parche de seguiridad",
                    Message: snapshot.Details,
                    CurrentValue: snapshot.TotalSecurityUpdates,
                    ThresholdValue: 0
                ), ct);
            }else _logger.LogInformation("Auditoria de parches completada: sin parches de seguridad");
        }
    }

    private static bool IsServiceActive(string serviceName)
    {
        if (!OperatingSystem.IsLinux()) return true;

        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "systemctl",
                Arguments = $"is-active --quiet {serviceName}",
                RedirectStandardOutput = false,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            bool exited = proc?.WaitForExit(2000) ?? false;
            if (!exited)
            {
                proc?.Kill();
                return false;
            }
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
    private async Task EvaluateThresholds(SystemTelemetryReport report,  CancellationToken ct,List<MonitoredProcessStatus>? processes =null)
    {
        var thresholds = _options.Thresholds;

        //Temperaturas
        if (report.Temperatures != null)
{
    foreach (var sensor in report.Temperatures)
    {
        double limit = sensor.CriticalThresholdCelsius ?? 85.0;

        if (sensor.TemperatureCelsius >= limit)
        {
            _logger.LogCritical("[ALERTA CRÍTICA] Temperatura alta en {Sensor}: {Temp}°C (Límite: {Crit}°C)",
                sensor.SensorLabel, sensor.TemperatureCelsius, limit);

            await _alertDispatcher.DispatchAlertAsync(new ServerAlertEvent(
                Hostname: report.Hostname,
                TimestampUtc: DateTime.UtcNow,
                Severity: AlertSeverity.Critical,
                MetricName: $"Temp_{sensor.DeviceName}_{sensor.SensorLabel}",
                Message: $"Temperatura crítica en {sensor.SensorLabel}: {sensor.TemperatureCelsius}°C.",
                CurrentValue: sensor.TemperatureCelsius,
                ThresholdValue: limit
            ), ct);
        }
    }
}
        //Fin Temperaturas

        // Alerta de Servicios caídos
        foreach (var s in report.Services.Where(s => !s.IsActive))
        {
            _logger.LogCritical("[ALERTA CRÍTICA] Servicio detenido: {Service}", s.Name);
            await _alertDispatcher.DispatchAlertAsync(new ServerAlertEvent(
                Hostname: report.Hostname,
                TimestampUtc: DateTime.UtcNow,
                Severity: AlertSeverity.Critical,
                MetricName: $"Service_{s.Name}",
                Message: $"El servicio {s.Name} se encuentra caído o detenido.",
                CurrentValue: 0,
                ThresholdValue: 1
            ), ct);
        }
        
        // Alerta de Memoria
        if (report.Memory.UsedPercent >= thresholds.MemoryUsedPercent)
        {
            _logger.LogWarning("[ALERTA] Memoria RAM sobre el umbral: {Used}% (Límite: {Threshold}%)",
                report.Memory.UsedPercent, thresholds.MemoryUsedPercent);
            await _alertDispatcher.DispatchAlertAsync(new ServerAlertEvent(
                Hostname: report.Hostname,
                TimestampUtc: DateTime.UtcNow,
                Severity: AlertSeverity.Warning,
                MetricName: "Memoria",
                Message: $"Uso de memoria elevado {report.Memory.UsedPercent}%",
                CurrentValue: report.Memory.UsedPercent,
                ThresholdValue: thresholds.MemoryUsedPercent
            ),ct);
        }

        if (processes != null && processes.Count > 0)
        {
            // Usamos ProcessMemoryMb si existe en thresholds, o default de 1024 MB
            double maxProcMemoryMb = thresholds.ProcessMemoryMb > 0 ? thresholds.ProcessMemoryMb : 1024.0;

            foreach (var proc in processes)
            {
                // 1. Alerta: Proceso detenido / ausente
                if (!proc.IsRunning)
                {
                    _logger.LogCritical("[ALERTA CRÍTICA] Proceso detenido: {Pattern}", proc.TargetPatterns);
                    await _alertDispatcher.DispatchAlertAsync(new ServerAlertEvent(
                        Hostname: report.Hostname,
                        TimestampUtc: DateTime.UtcNow,
                        Severity: AlertSeverity.Critical,
                        MetricName: $"Process_{proc.TargetPatterns}",
                        Message: $"CRÍTICO: El proceso '{proc.TargetPatterns}' no se encuentra en ejecución.",
                        CurrentValue: 0,
                        ThresholdValue: 1
                    ), ct);
                }
                // 2. Alerta: Sobreconsumo de memoria RAM física (VmRSS)
                else if (proc.TotalMemoryMegabytes > maxProcMemoryMb)
                {
                    _logger.LogWarning("[ALERTA] Proceso {Pattern} sobrepasó memoria: {Used} MB (Límite: {Threshold} MB)",
                        proc.TargetPatterns, proc.TotalMemoryMegabytes, maxProcMemoryMb);

                    await _alertDispatcher.DispatchAlertAsync(new ServerAlertEvent(
                        Hostname: report.Hostname,
                        TimestampUtc: DateTime.UtcNow,
                        Severity: AlertSeverity.Warning,
                        MetricName: $"ProcessMem_{proc.TargetPatterns}",
                        Message: $"ADVERTENCIA: El proceso '{proc.TargetPatterns}' (PIDs: {string.Join(", ", proc.ProcessIds)}) consume {proc.TotalMemoryMegabytes} MB de RAM, superando el límite de {maxProcMemoryMb} MB.",
                        CurrentValue: proc.TotalMemoryMegabytes,
                        ThresholdValue: maxProcMemoryMb
                    ), ct);
                }
            }
        }

        // Alerta de CPU Ocupada
        if (report.Cpu.TotalBusyPercent >= thresholds.CpuBusyPercent)
        {
            _logger.LogWarning("[ALERTA] CPU ocupada: {Used}% (Límite: {Threshold}%)",
                report.Cpu.TotalBusyPercent, thresholds.CpuBusyPercent);
            await _alertDispatcher.DispatchAlertAsync(new ServerAlertEvent(
            Hostname: report.Hostname,
            TimestampUtc: DateTime.UtcNow,
            Severity: AlertSeverity.Warning,
            MetricName: "CpuTotal",
            Message: $"CPU sobrecargada al {report.Cpu.TotalBusyPercent}%",
            CurrentValue: report.Cpu.TotalBusyPercent,
            ThresholdValue: thresholds.CpuBusyPercent
        ), ct);
        }

        // Alerta de I/O Wait (Cuello de botella de disco)
        if (report.Cpu.IoWaitPercent >= thresholds.CpuIoWaitPercent)
        {
            _logger.LogCritical("[ALERTA CRÍTICA] I/O Wait de CPU peligroso: {IoWait}% (Límite: {Threshold}%)",
                report.Cpu.IoWaitPercent, thresholds.CpuIoWaitPercent);
            await _alertDispatcher.DispatchAlertAsync(new ServerAlertEvent(
            Hostname: report.Hostname,
            TimestampUtc: DateTime.UtcNow,
            Severity: AlertSeverity.Critical,
            MetricName: "IOWait",
            Message: $"Contención severa de almacenamiento detectada ({report.Cpu.IoWaitPercent}% I/O Wait)",
            CurrentValue: report.Cpu.IoWaitPercent,
            ThresholdValue: thresholds.CpuIoWaitPercent
        ), ct);
        }

        // Alerta de Saturación de Carga
        if (report.Load.SaturationPercent >= thresholds.LoadSaturationPercent)
        {
            _logger.LogWarning("[ALERTA] Saturación de cola del sistema: {Sat}% (Límite: {Threshold}%)",
                report.Load.SaturationPercent, thresholds.LoadSaturationPercent);
            await _alertDispatcher.DispatchAlertAsync(new ServerAlertEvent(
            Hostname: report.Hostname,
            TimestampUtc: DateTime.UtcNow,
            Severity: AlertSeverity.Critical,
            MetricName: "LoadAvg",
            Message: $"Saturación de cola del sustema {report.Load}",
            CurrentValue: 0,
            ThresholdValue: 1
        ), ct);
        }

        // Alerta de Espacio en Disco e Inodos
        foreach (var disk in report.Disks)
        {
            if (disk.SpaceUsedPercent >= thresholds.DiskSpaceUsedPercent)
            {
                _logger.LogWarning("[ALERTA] Espacio en disco bajo en {Mount}: {Used}% (Límite: {Threshold}%)",
                    disk.MountPoint, disk.SpaceUsedPercent, thresholds.DiskSpaceUsedPercent);
                await _alertDispatcher.DispatchAlertAsync(new ServerAlertEvent(
                Hostname: report.Hostname,
                TimestampUtc: DateTime.UtcNow,
                Severity: AlertSeverity.Warning,
                MetricName: $"DiskSpace: {disk.MountPoint}",
                Message: $"Espacio en disco comprometido en partición '{disk.MountPoint}\n' ({disk.FreeGigabytes} GB libres de {disk.TotalGigabytes} GB)",
                CurrentValue: disk.SpaceUsedPercent,
                ThresholdValue: thresholds.DiskSpaceUsedPercent
            ), ct);
            }

            if (disk.InodesUsedPercent >= thresholds.DiskInodesUsedPercent)
            {
                _logger.LogCritical("[ALERTA CRÍTICA] Inodos agotándose en {Mount}: {Used}% (Límite: {Threshold}%)",
                    disk.MountPoint, disk.InodesUsedPercent, thresholds.DiskInodesUsedPercent);
                await _alertDispatcher.DispatchAlertAsync(new ServerAlertEvent(
                Hostname: report.Hostname,
                TimestampUtc: DateTime.UtcNow,
                Severity: AlertSeverity.Critical,
                MetricName: $"Inodes: '{disk.MountPoint}'",
                Message: $"Tabla de inodos casi llena en partición '{disk.MountPoint}'\n ({disk.FreeInodes:N0} inodos libres de {disk.TotalInodes:N0})",
                CurrentValue: disk.InodesUsedPercent,
                ThresholdValue: thresholds.DiskInodesUsedPercent
            ), ct);
            }
        }

        // Alerta de Servicios caídos
        foreach (var s in report.Services.Where(s => !s.IsActive))
        {
            _logger.LogCritical("[ALERTA CRÍTICA] Servicio detenido: {Service}", s.Name);
            await _alertDispatcher.DispatchAlertAsync(new ServerAlertEvent(
            Hostname: report.Hostname,
            TimestampUtc: DateTime.UtcNow,
            Severity: AlertSeverity.Critical,
            MetricName: $"Service_{s.Name}",
            Message: $"El servicio {s.Name} se encuentra caído o detenido.",
            CurrentValue: 0,
            ThresholdValue: 1
        ), ct);
        }
    }
}