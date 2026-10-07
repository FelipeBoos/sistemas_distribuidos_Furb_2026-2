using Ingressos.Contracts.Comandos;
using Ingressos.Domain.Entidades;
using Ingressos.Domain.Regras;
using Ingressos.Messaging;
using Ingressos.Persistence;
using Microsoft.EntityFrameworkCore;

DotEnv.Carregar();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddDbContextFactory<IngressosDbContext>(options =>
    options.UseNpgsql(PostgresOptions.ConnectionStringFromEnvironment()));

// Conexao e canal unicos do processo, publicados como singletons para os endpoints usarem.
var conexao = await RabbitMqConnection.ConectarAsync(RabbitMqOptions.FromEnvironment(), "Ingressos.ApiVendas");
var canal = await conexao.AbrirCanalAsync();
await Topologia.DeclararAsync(canal);
var publisher = new MensagemPublisher(canal);

builder.Services.AddSingleton(publisher);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Tela de demonstracao (wwwroot/index.html), servida pela propria API (mesma origem, sem CORS).
app.UseDefaultFiles();
app.UseStaticFiles();

// Ponto de entrada do cliente: solicita entrada na sala de espera (Admissor controla a liberacao).
app.MapPost("/fila/entrar", async (EntrarFilaRequisicao requisicao, MensagemPublisher mensagens) =>
{
    var comando = new EntrarFilaCommand(requisicao.UsuarioId, requisicao.EventoId, DateTime.UtcNow);
    await mensagens.PublicarAsync("fila.entrar", comando);
    return Results.Accepted(value: new { status = "aguardando_admissao" });
});

// Solicita a compra em si, depois que o cliente foi admitido na area de compra. A quantidade
// gera uma reserva por ingresso. O setor e lido do banco, para que a routing key nunca seja
// montada a partir de texto livre enviado pelo cliente.
app.MapPost("/compras", async (CompraRequisicao requisicao, IDbContextFactory<IngressosDbContext> dbFactory, MensagemPublisher mensagens, ILogger<Program> logger, CancellationToken ct) =>
{
    if (!RegrasCompra.QuantidadeValida(requisicao.QuantidadeIngressos))
    {
        return Results.BadRequest(new { erro = $"A quantidade deve estar entre 1 e {RegrasCompra.QuantidadeMaximaPorCompra}." });
    }

    await using var db = await dbFactory.CreateDbContextAsync(ct);
    var setor = await db.Setores.FirstOrDefaultAsync(s => s.Id == requisicao.SetorId, ct);
    if (setor is null)
    {
        return Results.NotFound(new { erro = "Setor nao encontrado." });
    }

    var comando = new CompraSolicitadaCommand(requisicao.UsuarioId, requisicao.SetorId, requisicao.QuantidadeIngressos, requisicao.MeiaEntrada);
    try
    {
        await mensagens.PublicarAsync(NomesTopologia.RoutingKeyCompraSolicitada(setor.Nome), comando);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        // Sem confirmacao do broker a compra nao foi aceita: responder 202 mentiria ao cliente.
        logger.LogError(ex, "Falha ao publicar a compra do usuario {UsuarioId}", requisicao.UsuarioId);
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Accepted(value: new { status = "compra_solicitada" });
});

// Confirma o pagamento de uma reserva ja criada pelo Alocador. O valor nao vem do cliente: o
// ServicoPagamento calcula a partir do preco do setor. A IdempotencyKey evita cobranca duplicada
// se o cliente reenviar a mesma solicitacao (ex.: retry de rede no navegador).
app.MapPost("/pagamentos", async (PagamentoRequisicao requisicao, MensagemPublisher mensagens, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(requisicao.IdempotencyKey))
    {
        return Results.BadRequest(new { erro = "IdempotencyKey e obrigatoria." });
    }

    var comando = new PagamentoSolicitadoCommand(requisicao.ReservaId, requisicao.IdempotencyKey);
    try
    {
        await mensagens.PublicarAsync("pagamento.solicitado", comando);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        logger.LogError(ex, "Falha ao publicar o pagamento da reserva {ReservaId}", requisicao.ReservaId);
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Accepted(value: new { status = "pagamento_solicitado" });
});

// Consultas usadas pela tela de demonstracao: setores disponiveis e reservas do usuario.
app.MapGet("/setores", async (IDbContextFactory<IngressosDbContext> dbFactory, CancellationToken ct) =>
{
    await using var db = await dbFactory.CreateDbContextAsync(ct);
    var setores = await db.Setores
        .OrderBy(s => s.Nome)
        .Select(s => new { s.Id, s.Nome, s.Preco, s.Capacidade })
        .ToListAsync(ct);

    return Results.Ok(setores);
});

app.MapGet("/reservas", async (Guid usuarioId, IDbContextFactory<IngressosDbContext> dbFactory, CancellationToken ct) =>
{
    await using var db = await dbFactory.CreateDbContextAsync(ct);

    var linhas = await (
        from r in db.Reservas
        join a in db.Assentos on r.AssentoId equals a.Id
        join s in db.Setores on a.SetorId equals s.Id
        where r.UsuarioId == usuarioId
        orderby r.CriadaEm descending
        select new { r.Id, r.Status, r.MeiaEntrada, Setor = s.Nome, Assento = a.Codigo, s.Preco }
    ).Take(20).ToListAsync(ct);

    var reservaIds = linhas.Select(l => l.Id).ToList();
    var pagamentos = await db.Pagamentos
        .Where(p => reservaIds.Contains(p.ReservaId))
        .Select(p => new { p.Id, p.ReservaId, p.Status, p.Valor })
        .ToListAsync(ct);
    var pagamentoIds = pagamentos.Select(p => p.Id).ToList();
    var ingressos = await db.Ingressos
        .Where(i => pagamentoIds.Contains(i.PagamentoId))
        .Select(i => new { i.PagamentoId, i.QrCode })
        .ToListAsync(ct);

    var resposta = linhas.Select(l =>
    {
        var pagamento = pagamentos.FirstOrDefault(p => p.ReservaId == l.Id);
        var ingresso = pagamento is null ? null : ingressos.FirstOrDefault(i => i.PagamentoId == pagamento.Id);
        return new ReservaResposta(
            l.Id,
            l.Setor,
            l.Assento,
            l.Status.ToString(),
            RegrasCompra.CalcularValor(l.Preco, l.MeiaEntrada),
            pagamento?.Status.ToString(),
            ingresso?.QrCode);
    });

    return Results.Ok(resposta);
});

// Verificacao de saude (a raiz "/" e servida pela tela de demonstracao, wwwroot/index.html).
app.MapGet("/saude", () => "Ingressos.ApiVendas");

app.Run();

internal record EntrarFilaRequisicao(Guid UsuarioId, Guid EventoId);

internal record CompraRequisicao(Guid UsuarioId, Guid SetorId, int QuantidadeIngressos, bool MeiaEntrada);

internal record PagamentoRequisicao(Guid ReservaId, string IdempotencyKey);

internal record ReservaResposta(
    Guid ReservaId,
    string Setor,
    string Assento,
    string Status,
    decimal Valor,
    string? StatusPagamento,
    string? QrCode);
