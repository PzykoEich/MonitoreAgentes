using System.Net.Sockets;

namespace PrintAgent.Services;

public class RawPrinterService
{
    private readonly ILogger<RawPrinterService> _logger;

    public RawPrinterService(ILogger<RawPrinterService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> SendJobAsync(string ipAddress, int port, byte[] data, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Enviando {Length} bytes a {Ip}:{Port}", data.Length, ipAddress, port);

        using var tcpClient = new TcpClient();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            await tcpClient.ConnectAsync(ipAddress, port, cts.Token);

            await using var networkStream = tcpClient.GetStream();
            await networkStream.WriteAsync(data, cancellationToken);
            await networkStream.FlushAsync(cancellationToken);

            _logger.LogInformation("Trabajo enviado exitosamente a {Ip}:{Port}", ipAddress, port);
            return true;
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "Error de socket al conectar con la impresora en {Ip}:{Port}", ipAddress, port);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Tiempo de espera agotado al conectar con {Ip}:{Port}", ipAddress, port);
            throw new TimeoutException($"Tiempo de espera agotado al conectar con {ipAddress}:{port}");
        }
    }
}