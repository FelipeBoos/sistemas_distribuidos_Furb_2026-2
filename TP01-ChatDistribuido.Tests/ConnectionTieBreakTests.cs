using ChatDistribuido;

namespace ChatDistribuido.Tests
{
    public class ConnectionTieBreakTests
    {
        [Fact]
        public void LowerEndpoint_KeepsInboundConnection()
        {
            var keepsInbound = ConnectionTieBreak.ShouldKeepNewConnection(
                selfEndpointKey: "127.0.0.1:9001",
                remoteEndpointKey: "127.0.0.1:9002",
                isOutbound: false);

            var keepsOutbound = ConnectionTieBreak.ShouldKeepNewConnection(
                selfEndpointKey: "127.0.0.1:9001",
                remoteEndpointKey: "127.0.0.1:9002",
                isOutbound: true);

            Assert.True(keepsInbound);
            Assert.False(keepsOutbound);
        }

        [Fact]
        public void HigherEndpoint_KeepsOutboundConnection()
        {
            var keepsOutbound = ConnectionTieBreak.ShouldKeepNewConnection(
                selfEndpointKey: "127.0.0.1:9002",
                remoteEndpointKey: "127.0.0.1:9001",
                isOutbound: true);

            var keepsInbound = ConnectionTieBreak.ShouldKeepNewConnection(
                selfEndpointKey: "127.0.0.1:9002",
                remoteEndpointKey: "127.0.0.1:9001",
                isOutbound: false);

            Assert.True(keepsOutbound);
            Assert.False(keepsInbound);
        }

        [Fact]
        public void BothSides_AgreeOnSameSurvivingConnection()
        {
            // Simula A discando para B e B discando para A ao mesmo tempo: as duas conexões
            // físicas resultantes precisam de uma decisão consistente entre os dois lados.
            const string a = "127.0.0.1:9001";
            const string b = "127.0.0.1:9002";

            var aKeepsOutboundToB = ConnectionTieBreak.ShouldKeepNewConnection(a, b, isOutbound: true);
            var bKeepsInboundFromA = ConnectionTieBreak.ShouldKeepNewConnection(b, a, isOutbound: false);
            Assert.Equal(aKeepsOutboundToB, bKeepsInboundFromA);

            var bKeepsOutboundToA = ConnectionTieBreak.ShouldKeepNewConnection(b, a, isOutbound: true);
            var aKeepsInboundFromB = ConnectionTieBreak.ShouldKeepNewConnection(a, b, isOutbound: false);
            Assert.Equal(bKeepsOutboundToA, aKeepsInboundFromB);
        }
    }
}
