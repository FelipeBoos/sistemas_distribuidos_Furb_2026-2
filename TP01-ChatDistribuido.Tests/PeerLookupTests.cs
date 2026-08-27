using ChatDistribuido;

namespace ChatDistribuido.Tests
{
    public class PeerLookupTests
    {
        [Fact]
        public void FindByNickname_ReturnsEmpty_WhenNoMatch()
        {
            var peers = new[] { new PeerId("alice", "127.0.0.1", 9001) };

            var result = PeerLookup.FindByNickname(peers, "bob");

            Assert.Empty(result);
        }

        [Fact]
        public void FindByNickname_ReturnsSingle_WhenOneMatch()
        {
            var peers = new[]
            {
                new PeerId("alice", "127.0.0.1", 9001),
                new PeerId("bob", "127.0.0.1", 9002)
            };

            var result = PeerLookup.FindByNickname(peers, "bob");

            Assert.Single(result);
            Assert.Equal("127.0.0.1:9002", result[0].EndpointKey);
        }

        [Fact]
        public void FindByNickname_ReturnsAll_WhenMultipleMatchesCaseInsensitive()
        {
            var peers = new[]
            {
                new PeerId("Bob", "192.168.2.5", 9002),
                new PeerId("bob", "192.168.3.7", 5000),
                new PeerId("carol", "127.0.0.1", 9003)
            };

            var result = PeerLookup.FindByNickname(peers, "BOB");

            Assert.Equal(2, result.Count);
            Assert.Contains(result, p => p.EndpointKey == "192.168.2.5:9002");
            Assert.Contains(result, p => p.EndpointKey == "192.168.3.7:5000");
        }
    }
}
