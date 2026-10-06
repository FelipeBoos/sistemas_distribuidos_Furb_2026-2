var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// TODO (Topico 3): consumir sala-espera.status e repassar a posicao na fila ao navegador via
// WebSocket, replicando uma unica mensagem a milhares de conexoes.
app.MapGet("/", () => "Ingressos.GatewayWebSocket");

app.Run();
