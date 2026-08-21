using System.Collections.Concurrent;
using System.Net.Sockets;

namespace ChatDistribuido
{
    /// <summary>
    /// Orquestra a malha completa (full mesh) de um nó: aceita conexões de entrada, disca para os
    /// pares conhecidos, mantém o dicionário local de peers conectados e roteia mensagens.
    /// Não existe visão global sincronizada — cada nó só conhece os peers com quem tem conexão direta.
    /// </summary>
    public sealed class MeshNode : IAsyncDisposable
    {
        private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ByeGraceTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan DialInitialBackoff = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan DialMaxBackoff = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan DialRecheckInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DialRetryAfterConnected = TimeSpan.FromSeconds(3);

        private readonly NodeConfig _config;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<string, PeerConnection> _peers = new();
        private readonly Socket _listener;
        private readonly Random _jitter = new();

        public PeerId Self { get; }

        public MeshNode(NodeConfig config)
        {
            _config = config;
            Self = new PeerId(config.Name, config.AdvertiseHost, config.Port);
            _listener = Connection.CreateListener(config.Port);
        }

        public async Task RunAsync()
        {
            ConsoleLog.WriteLine($"[{Self.Nickname}] escutando em 0.0.0.0:{Self.Port} (anunciado como {Self.EndpointKey})");

            var acceptTask = AcceptLoopAsync();
            var dialTasks = _config.KnownPeers.Select(p => DialLoopAsync(p.Host, p.Port)).ToArray();
            var uiTask = new ConsoleUi(this).RunAsync(_cts.Token);

            await uiTask;
            _cts.Cancel();

            try { await acceptTask; } catch { }
            try { await Task.WhenAll(dialTasks); } catch { }
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    Socket incoming;
                    try
                    {
                        incoming = await _listener.AcceptAsync(_cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    incoming.NoDelay = true;
                    _ = HandleIncomingAsync(incoming);
                }
            }
            catch (ObjectDisposedException) { }
        }

        private async Task HandleIncomingAsync(Socket socket)
        {
            try
            {
                var remote = await HandshakeAsync(socket);
                if (remote is null)
                {
                    socket.Dispose();
                    return;
                }

                RegisterConnection(socket, remote, isOutbound: false);
            }
            catch (Exception ex)
            {
                ConsoleLog.WriteLine($"[handshake de entrada falhou: {ex.GetType().Name} - {ex.Message}]");
                socket.Dispose();
            }
        }

        private async Task DialLoopAsync(string host, int port)
        {
            var endpointKey = $"{host}:{port}";
            var backoff = DialInitialBackoff;

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    if (_peers.ContainsKey(endpointKey))
                    {
                        await Task.Delay(DialRecheckInterval, _cts.Token);
                        continue;
                    }

                    var socket = await Connection.TryConnectAsync(host, port, _cts.Token);

                    if (socket is null)
                    {
                        var jitterMs = _jitter.Next(0, 500);
                        await Task.Delay(backoff + TimeSpan.FromMilliseconds(jitterMs), _cts.Token);
                        backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 1.5, DialMaxBackoff.TotalSeconds));
                        continue;
                    }

                    backoff = DialInitialBackoff;

                    try
                    {
                        var remote = await HandshakeAsync(socket);
                        if (remote is not null)
                            RegisterConnection(socket, remote, isOutbound: true);
                        else
                            socket.Dispose();
                    }
                    catch (Exception ex)
                    {
                        ConsoleLog.WriteLine($"[handshake de saída para {endpointKey} falhou: {ex.GetType().Name}]");
                        socket.Dispose();
                    }

                    await Task.Delay(DialRetryAfterConnected, _cts.Token);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task<PeerId?> HandshakeAsync(Socket socket)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            cts.CancelAfter(HandshakeTimeout);

            var myHello = WireMessage.Hello(Self.Nickname, Self.Host, Self.Port);

            var sendTask = Frames.WriteAsync(socket, myHello.ToFrame(), cts.Token);
            var receiveTask = Frames.ReadAsync(socket, cts.Token);
            await sendTask;
            var frame = await receiveTask;

            if (frame is null)
                return null;

            var remoteHello = WireMessage.FromFrame(frame);
            if (remoteHello.Type != MessageType.Hello || remoteHello.ListenHost is null || remoteHello.ListenPort is null)
                throw new InvalidDataException("Primeira mensagem recebida não foi um Hello válido.");

