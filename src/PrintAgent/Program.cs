using System.Text;
using PrintAgent.Models;
using PrintAgent.Services;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 1. REGISTRO DE SERVICIOS (El Builder) 
// ==========================================

// Configuración de CORS: Permite que el frontend web (ej. un portal de CENEFAS y GAFETTES)
// que corre en un navegador pueda hacer peticiones a http://localhost:5000 sin ser bloqueado.
//Producción
// builder.Services.AddCors(options =>
// {
//     options.AddPolicy("StrictWebAppOrigin", policy =>
//     {
//         policy.WithOrigins("https://portalCenefas.fragua.frg", "https://pos.fragua.frg")
//               .WithMethods("GET", "POST")
//               .WithHeaders("Content-Type", "X-Print-Token");
//     });
// });

//Pruebas
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWebApp", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Registro del servicio RawPrinterService para inyección de dependencias
builder.Services.AddSingleton<PrinterDiscoveryService>();
builder.Services.AddSingleton<RawPrinterService>();

var app = builder.Build();

// ==========================================
// 2. PIPELINE DE MIDDLEWARES (La Tubería)
// ==========================================

// Activamos el middleware de CORS para todas las peticiones entrantes
app.UseCors("AllowWebApp");

// ==========================================
// 3. DEFINICIÓN DE ENDPOINTS (Las Rutas)
// ==========================================
/// <summary>
/// Endpoint de salud y diagnóstico:
/// Permite al frontend web saber si el agente está corriendo,
/// qué IP local tiene la máquina Fedora y qué impresora resolvió automáticamente.
/// </summary>
app.MapGet("/api/status", (PrinterDiscoveryService discovery) =>
{
    var localIP = discovery.GetLocalSubnetIP()?.ToString() ?? "No se pudo determinar la IP local";
    var resolvedPrinterIP = discovery.ResolvePrinterIP()?.ToString() ?? "No se pudo resolver la IP de la impresora";

    return Results.Ok(new AgentStatusResponse(
        Status: "Agente de impresión corriendo",
        Version: "1.0.0",
        MachineName: Environment.MachineName,
        LocalIP: localIP,
        AssignedPrinterIP: resolvedPrinterIP,
        Timestamp: DateTime.UtcNow
    ));
});
/// <summary>
/// Endpoint de impresión:
/// Recibe el payload del documento, determina la IP destino y lo envía al socket 9100 Desde la aplicación web.
/// </summary>
/// 
app.MapPost("/api/print", async(
    PrintJobRequest request,
    PrinterDiscoveryService discovery,
    RawPrinterService printerService,
    CancellationToken cancellationToken) =>
{
    //Validación el contenido no puede estar vacio
    if (string.IsNullOrWhiteSpace(request.Payload))
    {
        return Results.BadRequest( new PrintJobResponse(
            Success: false,
            JobId: string.Empty,
            Message: "El contenido del trabajo de impresión no puede estar vacío."
        ));
    }
    // 1. Determinar la IP de la impresora:
    // Si la petición especifica una IP, se respeta; si no, se deduce por autodescubrimiento.
    var targetIp = request.TargetIp ?? discovery.ResolvePrinterIP();
    if (string.IsNullOrWhiteSpace(targetIp))
    {
        return Results.BadRequest(new PrintJobResponse(
            Success: false,
            JobId: string.Empty,
            Message: "No se pudo determinar la IP de la impresora."
        ));
    }
    //Convertir el payload de string a bytes
    byte[] dataBytes;
    try
    {
        if (request.IsBase64)
        {
            //Para Binarios(PDF's o Secuencias complejas) se espera que el payload venga en Base64
            dataBytes = Convert.FromBase64String(request.Payload);
        }else
        {
            //Para texto plano, se convierte a bytes usando UTF-8
            dataBytes = Encoding.UTF8.GetBytes(request.Payload);
        }
    }catch( FormatException)
    {
        return Results.BadRequest(new PrintJobResponse(
            Success: false,
            JobId: string.Empty,
            Message: "El contenido del trabajo de impresión no es un Base64 válido."
        ));
    }
    //Generar un ID único para el trabajo de impresión
    var jobId = Guid.NewGuid().ToString("N");

    //Enviar los bytes mediante el socket TCP raw al puerto 9100 de la impresora
    try
    {
        await printerService.SendJobAsync(targetIp, 9100, dataBytes, cancellationToken);
        return Results.Ok(new PrintJobResponse(
            Success: true,
            JobId: jobId,
            Message: $"Trabajo de impresión enviado correctamente a {targetIp}:9100"
        ));
    }catch (Exception ex)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Fallo de comunicación con la impresora",
            detail: ex.Message);
    }
}
);

// Iniciar Kestrel y escuchar peticiones
app.Run();