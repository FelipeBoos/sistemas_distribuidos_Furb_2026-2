using Ingressos.Contracts.Comandos;
using Ingressos.Messaging;

DotEnv.Carregar();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

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

// Ponto de entrada do cliente: solicita entrada na sala de espera (Admissor controla a liberacao).
app.MapPost("/fila/entrar", async (EntrarFilaRequisicao requisicao, MensagemPublisher mensagens) =>
{
    var comando = new EntrarFilaCommand(requisicao.UsuarioId, requisicao.EventoId, DateTime.UtcNow);
    await mensagens.PublicarAsync("fila.entrar", comando);
    return Results.Accepted(value: new { status = "aguardando_admissao" });
});

// Solicita a compra em si, depois que o cliente foi admitido na area de compra.
app.MapPost("/compras", async (CompraRequisicao requisicao, MensagemPublisher mensagens) =>
{
    var comando = new CompraSolicitadaCommand(requisicao.UsuarioId, requisicao.SetorId, requisicao.QuantidadeIngressos, requisicao.MeiaEntrada);
    var routingKey = NomesTopologia.RoutingKeyCompraSolicitada(requisicao.Setor);
    await mensagens.PublicarAsync(routingKey, comando);
    return Results.Accepted(value: new { status = "compra_solicitada" });
});

// Confirma o pagamento de uma reserva ja criada pelo Alocador. A IdempotencyKey evita cobranca
// duplicada se o cliente reenviar a mesma solicitacao (ex.: retry de rede no navegador).
app.MapPost("/pagamentos", async (PagamentoRequisicao requisicao, MensagemPublisher mensagens) =>
{
    var comando = new PagamentoSolicitadoCommand(requisicao.ReservaId, requisicao.Valor, requisicao.IdempotencyKey);
    await mensagens.PublicarAsync("pagamento.solicitado", comando);
    return Results.Accepted(value: new { status = "pagamento_solicitado" });
});

app.MapGet("/", () => "Ingressos.ApiVendas");

app.Run();

internal record EntrarFilaRequisicao(Guid UsuarioId, Guid EventoId);

internal record CompraRequisicao(Guid UsuarioId, Guid SetorId, string Setor, int QuantidadeIngressos, bool MeiaEntrada);

internal record PagamentoRequisicao(Guid ReservaId, decimal Valor, string IdempotencyKey);
