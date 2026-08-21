using ChatDistribuido;

namespace ChatDistribuido.Tests
{
    public class PeerIdTests
    {
        [Fact]
        public void EndpointKey_CombinesHostAndPort()
        {
            var peer = new PeerId("alice", "127.0.0.1", 9001);

            Assert.Equal("127.0.0.1:9001", peer.EndpointKey);
        }
    }
}
