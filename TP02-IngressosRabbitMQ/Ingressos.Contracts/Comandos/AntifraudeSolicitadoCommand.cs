namespace Ingressos.Contracts.Comandos;

// Routing key: antifraude.solicitado (padrao RPC: reply_to + correlation_id)
public record AntifraudeSolicitadoCommand(Guid UsuarioId, Guid ReservaId, decimal ValorCompra);
