using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using MonitoringAgent.Models;

namespace MonitoringAgent.Services;

public class AlertDispatcherService
{
    private readonly HttpClient _httpClient;
    private readonly MonitoringOptions _options;
    private readonly ILogger<AlertDispatcherService> _logger;
    private readonly ConcurrentQueue<ServerAlertEvent> _failedAlertQueue = new();
    private const int MaxQueueAlerts = 30;

    public AlertDispatcherService(
        HttpClient httpClient,
        IOptions<MonitoringOptions> options,
        ILogger<AlertDispatcherService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task DispatchAlertAsync(ServerAlertEvent alertEvent, CancellationToken cancellationToken = default)
    {
        var targetUrl = _options.CentralServer.IngressUrl;

        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return;
        }

        // 1. Si hay alertas pendientes acumuladas, intentamos vaciarlas primero
        await FlushQueuedAlertsAsync(targetUrl, cancellationToken);

        // 2. Intentamos enviar la alerta actual
        bool sent = await SendHttpPostAsync(targetUrl, alertEvent, cancellationToken);

        // 3. Si falló la conexión, la guardamos en la cola para reintentar después
        if (!sent && _failedAlertQueue.Count < MaxQueueAlerts)
        {
            _failedAlertQueue.Enqueue(alertEvent);
            _logger.LogWarning("Alerta guardada en cola local para reintento: {Metric}", alertEvent.MetricName);
        }
    }

    private async Task FlushQueuedAlertsAsync(string targetUrl, CancellationToken cancellationToken)
    {
        while (_failedAlertQueue.TryPeek(out var pendingAlert))
        {
            bool success = await SendHttpPostAsync(targetUrl, pendingAlert, cancellationToken);
            if (success)
            {
                _failedAlertQueue.TryDequeue(out _);
            }
            else
            {
                // Si sigue sin haber conexión, nos detenemos para no saturar
                break;
            }
        }
    }

    private async Task<bool> SendHttpPostAsync(string targetUrl, ServerAlertEvent alertEvent, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, targetUrl);
            request.Content = JsonContent.Create(alertEvent);

            if (!string.IsNullOrWhiteSpace(_options.CentralServer.ApiKey))
            {
                request.Headers.Add("X-Agent-Key", _options.CentralServer.ApiKey);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var response = await _httpClient.SendAsync(request, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("No se pudo conectar con el servidor central: {Message}", ex.Message);
            return false;
        }
    }
}