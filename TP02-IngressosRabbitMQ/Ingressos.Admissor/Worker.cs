using System.Collections.Concurrent;
using Ingressos.Contracts.Comandos;
using Ingressos.Contracts.Eventos;
using Ingressos.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Ingressos.Admissor;

// Controla quem entra na area de compra: consome fila.entrar (fila "sala-espera") e publica
// sala-espera.status uma vez por segundo com o total ja admitido - uma unica mensagem
// replicada a milhares de conexoes via Gateway WebSocket, em vez de uma mensagem por cliente
// (Topico 2.3.1 - Escalabilidade).
public class Worker(ILogger<Worker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, byte> _admitidos = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var conexao = await RabbitMqConnection.ConectarAsync(RabbitMqOptions.FromEnvironment(), "Ingressos.Admissor");
        var canal = await conexao.AbrirCanalAsync();
        await Topologia.DeclararAsync(canal);
        await canal.BasicQosAsync(0, 50, false, stoppingToken);

        var publisher = new MensagemPublisher(canal);

        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var comando = MensagemPublisher.Desserializar<EntrarFilaCommand>(ea.Body);
                _admitidos.TryAdd(comando.UsuarioId, 0);
                logger.LogInformation("Usuario {UsuarioId} admitido na sala de espera do evento {EventoId}", comando.UsuarioId, comando.EventoId);
                await canal.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao processar fila.entrar");
                await canal.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        await canal.BasicConsumeAsync("sala-espera", autoAck: false, consumer: consumidor, cancellationToken: stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var status = new SalaEsperaStatus(EventoId: Guid.Empty, PosicaoNaFila: 0, TotalAdmitido: _admitidos.Count);
            await publisher.PublicarAsync("sala-espera.status", status);
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
