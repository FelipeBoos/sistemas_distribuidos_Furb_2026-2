namespace Ingressos.Contracts.Comandos;

// Routing key: fila.entrar
public record EntrarFilaCommand(Guid UsuarioId, Guid EventoId, DateTime SolicitadoEm);
