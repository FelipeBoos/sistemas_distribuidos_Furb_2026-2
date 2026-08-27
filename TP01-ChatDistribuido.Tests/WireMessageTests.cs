using ChatDistribuido;

namespace ChatDistribuido.Tests
{
    public class WireMessageTests
    {
        [Fact]
        public void Chat_RoundTripsThroughFrame()
        {
            var original = WireMessage.Chat("alice", "ola pessoal");

            var decoded = WireMessage.FromFrame(original.ToFrame());

            Assert.Equal(MessageType.Chat, decoded.Type);
            Assert.Equal("alice", decoded.From);
            Assert.Equal("ola pessoal", decoded.Text);
            Assert.Null(decoded.To);
        }

        [Fact]
        public void Private_RoundTripsRecipient()
        {
            var original = WireMessage.Private("alice", "bob", "segredo");

            var decoded = WireMessage.FromFrame(original.ToFrame());

            Assert.Equal(MessageType.Private, decoded.Type);
            Assert.Equal("bob", decoded.To);
            Assert.Equal("segredo", decoded.Text);
        }

        [Fact]
        public void Hello_RoundTripsListenEndpoint()
        {
            var original = WireMessage.Hello("carol", "127.0.0.1", 9003);

            var decoded = WireMessage.FromFrame(original.ToFrame());

            Assert.Equal(MessageType.Hello, decoded.Type);
            Assert.Equal("127.0.0.1", decoded.ListenHost);
            Assert.Equal(9003, decoded.ListenPort);
        }

        [Fact]
        public void Bye_RoundTripsSender()
        {
            var original = WireMessage.Bye("alice");

            var decoded = WireMessage.FromFrame(original.ToFrame());

            Assert.Equal(MessageType.Bye, decoded.Type);
            Assert.Equal("alice", decoded.From);
        }

        [Fact]
        public void PeerList_RoundTripsPeerArray()
        {
            var peers = new List<PeerInfo>
            {
                new("bob", "192.168.2.5", 9002),
                new("carol", "192.168.3.7", 5000)
            };
            var original = WireMessage.PeerList("alice", peers);

            var decoded = WireMessage.FromFrame(original.ToFrame());

            Assert.Equal(MessageType.PeerList, decoded.Type);
            Assert.Equal("alice", decoded.From);
            Assert.NotNull(decoded.Peers);
            Assert.Equal(peers, decoded.Peers);
        }
    }
}
