using Ingressos.Messaging;
using Ingressos.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ingressos.OutboxRelay;

// Garante que dados gravados virem mensagem (padrao Outbox, Topico 2.3.2): le periodicamente as
// linhas pendentes da tabela outbox (gravadas na mesma transacao que a entidade de dominio pelos
// demais servicos) e publica cada uma no RabbitMQ, marcando como publicada.
public class Worker(ILogger<Worker> logger, IDbContextFactory<IngressosDbContext> dbFactory) : BackgroundService
{
    private static readonly TimeSpan IntervaloPolling = TimeSpan.FromSeconds(2);
    private const int TamanhoLote = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var conexao = await RabbitMqConnection.ConectarAsync(RabbitMqOptions.FromEnvironment(), "Ingressos.OutboxRelay");
        var canal = await conexao.AbrirCanalAsync();
        await Topologia.DeclararAsync(canal);
        var publisher = new MensagemPublisher(canal);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublicarPendentesAsync(publisher, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao publicar pendencias da outbox");
            }

            await Task.Delay(IntervaloPolling, stoppingToken);
        }
    }

    private async Task PublicarPendentesAsync(MensagemPublisher publisher, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var pendentes = await db.OutboxMessages
            .Where(o => o.PublicadaEm == null)
            .OrderBy(o => o.CriadaEm)
            .Take(TamanhoLote)
            .ToListAsync(ct);

        foreach (var mensagem in pendentes)
        {
            await publisher.PublicarBrutoAsync(mensagem.RoutingKey, mensagem.PayloadJson, mensagem.TipoMensagem, mensagemId: mensagem.Id);
            mensagem.PublicadaEm = DateTime.UtcNow;
        }

        if (pendentes.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Outbox: {Quantidade} mensagem(ns) publicada(s)", pendentes.Count);
        }
    }
}
