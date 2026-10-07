namespace Ingressos.Contracts.Comandos;

// Routing key: pagamento.solicitado. O valor nao viaja no comando: o ServicoPagamento o calcula
// a partir do preco do setor (o cliente nao pode definir o preco).
public record PagamentoSolicitadoCommand(Guid ReservaId, string IdempotencyKey);
