using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Ingressos.Messaging;

// Classe base para um consumidor dedicado a uma fila: conecta, declara a topologia, consome com
// prefetch limitado e so confirma (ack) apos processar com sucesso. Em erro, nack sem requeue -
// a mensagem segue para a dead-letter-exchange da fila (quando configurada) em vez de ficar
// re-entregue em loop.
public abstract class ConsumidorBase(ILogger logger) : BackgroundService
{
    protected abstract string Fila { get; }
    protected virtual ushort Prefetch => 10;

    // Filas do tipo stream exigem um offset inicial explicito (ex.: "first" para reler tudo
    // desde o inicio, usado pela Auditoria). Filas comuns nao precisam declarar isto.
    protected virtual IDictionary<string, object?>? ArgumentosConsumo => null;

    protected MensagemPublisher? Publisher { get; private set; }
    protected IChannel? Canal { get; private set; }

    private RabbitMqConnection? _conexao;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _conexao = await RabbitMqConnection.ConectarAsync(RabbitMqOptions.FromEnvironment(), GetType().Name);
        var _canal = await _conexao.AbrirCanalAsync();
        Canal = _canal;
        await Topologia.DeclararAsync(_canal);
        await _canal.BasicQosAsync(0, Prefetch, global: false, stoppingToken);

        Publisher = new MensagemPublisher(_canal);

        var consumidor = new AsyncEventingBasicConsumer(_canal);
        consumidor.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                await ProcessarAsync(ea.Body, ea.BasicProperties, stoppingToken);
                await _canal.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Desligamento do servico no meio do processamento: devolve a mensagem para a fila
                // em vez de descarta-la (ela sera reentregue no proximo start).
                await _canal.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao processar mensagem da fila {Fila}", Fila);
                await _canal.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        await _canal.BasicConsumeAsync(
            queue: Fila,
            autoAck: false,
            consumerTag: string.Empty,
            noLocal: false,
            exclusive: false,
            arguments: ArgumentosConsumo,
            consumer: consumidor,
            cancellationToken: stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // encerramento normal do servico
        }
    }

    protected abstract Task ProcessarAsync(ReadOnlyMemory<byte> corpo, IReadOnlyBasicProperties propriedades, CancellationToken ct);

    // Responde uma requisicao RPC (padrao reply_to + correlation_id) publicando direto na fila
    // exclusiva do solicitante, via exchange padrao (nome vazio).
    protected async Task ResponderRpcAsync<TResposta>(IReadOnlyBasicProperties requisicao, TResposta resposta)
    {
        if (Canal is null || requisicao.ReplyTo is null)
        {
            return;
        }

        var corpo = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(resposta, JsonSerializacao.Opcoes);
        var propriedades = new BasicProperties { CorrelationId = requisicao.CorrelationId };
        await Canal.BasicPublishAsync(exchange: string.Empty, routingKey: requisicao.ReplyTo, mandatory: false, basicProperties: propriedades, body: corpo);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Para o consumo (cancela stoppingToken) antes de fechar a conexao, para que handlers em
        // andamento ainda consigam dar ack/nack no canal.
        await base.StopAsync(cancellationToken);

        if (_conexao is not null)
        {
            await _conexao.DisposeAsync();
        }
    }
}
