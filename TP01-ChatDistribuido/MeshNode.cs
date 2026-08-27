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
        private static readonly TimeSpan SubnetScanConnectTimeout = TimeSpan.FromSeconds(1.5);
        private const int SubnetScanConcurrency = 32;

        private readonly NodeConfig _config;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<string, PeerConnection> _peers = new();
        private readonly ConcurrentDictionary<string, byte> _activeDialTargets = new();
        private readonly ConcurrentBag<Task> _dialTasks = new();
        private readonly Socket _listener;
        private readonly Random _jitter = new();

        public PeerId Self { get; }

        public MeshNode(NodeConfig config)
        {
            _config = config;
            Self = new PeerId(config.Name, config.AdvertiseHost, config.Port);
            _listener = Connection.CreateListener(config.AdvertiseHost, config.Port);
        }

        public async Task RunAsync()
        {
            ConsoleLog.WriteLine($"[{Self.Nickname}] escutando em {Self.EndpointKey}");

            var acceptTask = AcceptLoopAsync();
            StartInitialDialTargets();
            var uiTask = new ConsoleUi(this).RunAsync(_cts.Token);

            await uiTask;
            _cts.Cancel();

            try { await acceptTask; } catch { }
            try { await Task.WhenAll(_dialTasks.ToArray()); } catch { }
        }

        /// <summary>
        /// Monta os alvos de discagem. A própria sub-rede local é sempre varrida automaticamente
        /// (com ou sem --peers informado) — --peers serve para apontar peers de outras redes
        /// (ponte) ou sobrescrever/complementar a varredura local. Entradas terminadas em ".*"
        /// viram varredura de sub-rede; entradas normais são discadas diretamente, com a porta
        /// sobrescrita para <see cref="NetworkRules.CrossNetworkPort"/> quando o host está numa
        /// sub-rede diferente da minha — é o "ponto de entrada" numa rede desconhecida. Uma vez
        /// dentro, os peers aprendidos via <see cref="MessageType.PeerList"/> (ver
        /// <see cref="OnMessageReceivedAsync"/>) são discados na porta real que eles reportam,
        /// sem essa sobrescrita.
        /// </summary>
        private void StartInitialDialTargets()
        {
            var localPrefix = NetworkRules.SubnetPrefixOf(Self.Host);
            if (localPrefix is not null)
                StartSubnetScan(localPrefix, Self.Port);

            foreach (var peer in _config.KnownPeers)
            {
                if (peer.Host.EndsWith(".*", StringComparison.Ordinal))
                {
                    StartSubnetScan(peer.Host[..^1], peer.Port);
                    continue;
                }

                var effectivePort = NetworkRules.ResolveDialPort(Self.Host, peer.Host, peer.Port);
                StartDialLoop(peer.Host, effectivePort);
            }
        }

        /// <summary>Inicia um dial loop para host:port, evitando duplicar um loop já em
        /// andamento para o mesmo alvo (ex.: configurado em --peers e também aprendido via
        /// gossip de PeerList).</summary>
        private void StartDialLoop(string host, int port)
        {
            var endpointKey = $"{host}:{port}";
            if (endpointKey == Self.EndpointKey)
                return;

            if (_activeDialTargets.TryAdd(endpointKey, 0))
                _dialTasks.Add(DialLoopAsync(host, port));
        }

        private void StartSubnetScan(string subnetPrefix, int port)
        {
            var scanKey = $"scan:{subnetPrefix}*:{port}";
            if (_activeDialTargets.TryAdd(scanKey, 0))
                _dialTasks.Add(SubnetScanLoopAsync(subnetPrefix, port));
        }

        private async Task SubnetScanLoopAsync(string subnetPrefix, int port)
        {
            using var throttle = new SemaphoreSlim(SubnetScanConcurrency);

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var attempts = Enumerable.Range(1, 254).Select(async i =>
                    {
                        var host = $"{subnetPrefix}{i}";
                        var endpointKey = $"{host}:{port}";
                        if (endpointKey == Self.EndpointKey || _peers.ContainsKey(endpointKey))
                            return;

                        try
                        {
                            await throttle.WaitAsync(_cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }

                        try
                        {
                            var socket = await Connection.TryConnectAsync(host, port, _cts.Token, SubnetScanConnectTimeout);
                            if (socket is null)
                                return;

                            var remote = await HandshakeAsync(socket);
                            if (remote is not null)
                                RegisterConnection(socket, remote, isOutbound: true);
                            else
                                socket.Dispose();
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception) { }
                        finally
                        {
                            throttle.Release();
                        }
                    });

                    await Task.WhenAll(attempts);
                    await Task.Delay(DialRecheckInterval, _cts.Token);
                }
            }
            catch (OperationCanceledException) { }
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
        /// para o mesmo peer lógico, decide qual sobrevive:
        /// - Mesma direção da existente (ambas outbound ou ambas inbound): não é uma discagem
        ///   simultânea de dois lados diferentes, e sim duas tentativas nossas concorrentes para o
        ///   mesmo alvo (ex.: a varredura de sub-rede e um dial loop disparado por gossip de
        ///   PeerList disputando o mesmo peer) — mantém a conexão já estabelecida e descarta a
        ///   nova, evitando um ciclo de reconectar/substituir sem fim.
        /// - Direções diferentes (um lado discou, o outro aceitou): aplica a regra de desempate
        ///   determinística — o lado de menor EndpointKey mantém a conexão que ACEITOU (inbound);
        ///   o lado de maior mantém a que DISCOU (outbound). Como os dois lados aplicam a mesma
        ///   regra de forma independente, chegam à mesma decisão sem coordenação extra.
        /// </summary>
        private void RegisterConnection(Socket socket, PeerId remote, bool isOutbound)
        {
            var key = remote.EndpointKey;

            if (_peers.TryGetValue(key, out var existing))
            {
                var thisConnectionShouldSurvive = existing.IsOutbound != isOutbound &&
                    ConnectionTieBreak.ShouldKeepNewConnection(Self.EndpointKey, remote.EndpointKey, isOutbound);

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
            SharePeerListWith(connection);
        }

        /// <summary>
        /// Envia ao peer recém-conectado a lista de todos os outros peers que já conheço, para
        /// que ele possa discar diretamente para eles — é o mecanismo que permite a um nó, ao
        /// entrar por um único peer-ponte, alcançar transitivamente toda a malha que esse peer
        /// já enxerga (inclusive outra malha/sub-rede à qual o peer-ponte também esteja conectado).
        /// </summary>
        private void SharePeerListWith(PeerConnection connection)
        {
            var others = _peers.Values
                .Where(p => p.RemotePeer.EndpointKey != connection.RemotePeer.EndpointKey)
                .Select(p => new PeerInfo(p.RemotePeer.Nickname, p.RemotePeer.Host, p.RemotePeer.Port))
                .ToList();

            if (others.Count > 0)
                connection.TryEnqueue(WireMessage.PeerList(Self.Nickname, others));
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

                case MessageType.PeerList:
                    OnPeerListReceived(message.Peers);
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

        /// <summary>Disca para cada peer aprendido via gossip que ainda não conheço, usando a
        /// porta exatamente como reportada — sem sobrescrever para a porta de rede externa, já
        /// que veio de um peer confiável de dentro daquela malha.</summary>
        private void OnPeerListReceived(IReadOnlyList<PeerInfo>? peers)
        {
            if (peers is null)
                return;

            foreach (var peer in peers)
            {
                var candidateKey = $"{peer.Host}:{peer.Port}";
                if (candidateKey == Self.EndpointKey || _peers.ContainsKey(candidateKey))
                    continue;

                StartDialLoop(peer.Host, peer.Port);
            }
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

        /// <summary>Todos os peers conectados com aquele apelido — pode ter mais de um, cabe à
        /// UI desambiguar antes de enviar (ver <see cref="TrySendPrivateTo"/>).</summary>
        public IReadOnlyList<PeerId> FindPeersByNickname(string nickname) =>
            PeerLookup.FindByNickname(_peers.Values.Select(p => p.RemotePeer), nickname);

        /// <summary>Envia direto pela conexão daquele EndpointKey específico — usado depois que a
        /// UI já resolveu qual peer exatamente, entre os de mesmo apelido.</summary>
        public bool TrySendPrivateTo(string endpointKey, string text)
        {
            if (!_peers.TryGetValue(endpointKey, out var target))
                return false;

            target.TryEnqueue(WireMessage.Private(Self.Nickname, target.RemotePeer.Nickname, text));
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
