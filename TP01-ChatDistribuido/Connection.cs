using System.Net;
using System.Net.Sockets;

namespace ChatDistribuido
{
    public static class Connection
    {
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

        public static Socket CreateListener(int port)
        {
            var listener = new Socket(
                addressFamily: AddressFamily.InterNetwork,
                socketType: SocketType.Stream,
                protocolType: ProtocolType.Tcp);

            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Bind(new IPEndPoint(IPAddress.Any, port));
            listener.Listen(16);
            return listener;
        }

        /// <summary>Tenta conectar uma vez; retorna null (em vez de lançar) em timeout, recusa ou host inalcançável,
        /// para que o chamador decida a política de retry sem tratar exceção como fluxo de controle.</summary>
        public static async Task<Socket?> TryConnectAsync(string host, int port, CancellationToken ct)
        {
            var socket = new Socket(
                addressFamily: AddressFamily.InterNetwork,
                socketType: SocketType.Stream,
                protocolType: ProtocolType.Tcp)
            { NoDelay = true };

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(ConnectTimeout);
                await socket.ConnectAsync(host, port, timeout.Token);
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
