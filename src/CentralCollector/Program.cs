using Microsoft.Extensions.Options;
using CentralCollector.Models;
using CentralCollector.Services;
using System.Security.Cryptography.X509Certificates;
using System.Net.Mail;


var builder = WebApplication.CreateBuilder(args);

//Vincular sección Collector a las opciones 
builder.Services.Configure<CentralCollectorOptions>(builder.Configuration.GetSection("Collector"));

//Registrar el Cliente HTTP para Teams 
builder.Services.AddHttpClient<TeamsAlertService>();

//Registrar el servicio de alertas como singleton para que el diccionario de cooldown viva en memoria
builder.Services.AddSingleton<TeamsAlertService>();

var app = builder.Build();

//Endpoint de ingesta de alertas(Ingress)

app.MapPost("/api/alerts/ingress", async (
    ServerAlertEvent alert,
    HttpContext context,
    TeamsAlertService teamsService,
    IOptions<CentralCollectorOptions> options,
    CancellationToken ct) =>
{
    var expectedKey = options.Value.ExpectedApiKey;
    var providedKey = context.Request.Headers["X-Agent-Key"].FirstOrDefault();

    // Validación de seguridad de la clave del agente
    if (string.IsNullOrWhiteSpace(providedKey) || providedKey != expectedKey)
    {
        return Results.Unauthorized();
    }

    await teamsService.ProcessAlertAsync(alert, ct);
    return Results.Accepted();
});

// Endpoint de verificación rápida
app.MapGet("/api/health", () => Results.Ok(new { Status = "Collector Online", TimestampUtc = DateTime.UtcNow }));


app.Run();
