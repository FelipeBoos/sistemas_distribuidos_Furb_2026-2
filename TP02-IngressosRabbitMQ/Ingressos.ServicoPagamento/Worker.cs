namespace Ingressos.ServicoPagamento;

// TODO (Topico 3): consumir pagamento.solicitado e as filas de retry (5s, 30s, 2min), cobrar no
// gateway externo e publicar pagamento.aprovado ou pagamento.recusado. Aplicar circuit breaker
// para falhas seguidas no gateway.
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }
            await Task.Delay(1000, stoppingToken);
        }
    }
}
