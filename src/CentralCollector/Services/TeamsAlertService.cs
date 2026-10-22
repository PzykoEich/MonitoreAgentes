using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using CentralCollector.Models;
using System.Net.Mail;
using System.Net.Mime;

namespace CentralCollector.Services;

public class TeamsAlertService
{
    private readonly HttpClient _httpClient;
    private readonly CentralCollectorOptions _options;
    private readonly ILogger<TeamsAlertService> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _cooldownTracker = new ();


    public TeamsAlertService(HttpClient httpClient, IOptions<CentralCollectorOptions> options, ILogger<TeamsAlertService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessAlertAsync(ServerAlertEvent alert, CancellationToken ct = default)
    {
        var alertKey = $"{alert.Hostname}:{alert.MetricName}";
        var now = DateTime.UtcNow;

        //Verificar el periodo de enfriamiento 
        if(_cooldownTracker.TryGetValue(alertKey, out var lastSent))
        {
            var elapsed = now - lastSent;
            if(elapsed < TimeSpan.FromMinutes(_options.CooldownMinutes))
            {
                _logger.LogInformation($"Alera {alertKey} suprimida por cooldown. Restan {(_options.CooldownMinutes - elapsed.TotalMinutes):F1}");
                return;
            }
        }
        //Actualizar marca de tiempo
        _cooldownTracker[alertKey] = now;

        // Simular petición
        if (string.IsNullOrWhiteSpace(_options.TeamsWebhookUrl))
        {
            _logger.LogInformation($"[Calis TEAMS] {alert.Hostname} | {alert.Severity}: {alert.MetricName} => {alert.Message}");
            return;
        }

        await SendTeamsAdaptiveCardAsync(alert, ct);
    }

    private async Task SendTeamsAdaptiveCardAsync(ServerAlertEvent alert, CancellationToken ct)
    {
        //Adaptive Cads: "Alerta" = Rojo "Warning" => Amarillo / Naranja
        var accentColor = alert.Severity == AlertSeverity.Critical ? "Attention" : "Warning";

        var payload = new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new
                    {
                        type = "AdaptiveCard",
                        version = "1.4",
                        body = new object[]
                        {
                            new
                            {
                                type = "TextBlock",
                                size = "Medium",
                                weight = "Bolder",
                                color = accentColor,
                                text = $"[{alert.Severity.ToString().ToUpper()}] Incidente de Infraestructura"
                            },
                            new
                            {
                                type = "FactSet",
                                facts = new[]
                                {
                                    new { title = "Servidor: ", value = alert.Hostname},
                                    new { title = "Recurso /Métrica: ", value = alert.MetricName},
                                    new { title = "Detalle: ", value = alert.Message},
                                    new { title = "Valor detectado: ", value = $"{alert.CurrentValue:F2}" },
                                    new { title = "Umbral configurado: ", value = $"{alert.ThresholdValue:F2}"},
                                    new { title = "Fecha UTC: ", value = alert.TimestampUtc.ToString("dd-MM-yyyy HH:mm:ss")}
                                }
                            }
                        }
                    }
                }
            }
        };

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var response = await _httpClient.PostAsJsonAsync(_options.TeamsWebhookUrl, payload, cts.Token);

            if(response.IsSuccessStatusCode) _logger.LogInformation($"Adaptive Card enviada a Teams para {alert.Hostname}:{alert.MetricName}");
            else _logger.LogError($"Teams rechazo la tarjeta con código HTTP {response.StatusCode}");
        }catch(Exception ex)
        {
            _logger.LogError(ex, "Fallo al enviar la petición al webhook de teams");
        }
    }
}