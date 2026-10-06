using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Ingressos.Messaging;

// Cliente do padrao RPC sobre AMQP (reply_to + correlation_id), usado pelo Alocador para
// consultar o Antifraude de forma sincrona antes de concluir a reserva. Deve rodar num canal
// dedicado, de uma conexao separada da conexao principal de consumo do chamador - ver comentario
// em Ingressos.Alocador.Worker.
public class RpcCliente(IChannel canal)
{
    public async Task<TResposta> ChamarAsync<TRequisicao, TResposta>(string routingKey, TRequisicao requisicao, TimeSpan timeout)
    {
        var filaResposta = await canal.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true);
        var correlationId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += (_, ea) =>
        {
            if (ea.BasicProperties.CorrelationId == correlationId)
            {
                tcs.TrySetResult(ea.Body.ToArray());
            }

            return Task.CompletedTask;
        };
        var consumerTag = await canal.BasicConsumeAsync(filaResposta.QueueName, autoAck: true, consumer: consumidor);

        try
        {
            var corpo = JsonSerializer.SerializeToUtf8Bytes(requisicao, JsonSerializacao.Opcoes);
            var propriedades = new BasicProperties { CorrelationId = correlationId, ReplyTo = filaResposta.QueueName };
            await canal.BasicPublishAsync(NomesTopologia.ExchangeEventos, routingKey, mandatory: false, basicProperties: propriedades, body: corpo);

            using var cts = new CancellationTokenSource(timeout);
            await using var registro = cts.Token.Register(() => tcs.TrySetCanceled());

            var respostaBytes = await tcs.Task;
            return JsonSerializer.Deserialize<TResposta>(respostaBytes, JsonSerializacao.Opcoes)
                ?? throw new InvalidOperationException("Resposta RPC vazia");
        }
        finally
        {
            await canal.BasicCancelAsync(consumerTag);
            await canal.QueueDeleteAsync(filaResposta.QueueName, ifUnused: false, ifEmpty: false);
        }
    }
}
