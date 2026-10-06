namespace Ingressos.Contracts.Eventos;

// Routing key: sala-espera.status (publicado 1x/s pelo Admissor, consumido pelo Gateway WebSocket)
public record SalaEsperaStatus(Guid EventoId, int PosicaoNaFila, int TotalAdmitido);
