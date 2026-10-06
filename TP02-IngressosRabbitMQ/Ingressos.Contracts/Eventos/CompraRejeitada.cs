namespace Ingressos.Contracts.Eventos;

// Routing key: compra.rejeitada.* (limite de CPF, cota de meia-entrada ou risco alto no antifraude)
public record CompraRejeitada(Guid UsuarioId, string Motivo);
