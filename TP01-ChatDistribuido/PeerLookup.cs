namespace ChatDistribuido
{
    /// <summary>
    /// Busca pura de peers por apelido, extraída para ser testável sem depender de
    /// PeerConnection/sockets reais. Usada pelo /msg para decidir se há um único
    /// destinatário ou se é preciso desambiguar entre vários com o mesmo apelido.
    /// </summary>
    public static class PeerLookup
    {
        public static IReadOnlyList<PeerId> FindByNickname(IEnumerable<PeerId> knownPeers, string nickname) =>
            knownPeers
                .Where(p => string.Equals(p.Nickname, nickname, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }
}
