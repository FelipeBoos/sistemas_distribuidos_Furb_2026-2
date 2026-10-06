using System.Text.Json;
using Ingressos.Contracts.Eventos;
using Ingressos.Domain.Entidades;
using Ingressos.Messaging;
using Ingressos.Persistence;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace Ingressos.LiberadorReserva;

// Trata reservas que expiraram: a fila reservas-pendentes tem TTL de 10 min e dead-letter para
// ingressos.dlx, que entrega aqui a mensagem original de reserva.criada. Se a reserva ainda nao
// foi paga, libera o assento (Topico 2.3.3).
public class Worker(ILogger<Worker> logger, IDbContextFactory<IngressosDbContext> dbFactory) : ConsumidorBase(logger)
{
    protected override string Fila => NomesTopologia.FilaReservasExpiradas;

    protected override async Task ProcessarAsync(ReadOnlyMemory<byte> corpo, IReadOnlyBasicProperties propriedades, CancellationToken ct)
    {
        var reservaCriada = MensagemPublisher.Desserializar<ReservaCriada>(corpo);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var reserva = await db.Reservas.FirstOrDefaultAsync(r => r.Id == reservaCriada.ReservaId, ct);
        if (reserva is null || reserva.Status != StatusReserva.Reservada && reserva.Status != StatusReserva.AguardandoPagamento)
        {
            // Ja foi paga, ja foi liberada, ou nao existe mais: nada a fazer (idempotente).
            return;
        }

        reserva.Status = StatusReserva.Expirada;

        var assento = await db.Assentos.FirstOrDefaultAsync(a => a.Id == reserva.AssentoId, ct);
        if (assento is not null)
        {
            assento.Status = StatusAssento.Disponivel;
        }

        db.OutboxMessages.Add(new OutboxMessage
        {
            RoutingKey = "assento.liberado",
            TipoMensagem = nameof(AssentoLiberado),
            PayloadJson = JsonSerializer.Serialize(new AssentoLiberado(reserva.AssentoId, reserva.Id, "reserva_expirada_ttl"), JsonSerializacao.Opcoes)
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Reserva {ReservaId} expirada; assento {AssentoId} liberado", reserva.Id, reserva.AssentoId);
    }
}
