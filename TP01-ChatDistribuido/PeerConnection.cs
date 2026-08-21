using System.Net.Sockets;
using System.Threading.Channels;

namespace ChatDistribuido
{
    /// <summary>
    /// Uma conexão TCP estabelecida com um par da malha: um socket, uma fila de saída própria
    /// e um par de tarefas (recepção/envio) isoladas por seu próprio CancellationTokenSource,
    /// de forma que a falha deste par nunca afeta os demais peers conectados ao nó.
    /// </summary>
    public sealed class PeerConnection : IAsyncDisposable
    {
        private const int SendQueueCapacity = 64;
        private const int MaxConsecutiveSendFailures = 3;
        private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan IdleReadTimeout = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

        private readonly Socket _socket;
        private readonly MeshNode _node;
        private readonly Channel<byte[]> _outbox;
        private readonly CancellationTokenSource _cts;
        private int _consecutiveSendFailures;

        public PeerId RemotePeer { get; }
        public bool IsOutbound { get; }

        public PeerConnection(Socket socket, PeerId remotePeer, bool isOutbound, MeshNode node, CancellationToken parentCt)
        {
            _socket = socket;
            RemotePeer = remotePeer;
            IsOutbound = isOutbound;
            _node = node;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(parentCt);

            // Fila de saída limitada por peer: um peer lento/travado nunca bloqueia o produtor
            // (broadcast/console), pois TryEnqueue nunca espera — mensagens antigas são descartadas
            // (DropOldest) em favor das mais recentes quando a fila enche. Ver README para justificativa.
            _outbox = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(SendQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
        }

        public void Start()
        {
            _ = RunReceiveLoopAsync();
            _ = RunSendLoopAsync();
            _ = RunHeartbeatLoopAsync();
        }

        public bool TryEnqueue(WireMessage message) => _outbox.Writer.TryWrite(message.ToFrame());

        /// <summary>Envio direto de melhor esforço fora da fila, usado apenas para o Bye de /quit
        /// (quando o nó está encerrando e não faz sentido esperar a fila normal escoar).</summary>
        public async Task SendBestEffortAsync(WireMessage message, TimeSpan timeout)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeout);
                await Frames.WriteAsync(_socket, message.ToFrame(), cts.Token);
            }
            catch { /* melhor esforço: a conexão pode já estar caindo */ }
        }

        private async Task RunReceiveLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    byte[]? frame;
                    using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                    {
                        readCts.CancelAfter(IdleReadTimeout);
                        try
                        {
                            frame = await Frames.ReadAsync(_socket, readCts.Token);
                        }
                        catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
                        {
                            await _node.OnPeerFailedAsync(this, "sem resposta (timeout de leitura ociosa)");
                            return;
                        }
                    }

                    if (frame is null)
                    {
                        await _node.OnPeerFailedAsync(this, "conexão encerrada pelo par");
                        return;
                    }

                    var message = WireMessage.FromFrame(frame);
                    await _node.OnMessageReceivedAsync(this, message);

                    if (message.Type == MessageType.Bye)
                        return;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await _node.OnPeerFailedAsync(this, $"erro de leitura: {ex.GetType().Name}");
            }
        }

        private async Task RunSendLoopAsync()
        {
            try
            {
                await foreach (var payload in _outbox.Reader.ReadAllAsync(_cts.Token))
                {
                    using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                    sendCts.CancelAfter(SendTimeout);
                    try
                    {
                        await Frames.WriteAsync(_socket, payload, sendCts.Token);
                        _consecutiveSendFailures = 0;
                    }
                    catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
                    {
                        _consecutiveSendFailures++;
                        if (_consecutiveSendFailures >= MaxConsecutiveSendFailures)
                        {
                            await _node.OnPeerFailedAsync(this, "não consome mensagens (timeout de envio persistente)");
                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await _node.OnPeerFailedAsync(this, $"erro de envio: {ex.GetType().Name}");
            }
        }

        private async Task RunHeartbeatLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    await Task.Delay(HeartbeatInterval, _cts.Token);
                    TryEnqueue(WireMessage.Ping(_node.Self.Nickname));
                }
            }
            catch (OperationCanceledException) { }
        }

        public void Cancel() => _cts.Cancel();

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { _socket.Shutdown(SocketShutdown.Both); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            _socket.Dispose();
            _cts.Dispose();
            await Task.CompletedTask;
        }
    }
}
