using System.Globalization;
using System.Text.Json;
using Ingressos.Contracts.Comandos;
using Ingressos.Contracts.Eventos;
using Ingressos.Domain.Entidades;
using Ingressos.Domain.Regras;
using Ingressos.Messaging;
using Ingressos.Persistence;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace Ingressos.ServicoPagamento;

// Cobra o cliente no gateway de pagamento externo (simulado nesta demonstracao). Em recusa,
// segue pelas filas de retry com espera crescente (5s/30s/2min); apos esgotar as 3 tentativas
// de retry, vai para revisao manual (parking lot) - Topico 2.3.3.
public class Worker(ILogger<Worker> logger, IDbContextFactory<IngressosDbContext> dbFactory) : ConsumidorBase(logger)
{
    protected override string Fila => NomesTopologia.FilaPagamento;

    // InvariantCulture pelo mesmo motivo do Antifraude: "0.3" no .env deve ser lido como ponto
    // decimal independente do locale do sistema operacional.
    private static readonly double TaxaRecusa =
        double.TryParse(Environment.GetEnvironmentVariable("SERVICOPAGAMENTO_TAXA_RECUSA"), NumberStyles.Float, CultureInfo.InvariantCulture, out var taxa)
            ? taxa
            : 0.3;

    protected override async Task ProcessarAsync(ReadOnlyMemory<byte> corpo, IReadOnlyBasicProperties propriedades, CancellationToken ct)
    {
        var comando = MensagemPublisher.Desserializar<PagamentoSolicitadoCommand>(corpo);
        var tentativaAnterior = LerTentativa(propriedades);

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var pagamento = await db.Pagamentos.FirstOrDefaultAsync(p => p.IdempotencyKey == comando.IdempotencyKey, ct);
        if (pagamento is { Status: StatusPagamento.Aprovado })
        {
            // Mensagem duplicada/reentregue apos ja aprovado: idempotencia evita cobranca dupla.
            logger.LogInformation("Pagamento {IdempotencyKey} ja aprovado; ignorando reentrega", comando.IdempotencyKey);
            return;
        }

        if (pagamento is null)
        {
            // O valor e calculado pelo servidor a partir do preco do setor da reserva: o cliente
            // nao informa preco. Reserva inexistente nao tem o que cobrar.
            var tarifa = await (
                from r in db.Reservas
                join a in db.Assentos on r.AssentoId equals a.Id
                join s in db.Setores on a.SetorId equals s.Id
                where r.Id == comando.ReservaId
                select new { s.Preco, r.MeiaEntrada }).FirstOrDefaultAsync(ct);

            if (tarifa is null)
            {
                logger.LogWarning("Pagamento solicitado para reserva {ReservaId} inexistente; ignorado", comando.ReservaId);
                return;
            }

            pagamento = new Pagamento
            {
                Id = Guid.NewGuid(),
                ReservaId = comando.ReservaId,
                Valor = RegrasCompra.CalcularValor(tarifa.Preco, tarifa.MeiaEntrada),
                IdempotencyKey = comando.IdempotencyKey,
                Status = StatusPagamento.Solicitado
            };
            db.Pagamentos.Add(pagamento);
        }

        var aprovado = Random.Shared.NextDouble() >= TaxaRecusa;

        if (aprovado)
        {
            pagamento.Status = StatusPagamento.Aprovado;

            var reserva = await db.Reservas.FirstOrDefaultAsync(r => r.Id == comando.ReservaId, ct);
            if (reserva is not null)
            {
                reserva.Status = StatusReserva.Paga;
            }

            AdicionarOutbox(db, "pagamento.aprovado", new PagamentoAprovado(pagamento.Id, pagamento.ReservaId, pagamento.Valor));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Pagamento {PagamentoId} aprovado", pagamento.Id);
            return;
        }

        var tentativaAtual = tentativaAnterior + 1;
        pagamento.Status = StatusPagamento.Recusado;
        await db.SaveChangesAsync(ct);

        if (Publisher is null)
        {
            return;
        }

        if (tentativaAtual > 3)
        {
            logger.LogWarning("Pagamento {PagamentoId} recusado apos {Tentativas} tentativas; enviado para revisao manual", pagamento.Id, tentativaAtual);
            await Publisher.PublicarAsync("pagamento.parking-lot",
                new PagamentoRecusado(pagamento.Id, pagamento.ReservaId, "limite_de_tentativas_excedido", tentativaAtual));
            return;
        }

        var filaRetry = tentativaAtual switch
        {
            1 => "pagamento.retry.5s",
            2 => "pagamento.retry.30s",
            _ => "pagamento.retry.2m"
        };

        logger.LogInformation("Pagamento {PagamentoId} recusado (tentativa {Tentativa}); reagendado via {Fila}", pagamento.Id, tentativaAtual, filaRetry);

        // Evento informativo (Notificador/Auditoria) - NAO e o payload que volta para a fila
        // "pagamento" quando o TTL da fila de retry expira. Esse payload precisa continuar sendo
        // um PagamentoSolicitadoCommand (mesmo formato do comando original), senao a reentrega
        // falha ao desserializar (IdempotencyKey chega nulo, violando a constraint do banco).
        await Publisher.PublicarAsync("pagamento.recusado",
            new PagamentoRecusado(pagamento.Id, pagamento.ReservaId, "recusado_pelo_gateway", tentativaAtual));

        await Publisher.PublicarAsync(
            filaRetry,
            comando,
            exchange: NomesTopologia.ExchangeRetry,
            headers: new Dictionary<string, object?> { ["x-tentativa"] = tentativaAtual });
    }

    private static int LerTentativa(IReadOnlyBasicProperties propriedades)
    {
        if (propriedades.Headers is not null &&
            propriedades.Headers.TryGetValue("x-tentativa", out var valor) &&
            valor is not null)
        {
            // Mensagens que passaram por uma fila de retry chegam com o header em bytes (AMQP table).
            return valor switch
            {
                int i => i,
                long l => (int)l,
                byte[] bytes => int.Parse(System.Text.Encoding.UTF8.GetString(bytes)),
                _ => 0
            };
        }

        return 0;
    }

    private static void AdicionarOutbox<T>(IngressosDbContext db, string routingKey, T mensagem)
    {
        db.OutboxMessages.Add(new OutboxMessage
        {
            RoutingKey = routingKey,
            TipoMensagem = typeof(T).Name,
            PayloadJson = JsonSerializer.Serialize(mensagem, JsonSerializacao.Opcoes)
        });
    }
}
