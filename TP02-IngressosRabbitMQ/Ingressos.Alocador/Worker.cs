namespace Ingressos.Alocador;

// TODO (Topico 3): consumir compra.solicitada.<setor> via Single Active Consumer (1 por setor),
// reservar o assento sem overbooking, validar limite de ingressos por CPF e cota de meia-entrada
// (Lei 12.933/2013), e publicar reserva.criada, assento.indisponivel ou compra.rejeitada.*.
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
