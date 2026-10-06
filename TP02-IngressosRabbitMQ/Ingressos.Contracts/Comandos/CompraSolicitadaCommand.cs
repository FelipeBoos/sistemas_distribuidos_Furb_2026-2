namespace Ingressos.Contracts.Comandos;

// Routing key: compra.solicitada.<setor>
public record CompraSolicitadaCommand(Guid UsuarioId, Guid SetorId, int QuantidadeIngressos, bool MeiaEntrada);
