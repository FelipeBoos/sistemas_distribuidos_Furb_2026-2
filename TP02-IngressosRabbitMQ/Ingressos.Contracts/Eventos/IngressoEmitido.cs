namespace Ingressos.Contracts.Eventos;

// Routing key: ingresso.emitido
public record IngressoEmitido(Guid IngressoId, Guid PagamentoId, string QrCode);
