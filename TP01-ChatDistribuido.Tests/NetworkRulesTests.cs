using ChatDistribuido;

namespace ChatDistribuido.Tests
{
    public class NetworkRulesTests
    {
        [Fact]
        public void IsSameSubnet_TrueForSameFirstThreeOctets()
        {
            Assert.True(NetworkRules.IsSameSubnet("192.168.2.10", "192.168.2.20"));
        }

        [Fact]
        public void IsSameSubnet_FalseForDifferentThirdOctet()
        {
            Assert.False(NetworkRules.IsSameSubnet("192.168.2.10", "192.168.3.10"));
        }

        [Fact]
        public void ResolveDialPort_UsesConfiguredPort_WhenSameSubnet()
        {
            var port = NetworkRules.ResolveDialPort("192.168.2.5", "192.168.2.10", configuredPort: 9002);

            Assert.Equal(9002, port);
        }

        [Fact]
        public void ResolveDialPort_ForcesPort5000_WhenDifferentSubnet_EvenIfPortWasConfigured()
        {
            var port = NetworkRules.ResolveDialPort("192.168.2.5", "192.168.3.10", configuredPort: 9002);

            Assert.Equal(NetworkRules.CrossNetworkPort, port);
            Assert.Equal(5000, port);
        }

        [Fact]
        public void SubnetPrefixOf_ExtractsFirstThreeOctets()
        {
            Assert.Equal("127.0.1.", NetworkRules.SubnetPrefixOf("127.0.1.11"));
            Assert.Equal("192.168.2.", NetworkRules.SubnetPrefixOf("192.168.2.5"));
        }

        [Fact]
        public void SubnetPrefixOf_ReturnsNull_ForInvalidHost()
        {
            Assert.Null(NetworkRules.SubnetPrefixOf("nao-e-um-ip"));
        }
    }
}
