namespace Ingressos.Contracts.Eventos;

// Routing key: reserva.criada
public record ReservaCriada(Guid ReservaId, Guid UsuarioId, Guid AssentoId, DateTime ExpiraEm);
