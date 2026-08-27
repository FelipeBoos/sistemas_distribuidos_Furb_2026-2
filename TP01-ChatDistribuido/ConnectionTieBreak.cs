namespace ChatDistribuido
{
    /// <summary>
    /// Regra de desempate para conexões duplicadas: quando dois peers discam um para o
    /// outro simultaneamente, ambos os lados precisam chegar à mesma decisão sobre qual
    /// das duas conexões físicas sobrevive, sem nenhuma coordenação além do que cada lado
    /// já sabe (seu próprio endpoint de escuta e o do peer remoto).
    /// </summary>
    public static class ConnectionTieBreak
    {
        /// <summary>
        /// Decide se a conexão recém-estabelecida deve substituir uma conexão já existente
        /// para o mesmo peer lógico. Regra simétrica: o lado de menor <paramref name="selfEndpointKey"/>
        /// (comparação ordinal) mantém a conexão que ACEITOU (inbound); o lado de maior
        /// mantém a que DISCOU (outbound). Aplicada independentemente pelos dois lados,
        /// produz sempre a mesma decisão sobre qual conexão sobrevive.
        /// </summary>
        public static bool ShouldKeepNewConnection(string selfEndpointKey, string remoteEndpointKey, bool isOutbound)
        {
            var selfIsLower = string.CompareOrdinal(selfEndpointKey, remoteEndpointKey) < 0;
            return isOutbound ? !selfIsLower : selfIsLower;
        }
    }

    /// <summary>
    /// Regras puras de rede usadas para discagem: se um host está na mesma sub-rede /24
    /// que este nó, e qual porta usar para alcançá-lo. Extraídas como funções estáticas
    /// para serem testáveis sem sockets, no mesmo espírito de <see cref="ConnectionTieBreak"/>.
    /// </summary>
    public static class NetworkRules
    {
        public const int CrossNetworkPort = 5000;

        /// <summary>Compara os 3 primeiros octetos de dois endereços IPv4 (formato "a.b.c.d").</summary>
        public static bool IsSameSubnet(string hostA, string hostB)
        {
            var a = hostA.Split('.');
            var b = hostB.Split('.');
            if (a.Length != 4 || b.Length != 4)
                return false;

            return a[0] == b[0] && a[1] == b[1] && a[2] == b[2];
        }

        /// <summary>
        /// Porta efetiva a usar ao discar para <paramref name="peerHost"/>: a porta configurada,
        /// se ele estiver na mesma sub-rede de <paramref name="selfHost"/>; caso contrário, a porta
        /// fixa de rede externa (<see cref="CrossNetworkPort"/>), ignorando a porta informada.
        /// </summary>
        public static int ResolveDialPort(string selfHost, string peerHost, int configuredPort, int crossNetworkPort = CrossNetworkPort) =>
            IsSameSubnet(selfHost, peerHost) ? configuredPort : crossNetworkPort;

        /// <summary>Prefixo "a.b.c." (com o ponto final) de um endereço IPv4 "a.b.c.d", usado para
        /// varrer a própria sub-rede quando nenhum --peers é informado. Deriva do próprio --host
        /// configurado em vez de detectar a interface de rede do SO, para funcionar tanto com IP
        /// real quanto com aliases de loopback usados em testes locais (127.0.1.x, 127.0.2.x...).</summary>
        public static string? SubnetPrefixOf(string host)
        {
            var octets = host.Split('.');
            return octets.Length == 4 ? $"{octets[0]}.{octets[1]}.{octets[2]}." : null;
        }
    }
}
