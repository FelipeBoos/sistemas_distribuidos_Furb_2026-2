using Ingressos.GatewayWebSocket;

Ingressos.Messaging.DotEnv.Carregar();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<WebSocketBroadcaster>();
builder.Services.AddHostedService<StatusConsumer>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseWebSockets();

// Repassa a posicao na fila ao navegador em tempo real (Topico 2.1).
app.Map("/ws/sala-espera", async (HttpContext context, WebSocketBroadcaster broadcaster) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var id = broadcaster.Registrar(socket);

    var buffer = new byte[1024];
    try
    {
        while (socket.State == System.Net.WebSockets.WebSocketState.Open)
        {
            var resultado = await socket.ReceiveAsync(buffer, context.RequestAborted);
            if (resultado.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
            {
                break;
            }
        }
    }
    finally
    {
        broadcaster.Remover(id);
    }
});

app.MapGet("/", () => "Ingressos.GatewayWebSocket");

app.Run();
