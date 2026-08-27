using System.Net;
using System.Net.Sockets;

namespace ChatDistribuido
{
    public static class Connection
    {
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

        /// <summary>Escuta especificamente no host anunciado, não em todas as interfaces —
        /// necessário para rodar múltiplas instâncias na mesma máquina usando a mesma porta em
        /// endereços diferentes (ex.: vários aliases de loopback 127.0.x.x simulando "redes"
        /// distintas). Se o host anunciado não for um endereço local desta máquina (ex.: IP
        /// público atrás de NAT/port-forwarding), cai para todas as interfaces (0.0.0.0).</summary>
        public static Socket CreateListener(string bindHost, int port)
        {
            var listener = new Socket(
                addressFamily: AddressFamily.InterNetwork,
                socketType: SocketType.Stream,
                protocolType: ProtocolType.Tcp);

            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            var bindAddress = IPAddress.TryParse(bindHost, out var parsed) ? parsed : IPAddress.Any;
            try
            {
                listener.Bind(new IPEndPoint(bindAddress, port));
            }
            catch (SocketException) when (!bindAddress.Equals(IPAddress.Any))
            {
                listener.Bind(new IPEndPoint(IPAddress.Any, port));
            }

            listener.Listen(16);
            return listener;
        }

        /// <summary>Tenta conectar uma vez; retorna null (em vez de lançar) em timeout, recusa ou host inalcançável,
        /// para que o chamador decida a política de retry sem tratar exceção como fluxo de controle.</summary>
        public static async Task<Socket?> TryConnectAsync(string host, int port, CancellationToken ct, TimeSpan? timeout = null)
        {
            var socket = new Socket(
                addressFamily: AddressFamily.InterNetwork,
                socketType: SocketType.Stream,
                protocolType: ProtocolType.Tcp)
            { NoDelay = true };

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeout ?? ConnectTimeout);
                await socket.ConnectAsync(host, port, timeoutCts.Token);
                return socket;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                socket.Dispose();
                return null;
            }
            catch (SocketException)
            {
                socket.Dispose();
                return null;
            }
        }
    }
}
