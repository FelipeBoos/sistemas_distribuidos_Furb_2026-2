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

    // So o tipo e o identificador vao para o log: o payload pode conter dados sensiveis (ex.: o QR
    // code do ingresso, que valida a entrada no evento) e nao deve ficar legivel em logs.
    protected override Task ProcessarAsync(ReadOnlyMemory<byte> corpo, IReadOnlyBasicProperties propriedades, CancellationToken ct)
    {
        logger.LogInformation("[AUDITORIA] {Tipo} ({MessageId}), {Bytes} byte(s)",
            propriedades.Type, propriedades.MessageId, corpo.Length);
        return Task.CompletedTask;
    }
}
