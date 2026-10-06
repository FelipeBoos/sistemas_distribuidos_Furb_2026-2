using System.Text;
using Ingressos.Messaging;
using RabbitMQ.Client;

namespace Ingressos.Auditoria;

// Registra todo o historico de eventos (fila do tipo Stream, routing key # = tudo). O offset
// "first" faz a Auditoria reler o historico completo a cada reinicio, demonstrando o replay
// citado no Topico 2.3.2 (nao e o comportamento tipico de uma fila comum).
public class Worker(ILogger<Worker> logger) : ConsumidorBase(logger)
{
    protected override string Fila => NomesTopologia.FilaAuditoriaStream;

    protected override IDictionary<string, object?> ArgumentosConsumo => new Dictionary<string, object?>
    {
        ["x-stream-offset"] = "first"
    };

    protected override Task ProcessarAsync(ReadOnlyMemory<byte> corpo, IReadOnlyBasicProperties propriedades, CancellationToken ct)
    {
        logger.LogInformation("[AUDITORIA] {Tipo} ({MessageId}): {Payload}",
            propriedades.Type, propriedades.MessageId, Encoding.UTF8.GetString(corpo.Span));
        return Task.CompletedTask;
    }
}
