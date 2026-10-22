using MonitoringAgent.Collectors;
using MonitoringAgent.Services;
using MonitoringAgent.Models;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
// Vincular la sección "Monitoring" del JSON / Variables de Entorno a la clase tipada
builder.Services.Configure<MonitoringOptions>(builder.Configuration.GetSection("Monitoring"));
builder.Services.AddSingleton<MetricsStateStore>();
builder.Services.AddHostedService<MonitoringWorker>();
// Registro del despachador gestionando HttpClient sin fugas de sockets
builder.Services.AddHttpClient<AlertDispatcherService>();
var app = builder.Build();

app.MapGet("/api/memory", () => Results.Ok(MemoryCollector.Collect()));

app.MapGet("/api/cpu", async () =>
{
    var snapshot1 = CpuCollector.ReadSnapshot();
    await Task.Delay(500); // Ventana de 500 ms para calcular el delta de jiffies
    var snapshot2 = CpuCollector.ReadSnapshot();

    var cpuMetrics = CpuCollector.CalculateUsage(snapshot1, snapshot2);
    return Results.Ok(cpuMetrics);
});

app.MapGet("/api/load", () => Results.Ok(LoadAverageCollector.Collect()));

app.MapGet("/api/disk", () => Results.Ok(DiskCollector.Collect()));
app.MapGet("/api/metrics", (
    HttpContext context, 
    MetricsStateStore store, 
    IOptions<MonitoringOptions> options) =>
{
    var expectedToken = options.Value.Security.MonitoringToken;

    // Validación de seguridad: si no hay token configurado, nadie debe poder consultar
    if (string.IsNullOrWhiteSpace(expectedToken))
    {
        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Configuración insegura",
            detail: "El agente no tiene configurado un token de acceso válido.");
    }

    var providedToken = context.Request.Headers["X-Monitoring-Token"].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(providedToken) || providedToken != expectedToken)
    {
        return Results.Unauthorized();
    }

    var report = store.GetLatest();
    if (report == null)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(report);
});

// 1. Consulta rápida del último estado de parches conocido (en memoria)
app.MapGet("/api/security/updates", (
    HttpContext context,
    MetricsStateStore store,
    IOptions<MonitoringOptions> options) =>
{
    var expectedToken = options.Value.Security.MonitoringToken;
    var providedToken = context.Request.Headers["X-Monitoring-Token"].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(providedToken) || providedToken != expectedToken)
    {
        return Results.Unauthorized();
    }

    var snapshot = store.GetLatestSecuritySnapshot();
    if (snapshot == null)
    {
        return Results.Ok(new
        {
            Status = "PendingInitialScan",
            Message = "Aún no se ha completado el primer escaneo programado."
        });
    }

    return Results.Ok(snapshot);
});

// 2. Forzar un escaneo manual de parches en vivo (ejecuta dnf)
app.MapPost("/api/security/updates/check", async (
    HttpContext context,
    MetricsStateStore store,
    IOptions<MonitoringOptions> options,
    CancellationToken ct) =>
{
    var expectedToken = options.Value.Security.MonitoringToken;
    var providedToken = context.Request.Headers["X-Monitoring-Token"].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(providedToken) || providedToken != expectedToken)
    {
        return Results.Unauthorized();
    }

    // Ejecuta la auditoría en vivo
    var snapshot = await SecurityPatchCollector.CheckUpdatesAsync(ct);
    store.UpdateSecuritySnapshot(snapshot);

    return Results.Ok(snapshot);
});

// --- ENDPOINTS DE SERVICIOS (Desde memoria, cero overhead) ---

// Listar todos los servicios supervisados
app.MapGet("/api/services", (
    HttpContext context,
    MetricsStateStore store,
    IOptions<MonitoringOptions> options) =>
{
    var expectedToken = options.Value.Security.MonitoringToken;
    var providedToken = context.Request.Headers["X-Monitoring-Token"].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(providedToken) || providedToken != expectedToken)
    {
        return Results.Unauthorized();
    }

    var latest = store.GetLatest();
    if (latest == null)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new
    {
        TimestampUtc = latest.TimestampUtc,
        TotalMonitored = latest.Services.Count,
        Services = latest.Services
    });
});

// Consultar un servicio puntual por nombre
app.MapGet("/api/services/{name}", (
    string name,
    HttpContext context,
    MetricsStateStore store,
    IOptions<MonitoringOptions> options) =>
{
    var expectedToken = options.Value.Security.MonitoringToken;
    var providedToken = context.Request.Headers["X-Monitoring-Token"].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(providedToken) || providedToken != expectedToken)
    {
        return Results.Unauthorized();
    }

    var latest = store.GetLatest();
    if (latest == null)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    // Coincidencia exacta con Name de ServiceStatus
    var service = latest.Services.FirstOrDefault(s => 
        string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(s.Name, $"{name}.service", StringComparison.OrdinalIgnoreCase));

    if (service == null)
    {
        return Results.NotFound(new 
        { 
            Message = $"El servicio '{name}' no está en la lista de monitoreo activo.",
            Configured = options.Value.MonitoredServices 
        });
    }

    return Results.Ok(service);
});

// --- ENDPOINTS DE PROCESOS (Java, Scripts, etc.) ---

// Consultar todos los procesos configurados en appsettings
app.MapGet("/api/processes", (
    HttpContext context,
    IOptions<MonitoringOptions> options) =>
{
    var expectedToken = options.Value.Security.MonitoringToken;
    var providedToken = context.Request.Headers["X-Monitoring-Token"].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(providedToken) || providedToken != expectedToken)
    {
        return Results.Unauthorized();
    }

    var patterns = options.Value.MonitoredProcesses ?? new List<string>();
    var results = patterns.Select(ProcessTracker.CheckProcess).ToList();

    return Results.Ok(new
    {
        TimestampUtc = DateTime.UtcNow,
        TotalConfigured = patterns.Count,
        Processes = results
    });
});

// Buscar en vivo cualquier proceso en /proc bajo demanda
app.MapGet("/api/processes/find", (
    string q,
    HttpContext context,
    IOptions<MonitoringOptions> options) =>
{
    var expectedToken = options.Value.Security.MonitoringToken;
    var providedToken = context.Request.Headers["X-Monitoring-Token"].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(providedToken) || providedToken != expectedToken)
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(q))
    {
        return Results.BadRequest(new { Message = "Debe proporcionar el parámetro 'q'." });
    }

    var result = ProcessTracker.CheckProcess(q);
    return Results.Ok(result);
});
app.Run();