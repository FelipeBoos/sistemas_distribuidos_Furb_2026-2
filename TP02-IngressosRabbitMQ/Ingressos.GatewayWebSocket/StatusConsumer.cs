using System.Text;
using Ingressos.Messaging;
using RabbitMQ.Client;

namespace Ingressos.GatewayWebSocket;

// Consome sala-espera.status (fila sala-espera-status) e repassa para o WebSocketBroadcaster.
public class StatusConsumer(ILogger<StatusConsumer> logger, WebSocketBroadcaster broadcaster) : ConsumidorBase(logger)
{
    protected override string Fila => NomesTopologia.FilaSalaEsperaStatus;

    protected override Task ProcessarAsync(ReadOnlyMemory<byte> corpo, IReadOnlyBasicProperties propriedades, CancellationToken ct) =>
        broadcaster.BroadcastAsync(Encoding.UTF8.GetString(corpo.Span), ct);
}
