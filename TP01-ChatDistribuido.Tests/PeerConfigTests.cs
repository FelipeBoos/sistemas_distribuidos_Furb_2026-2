using ChatDistribuido;

namespace ChatDistribuido.Tests
{
    public class PeerConfigTests
    {
        [Fact]
        public void ParsesArgs_WithPeersList()
        {
            var config = PeerConfig.Parse([
                "--port", "9001", "--name", "alice", "--peers", "127.0.0.1:9002,127.0.0.1:9003"
            ]);

            Assert.Equal(9001, config.Port);
            Assert.Equal("alice", config.Name);
            Assert.Equal("127.0.0.1", config.AdvertiseHost);
            Assert.Equal(2, config.KnownPeers.Count);
            Assert.Contains(config.KnownPeers, p => p.Host == "127.0.0.1" && p.Port == 9002);
            Assert.Contains(config.KnownPeers, p => p.Host == "127.0.0.1" && p.Port == 9003);
        }

        [Fact]
        public void ParsesArgs_WithCustomHost()
        {
            var config = PeerConfig.Parse(["--port", "9001", "--name", "alice", "--host", "192.168.0.5"]);

            Assert.Equal("192.168.0.5", config.AdvertiseHost);
        }

        [Fact]
        public void MissingPort_Throws()
        {
            Assert.Throws<ArgumentException>(() => PeerConfig.Parse(["--name", "alice"]));
        }

        [Fact]
        public void MissingName_Throws()
        {
            Assert.Throws<ArgumentException>(() => PeerConfig.Parse(["--port", "9001"]));
        }

        [Fact]
        public void InvalidPeerFormat_Throws()
        {
            Assert.Throws<ArgumentException>(() => PeerConfig.Parse([
                "--port", "9001", "--name", "alice", "--peers", "host:nao-e-numero"
            ]));
        }

        [Fact]
        public void ParsesArgs_WithSubnetWildcardPeer()
        {
            var config = PeerConfig.Parse([
                "--port", "9001", "--name", "alice", "--peers", "192.168.2.*:9001"
            ]);

            Assert.Single(config.KnownPeers);
            Assert.Equal(("192.168.2.*", 9001), config.KnownPeers[0]);
        }

        [Theory]
        [InlineData("192.168.*:9001")]
        [InlineData("256.1.1.*:9001")]
        [InlineData("abc.168.2.*:9001")]
        public void SubnetWildcard_RejectsInvalidFormat(string peerEntry)
        {
            Assert.Throws<ArgumentException>(() => PeerConfig.Parse([
                "--port", "9001", "--name", "alice", "--peers", peerEntry
            ]));
        }

        [Fact]
        public void ParsesArgs_WithHostOnlyPeer_NoPort()
        {
            var config = PeerConfig.Parse([
                "--port", "9001", "--name", "alice", "--peers", "192.168.3.10"
            ]);

            Assert.Single(config.KnownPeers);
            Assert.Equal("192.168.3.10", config.KnownPeers[0].Host);
        }

        [Fact]
        public void ParsesConfigFile()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, """
                { "port": 9005, "name": "bob", "peers": ["127.0.0.1:9001"] }
                """);

                var config = PeerConfig.Parse(["--config", path]);

                Assert.Equal(9005, config.Port);
                Assert.Equal("bob", config.Name);
                Assert.Single(config.KnownPeers);
                Assert.Equal(("127.0.0.1", 9001), config.KnownPeers[0]);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
