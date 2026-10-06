namespace Ingressos.Contracts.Eventos;

// Routing key: assento.liberado (reserva expirada por TTL ou recusada apos retries)
public record AssentoLiberado(Guid AssentoId, Guid ReservaId, string Motivo);
