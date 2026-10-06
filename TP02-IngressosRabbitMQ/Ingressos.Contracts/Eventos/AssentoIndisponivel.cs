namespace Ingressos.Contracts.Eventos;

// Routing key: assento.indisponivel
public record AssentoIndisponivel(Guid UsuarioId, Guid SetorId, string Motivo);
