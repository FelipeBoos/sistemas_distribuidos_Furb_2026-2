using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace Ingressos.Messaging;

// Publica um contrato (record de Ingressos.Contracts) serializado em JSON na exchange principal,
// com publisher confirms (aguarda confirmacao de persistencia do broker antes de retornar).
public class MensagemPublisher(IChannel canal)
{
    public async Task PublicarAsync<T>(string routingKey, T mensagem, string? exchange = null, Guid? mensagemId = null,
        IDictionary<string, object?>? headers = null)
    {
        var corpo = JsonSerializer.SerializeToUtf8Bytes(mensagem, JsonSerializacao.Opcoes);

        var propriedades = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = (mensagemId ?? Guid.NewGuid()).ToString(),
            Type = typeof(T).Name,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            Headers = headers
        };

        await canal.BasicPublishAsync(
            exchange: exchange ?? NomesTopologia.ExchangeEventos,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: propriedades,
            body: corpo);
    }

    // Publica um payload ja serializado (usado pelo Outbox Relay, que le o JSON pronto da tabela
    // outbox em vez de receber o objeto tipado original).
    public async Task PublicarBrutoAsync(string routingKey, string payloadJson, string tipoMensagem, string? exchange = null, Guid? mensagemId = null)
    {
        var corpo = Encoding.UTF8.GetBytes(payloadJson);

        var propriedades = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = (mensagemId ?? Guid.NewGuid()).ToString(),
            Type = tipoMensagem,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        };

        await canal.BasicPublishAsync(
            exchange: exchange ?? NomesTopologia.ExchangeEventos,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: propriedades,
            body: corpo);
    }

    public static T Desserializar<T>(ReadOnlyMemory<byte> corpo) =>
        JsonSerializer.Deserialize<T>(corpo.Span, JsonSerializacao.Opcoes)
        ?? throw new InvalidOperationException($"Mensagem vazia ou invalida para {typeof(T).Name}");
}
