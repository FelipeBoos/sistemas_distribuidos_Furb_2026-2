namespace Ingressos.Contracts.Eventos;

// Routing key: pagamento.aprovado (bindings multiplos: emissao, notificacao, auditoria)
public record PagamentoAprovado(Guid PagamentoId, Guid ReservaId, decimal Valor);
