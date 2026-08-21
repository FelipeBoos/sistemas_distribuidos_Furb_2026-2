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
}
