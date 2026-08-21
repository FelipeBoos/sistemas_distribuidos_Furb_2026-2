using System.Net;
using System.Net.Sockets;
using System.Text;
using ChatDistribuido;

namespace ChatDistribuido.Tests
{
    public class FramesTests
    {
        private static async Task<(Socket client, Socket server)> CreateConnectedPairAsync()
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            var port = ((IPEndPoint)listener.LocalEndPoint!).Port;

            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
            var server = await listener.AcceptAsync();
            await connectTask;

            return (client, server);
        }

        [Fact]
        public async Task WriteAndRead_RoundTripsPayload()
        {
            var (client, server) = await CreateConnectedPairAsync();
            using (client)
            using (server)
            {
                var payload = Encoding.UTF8.GetBytes("ola mundo");
                await Frames.WriteAsync(client, payload);

                var received = await Frames.ReadAsync(server);

                Assert.NotNull(received);
                Assert.Equal(payload, received);
            }
        }

        [Fact]
        public async Task BurstOfShortMessages_ArrivesUnglued()
        {
            var (client, server) = await CreateConnectedPairAsync();
            using (client)
            using (server)
            {
                var messages = new[] { "um", "dois", "tres", "quatro", "cinco" };
                foreach (var m in messages)
                    await Frames.WriteAsync(client, Encoding.UTF8.GetBytes(m));

                foreach (var expected in messages)
                {
                    var frame = await Frames.ReadAsync(server);
                    Assert.NotNull(frame);
                    Assert.Equal(expected, Encoding.UTF8.GetString(frame!));
                }
            }
        }

        [Fact]
        public async Task LongMessage_IsNotTruncated()
        {
            var (client, server) = await CreateConnectedPairAsync();
            using (client)
            using (server)
            {
                var payload = Encoding.UTF8.GetBytes(new string('x', 50_000));
                await Frames.WriteAsync(client, payload);

                var received = await Frames.ReadAsync(server);

                Assert.NotNull(received);
                Assert.Equal(payload.Length, received!.Length);
                Assert.Equal(payload, received);
            }
        }

        [Fact]
        public async Task PayloadAboveLimit_ThrowsArgumentException()
        {
            var (client, server) = await CreateConnectedPairAsync();
            using (client)
            using (server)
            {
                var oversized = new byte[Frames.MaxFrameSize + 1];
                await Assert.ThrowsAsync<ArgumentException>(() => Frames.WriteAsync(client, oversized));
            }
        }

        [Fact]
        public async Task ReadAfterCleanClose_ReturnsNull()
        {
            var (client, server) = await CreateConnectedPairAsync();
            using (server)
            {
                client.Shutdown(SocketShutdown.Send);
                client.Dispose();

                var frame = await Frames.ReadAsync(server);

                Assert.Null(frame);
            }
        }
    }
}
