namespace Ingressos.Contracts.Comandos;

// Resposta sincrona do padrao RPC de antifraude.solicitado (via reply_to + correlation_id).
public record AntifraudeResposta(bool RiscoAceitavel, string Motivo);
