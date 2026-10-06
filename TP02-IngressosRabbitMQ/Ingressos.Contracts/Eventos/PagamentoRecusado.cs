namespace Ingressos.Contracts.Eventos;

// Routing key: pagamento.recusado (segue para filas de retry: 5s, 30s, 2min)
public record PagamentoRecusado(Guid PagamentoId, Guid ReservaId, string Motivo, int TentativaNumero);
