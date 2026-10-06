using System.Globalization;
using Ingressos.Contracts.Comandos;
using Ingressos.Messaging;
using RabbitMQ.Client;

namespace Ingressos.Antifraude;

// Avalia o risco da compra e responde de forma sincrona (padrao RPC via reply_to +
// correlation_id). Nesta demonstracao a avaliacao e simulada por uma taxa de risco configuravel
// (ANTIFRAUDE_TAXA_RISCO_ALTO, 0 a 1), em vez de um modelo real de score.
public class Worker(ILogger<Worker> logger) : ConsumidorBase(logger)
{
    protected override string Fila => NomesTopologia.FilaAntifraude;

    // CultureInfo.InvariantCulture e obrigatorio aqui: o valor no .env sempre usa "." como
    // separador decimal, mas double.TryParse sem cultura explicita usa a cultura do sistema
    // operacional - em uma maquina com locale pt-BR (onde "." e separador de milhar), "0.05"
    // seria interpretado incorretamente (e nao como 0,05).
    private static readonly double TaxaRiscoAlto =
        double.TryParse(Environment.GetEnvironmentVariable("ANTIFRAUDE_TAXA_RISCO_ALTO"), NumberStyles.Float, CultureInfo.InvariantCulture, out var taxa)
            ? taxa
            : 0.05;

    protected override async Task ProcessarAsync(ReadOnlyMemory<byte> corpo, IReadOnlyBasicProperties propriedades, CancellationToken ct)
    {
        var requisicao = MensagemPublisher.Desserializar<AntifraudeSolicitadoCommand>(corpo);
        var riscoAlto = Random.Shared.NextDouble() < TaxaRiscoAlto;

        var resposta = riscoAlto
            ? new AntifraudeResposta(RiscoAceitavel: false, Motivo: "padrao_de_compra_suspeito")
            : new AntifraudeResposta(RiscoAceitavel: true, Motivo: "ok");

        logger.LogInformation("Antifraude para usuario {UsuarioId}: {Resultado}", requisicao.UsuarioId, resposta.Motivo);
        await ResponderRpcAsync(propriedades, resposta);
    }
}
