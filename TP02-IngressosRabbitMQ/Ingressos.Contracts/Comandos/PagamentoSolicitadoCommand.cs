namespace Ingressos.Contracts.Comandos;

// Routing key: pagamento.solicitado
public record PagamentoSolicitadoCommand(Guid ReservaId, decimal Valor, string IdempotencyKey);
