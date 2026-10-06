var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// TODO (Topico 3): expor endpoint HTTP/REST de solicitacao de compra, publicando fila.entrar
// e compra.solicitada.<setor>, e expor WebSocket para acompanhar a posicao na fila em tempo real.
app.MapGet("/", () => "Ingressos.ApiVendas");

app.Run();
