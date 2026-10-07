using RabbitMQ.Client;

namespace Ingressos.Messaging;

// Nomes de exchanges e filas, e os setores de demonstracao usados pelo Alocador (1 fila por
// setor, como descrito na secao 2.1 do documento).
public static class NomesTopologia
{
    public const string ExchangeEventos = "ingressos.eventos";
    public const string ExchangeDlx = "ingressos.dlx";
    public const string ExchangeRetry = "ingressos.retry";

    // Dead-letter das filas de consumo (fanout): mensagem rejeitada sem requeue cai em
    // dlq.ingressos em vez de ser descartada. Separada de ingressos.dlx, que alimenta o Liberador.
    public const string ExchangeDlq = "ingressos.dlq";
    public const string FilaDlq = "dlq.ingressos";

    public static readonly string[] Setores = ["pista", "cadeira", "camarote"];

    public const string FilaSalaEspera = "sala-espera";
    public const string FilaSalaEsperaStatus = "sala-espera-status";
    public const string FilaReservasExpiradas = "reservas-expiradas";
    public const string FilaPagamento = "pagamento";
    public const string FilaAntifraude = "antifraude";
    public const string FilaEmissao = "emissao";
    public const string FilaNotificacao = "notificacao";
    public const string FilaAuditoriaStream = "auditoria-stream";
    public const string FilaPagamentoParkingLot = "pagamento-parking-lot";

    public static string FilaAlocacao(string setor) => $"alocacao.{setor}";
    public static string RoutingKeyCompraSolicitada(string setor) => $"compra.solicitada.{setor}";
}

// Declara exchanges, filas e bindings de forma idempotente (seguro chamar no startup de cada
// servico quantas vezes for). Reflete os parametros do Quadro 2 do documento de arquitetura.
public static class Topologia
{
    public static async Task DeclararAsync(IChannel canal)
    {
        await canal.ExchangeDeclareAsync(NomesTopologia.ExchangeEventos, ExchangeType.Topic, durable: true);
        await canal.ExchangeDeclareAsync(NomesTopologia.ExchangeDlx, ExchangeType.Fanout, durable: true);
        await canal.ExchangeDeclareAsync(NomesTopologia.ExchangeRetry, ExchangeType.Topic, durable: true);
        await canal.ExchangeDeclareAsync(NomesTopologia.ExchangeDlq, ExchangeType.Fanout, durable: true);
        await canal.QueueDeclareAsync(NomesTopologia.FilaDlq, durable: true, exclusive: false, autoDelete: false);
        await canal.QueueBindAsync(NomesTopologia.FilaDlq, NomesTopologia.ExchangeDlq, routingKey: string.Empty);

        // Argumentos das filas de consumo: mensagens rejeitadas (nack sem requeue) vao para a DLQ.
        var argsDlq = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = NomesTopologia.ExchangeDlq };

