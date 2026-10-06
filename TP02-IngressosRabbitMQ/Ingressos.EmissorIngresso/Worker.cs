using System.Text.Json;
using Ingressos.Contracts.Eventos;
using Ingressos.Domain.Entidades;
using Ingressos.Messaging;
using Ingressos.Persistence;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace Ingressos.EmissorIngresso;

// Gera o ingresso com QR Code ao consumir pagamento.aprovado e publica ingresso.emitido via
// outbox, na mesma transacao que grava o Ingresso (Topico 2.3.2 - padrao Outbox).
public class Worker(ILogger<Worker> logger, IDbContextFactory<IngressosDbContext> dbFactory) : ConsumidorBase(logger)
{
    protected override string Fila => NomesTopologia.FilaEmissao;

    protected override async Task ProcessarAsync(ReadOnlyMemory<byte> corpo, IReadOnlyBasicProperties propriedades, CancellationToken ct)
    {
        var evento = MensagemPublisher.Desserializar<PagamentoAprovado>(corpo);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var jaEmitido = await db.Ingressos.AnyAsync(i => i.PagamentoId == evento.PagamentoId, ct);
        if (jaEmitido)
        {
            // Idempotencia: pagamento.aprovado reentregue nao gera um segundo ingresso.
            return;
        }

        var ingresso = new Ingresso
        {
            Id = Guid.NewGuid(),
            PagamentoId = evento.PagamentoId,
            QrCode = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            EmitidoEm = DateTime.UtcNow
        };
        db.Ingressos.Add(ingresso);

        db.OutboxMessages.Add(new OutboxMessage
        {
            RoutingKey = "ingresso.emitido",
            TipoMensagem = nameof(IngressoEmitido),
            PayloadJson = JsonSerializer.Serialize(new IngressoEmitido(ingresso.Id, ingresso.PagamentoId, ingresso.QrCode), JsonSerializacao.Opcoes)
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Ingresso {IngressoId} emitido para pagamento {PagamentoId}", ingresso.Id, evento.PagamentoId);
    }
}
