using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace Ingressos.GatewayWebSocket;

// Mantem as conexoes WebSocket abertas e repassa cada status recebido do RabbitMQ a todas elas -
// uma unica mensagem de entrada (sala-espera.status) vira N mensagens de saida, uma por cliente
// conectado (Topico 2.3.1 - escalabilidade da sala de espera).
public class WebSocketBroadcaster
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _conexoes = new();

    public Guid Registrar(WebSocket socket)
    {
        var id = Guid.NewGuid();
        _conexoes[id] = socket;
        return id;
    }

    public void Remover(Guid id) => _conexoes.TryRemove(id, out _);

    public async Task BroadcastAsync(string mensagemJson, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(mensagemJson);

        foreach (var (id, socket) in _conexoes)
        {
            if (socket.State != WebSocketState.Open)
            {
                Remover(id);
                continue;
            }

            try
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
            }
            catch (Exception)
            {
                Remover(id);
            }
        }
    }
}