        // fila.entrar -> sala-espera (backpressure: rejeita publicacao quando a fila esta cheia)
        await canal.QueueDeclareAsync("sala-espera", durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-max-priority"] = 10,
                ["x-max-length"] = 100_000,
                ["x-overflow"] = "reject-publish"
            });
        await canal.QueueBindAsync("sala-espera", NomesTopologia.ExchangeEventos, "fila.entrar");

        // sala-espera.status -> sala-espera-status, consumida pelo Gateway WebSocket
        await canal.QueueDeclareAsync("sala-espera-status", durable: true, exclusive: false, autoDelete: false);
        await canal.QueueBindAsync("sala-espera-status", NomesTopologia.ExchangeEventos, "sala-espera.status");

        // compra.solicitada.<setor> -> alocacao.<setor>, 1 fila quorum por setor (Single Active Consumer)
        var sacHabilitado = Environment.GetEnvironmentVariable("ALOCADOR_SINGLE_ACTIVE_CONSUMER") != "false";
        foreach (var setor in NomesTopologia.Setores)
        {
            var args = new Dictionary<string, object?>(argsDlq) { ["x-queue-type"] = "quorum" };
            if (sacHabilitado)
            {
                args["x-single-active-consumer"] = true;
            }

            var fila = NomesTopologia.FilaAlocacao(setor);
            await canal.QueueDeclareAsync(fila, durable: true, exclusive: false, autoDelete: false, arguments: args);
            await canal.QueueBindAsync(fila, NomesTopologia.ExchangeEventos, NomesTopologia.RoutingKeyCompraSolicitada(setor));
        }

        // reserva.criada -> reservas-pendentes, com TTL de 10 min e dead-letter para ingressos.dlx
        await canal.QueueDeclareAsync("reservas-pendentes", durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-message-ttl"] = 600_000,
                ["x-dead-letter-exchange"] = NomesTopologia.ExchangeDlx
            });
        await canal.QueueBindAsync("reservas-pendentes", NomesTopologia.ExchangeEventos, "reserva.criada");

        // ingressos.dlx (fanout) -> reservas-expiradas, consumida pelo Liberador de Reserva
        await canal.QueueDeclareAsync("reservas-expiradas", durable: true, exclusive: false, autoDelete: false);
        await canal.QueueBindAsync("reservas-expiradas", NomesTopologia.ExchangeDlx, routingKey: string.Empty);

        // pagamento.solicitado -> pagamento
        await canal.QueueDeclareAsync("pagamento", durable: true, exclusive: false, autoDelete: false, arguments: argsDlq);
        await canal.QueueBindAsync("pagamento", NomesTopologia.ExchangeEventos, "pagamento.solicitado");

        // filas de retry com TTL crescente; ao expirar, voltam para pagamento.solicitado
        await DeclararFilaRetryAsync(canal, "pagamento.retry.5s", ttlMs: 5_000);
        await DeclararFilaRetryAsync(canal, "pagamento.retry.30s", ttlMs: 30_000);
        await DeclararFilaRetryAsync(canal, "pagamento.retry.2m", ttlMs: 120_000);

        // apos 3 tentativas, revisao manual (parking lot) - publicada diretamente pelo Servico de Pagamento
        await canal.QueueDeclareAsync("pagamento-parking-lot", durable: true, exclusive: false, autoDelete: false);
        await canal.QueueBindAsync("pagamento-parking-lot", NomesTopologia.ExchangeEventos, "pagamento.parking-lot");

        // antifraude.solicitado -> antifraude (RPC: reply_to + correlation_id)
        await canal.QueueDeclareAsync("antifraude", durable: true, exclusive: false, autoDelete: false, arguments: argsDlq);
        await canal.QueueBindAsync("antifraude", NomesTopologia.ExchangeEventos, "antifraude.solicitado");

        // pagamento.aprovado -> emissao
        await canal.QueueDeclareAsync("emissao", durable: true, exclusive: false, autoDelete: false, arguments: argsDlq);
        await canal.QueueBindAsync("emissao", NomesTopologia.ExchangeEventos, "pagamento.aprovado");

        // reserva.*, pagamento.*, ingresso.emitido -> notificacao (bindings multiplos)
        await canal.QueueDeclareAsync("notificacao", durable: true, exclusive: false, autoDelete: false, arguments: argsDlq);
        await canal.QueueBindAsync("notificacao", NomesTopologia.ExchangeEventos, "reserva.*");
        await canal.QueueBindAsync("notificacao", NomesTopologia.ExchangeEventos, "pagamento.*");
        await canal.QueueBindAsync("notificacao", NomesTopologia.ExchangeEventos, "ingresso.emitido");
        await canal.QueueBindAsync("notificacao", NomesTopologia.ExchangeEventos, "compra.rejeitada.*");
        await canal.QueueBindAsync("notificacao", NomesTopologia.ExchangeEventos, "assento.*");

        // Historico de eventos de negocio -> auditoria-stream, permite replay. Nao inclui
        // sala-espera.status (heartbeat de posicao, nao e evento de negocio). A retencao e limitada
        // por idade e tamanho: a stream nao apaga mensagens ao ser consumida.
        await canal.QueueDeclareAsync("auditoria-stream", durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "stream",
                ["x-max-age"] = "1h",
                ["x-max-length-bytes"] = 50_000_000L
            });
        foreach (var padrao in new[] { "fila.#", "antifraude.#", "compra.#", "reserva.#", "pagamento.#", "ingresso.#", "assento.#" })
        {
            await canal.QueueBindAsync("auditoria-stream", NomesTopologia.ExchangeEventos, padrao);
        }
    }

    private static async Task DeclararFilaRetryAsync(IChannel canal, string nomeFila, int ttlMs)
    {
        await canal.QueueDeclareAsync(nomeFila, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-message-ttl"] = ttlMs,
                ["x-dead-letter-exchange"] = NomesTopologia.ExchangeEventos,
                ["x-dead-letter-routing-key"] = "pagamento.solicitado"
            });
        await canal.QueueBindAsync(nomeFila, NomesTopologia.ExchangeRetry, nomeFila);
    }
}
