namespace ChatDistribuido
{
    public sealed class ConsoleUi
    {
        private readonly MeshNode _node;

        public ConsoleUi(MeshNode node) => _node = node;

        public async Task RunAsync(CancellationToken ct)
        {
            ConsoleLog.WriteLine($"Você é '{_node.Self.Nickname}'. Comandos: /list, /msg apelido texto, /quit.");

            while (!ct.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await Task.Run(Console.ReadLine, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (line is null)
                    return;

                if (line.Length == 0)
                    continue;

                if (line.Equals("/quit", StringComparison.OrdinalIgnoreCase))
                {
                    await _node.QuitAsync();
                    return;
                }

                if (line.Equals("/list", StringComparison.OrdinalIgnoreCase))
                {
                    PrintList();
                    continue;
                }

                if (line.StartsWith("/msg ", StringComparison.OrdinalIgnoreCase))
                {
                    HandleMsgCommand(line);
                    continue;
                }

                if (line.StartsWith('/'))
                {
                    ConsoleLog.WriteLine($"[comando desconhecido: {line}]");
                    continue;
                }

                _node.SendChat(line);
            }
        }

        private void PrintList()
        {
            var peers = _node.ListPeers();
            ConsoleLog.WriteLine($"Participantes conhecidos ({peers.Count}):");
            foreach (var peer in peers)
                ConsoleLog.WriteLine($"  - {peer.Nickname} ({peer.EndpointKey})");
        }

        private void HandleMsgCommand(string line)
        {
            var rest = line["/msg ".Length..];
            var separatorIndex = rest.IndexOf(' ');
            if (separatorIndex <= 0)
            {
                ConsoleLog.WriteLine("[uso: /msg apelido texto]");
                return;
            }

            var toNickname = rest[..separatorIndex];
            var text = rest[(separatorIndex + 1)..];

            if (!_node.TrySendPrivate(toNickname, text))
                ConsoleLog.WriteLine($"[peer '{toNickname}' não encontrado entre os conectados]");
        }
    }
}