            return new PeerId(remoteHello.From, remoteHello.ListenHost, remoteHello.ListenPort.Value);
        }

        /// <summary>
        /// Registra uma conexão recém-estabelecida (accept ou connect). Se já existir uma conexão
        /// para o mesmo peer lógico (ambos os lados discaram simultaneamente), aplica a regra de
        /// desempate determinística: o lado de menor EndpointKey mantém a conexão que ACEITOU
        /// (inbound); o lado de maior EndpointKey mantém a que DISCOU (outbound). Como os dois lados
        /// aplicam a mesma regra de forma independente, chegam à mesma decisão sem coordenação extra.
        /// </summary>
        private void RegisterConnection(Socket socket, PeerId remote, bool isOutbound)
        {
            var key = remote.EndpointKey;

            if (_peers.TryGetValue(key, out var existing))
            {
                var thisConnectionShouldSurvive = ConnectionTieBreak.ShouldKeepNewConnection(
                    Self.EndpointKey, remote.EndpointKey, isOutbound);

                if (!thisConnectionShouldSurvive)
                {
                    try { socket.Shutdown(SocketShutdown.Both); } catch { }
                    socket.Dispose();
                    return;
                }

                if (((ICollection<KeyValuePair<string, PeerConnection>>)_peers).Remove(
                        new KeyValuePair<string, PeerConnection>(key, existing)))
                {
                    existing.Cancel();
                }
            }

            var connection = new PeerConnection(socket, remote, isOutbound, this, _cts.Token);
            if (!_peers.TryAdd(key, connection))
            {
                socket.Dispose();
                return;
            }

            connection.Start();
            ConsoleLog.WriteLine($"[{remote.Nickname} conectado — {key}]");
            Broadcast(WireMessage.PeerJoined(Self.Nickname, remote.Nickname, remote.Host, remote.Port), except: connection);
        }

        public Task OnMessageReceivedAsync(PeerConnection connection, WireMessage message)
        {
            switch (message.Type)
            {
                case MessageType.Chat:
                    ConsoleLog.WriteLine($"{message.From}: {message.Text}");
                    break;

                case MessageType.Private:
                    ConsoleLog.WriteLine($"[privado de {message.From}]: {message.Text}");
                    break;

                case MessageType.PeerJoined:
                    ConsoleLog.WriteLine($"[{message.Text} entrou na conversa]");
                    break;

                case MessageType.Bye:
                    ConsoleLog.WriteLine($"[{message.From} saiu da conversa]");
                    RemovePeer(connection);
                    break;

                case MessageType.Ping:
                case MessageType.Hello:
                    break;
            }

            return Task.CompletedTask;
        }

        public Task OnPeerFailedAsync(PeerConnection connection, string reason)
        {
            ConsoleLog.WriteLine($"[{connection.RemotePeer.Nickname} caiu: {reason}]");
            RemovePeer(connection);
            return Task.CompletedTask;
        }

        private void RemovePeer(PeerConnection connection)
        {
            var key = connection.RemotePeer.EndpointKey;
            _peers.TryRemove(new KeyValuePair<string, PeerConnection>(key, connection));
            _ = connection.DisposeAsync().AsTask();
        }

        private void Broadcast(WireMessage message, PeerConnection? except = null)
        {
            foreach (var peer in _peers.Values)
            {
                if (ReferenceEquals(peer, except))
                    continue;
                peer.TryEnqueue(message);
            }
        }

        public void SendChat(string text) => Broadcast(WireMessage.Chat(Self.Nickname, text));

        public bool TrySendPrivate(string toNickname, string text)
        {
            var target = _peers.Values.FirstOrDefault(p =>
                string.Equals(p.RemotePeer.Nickname, toNickname, StringComparison.OrdinalIgnoreCase));

            if (target is null)
                return false;

            target.TryEnqueue(WireMessage.Private(Self.Nickname, toNickname, text));
            return true;
        }

        public IReadOnlyList<PeerId> ListPeers() => _peers.Values.Select(p => p.RemotePeer).ToList();

        public async Task QuitAsync()
        {
            var bye = WireMessage.Bye(Self.Nickname);
            var sends = _peers.Values.Select(p => p.SendBestEffortAsync(bye, ByeGraceTimeout));
            await Task.WhenAll(sends);
            _cts.Cancel();
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Dispose();
            foreach (var peer in _peers.Values)
                await peer.DisposeAsync();
            _cts.Dispose();
        }
    }
}
