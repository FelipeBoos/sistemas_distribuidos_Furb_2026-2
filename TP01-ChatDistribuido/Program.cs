using ChatDistribuido;
using System.Net.Sockets;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    var config = PeerConfig.Parse(args);
    await using var node = new MeshNode(config);

    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        _ = node.QuitAsync();
    };

    await node.RunAsync();
    return 0;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Erro de configuração: {ex.Message}");
    PrintUsage();
    return 1;
}
catch (SocketException ex)
{
    Console.Error.WriteLine($"Erro de socket: {ex.SocketErrorCode} - {ex.Message}");
    return 1;
}

static void PrintUsage() => Console.WriteLine("""
    Chat distribuído em malha (full mesh), sem servidor central.

      chat --port <porta> --name <apelido> [--host <host-anunciado>] [--peers host:porta,host:porta,...]
      chat --config <arquivo.json>

    Exemplo:
      chat --port 9001 --name alice --peers 127.0.0.1:9002,127.0.0.1:9003

    Comandos durante a conversa:
      /list               lista participantes conhecidos
      /msg apelido texto  envia mensagem privada
      /quit               sai anunciando a saída
    """);
