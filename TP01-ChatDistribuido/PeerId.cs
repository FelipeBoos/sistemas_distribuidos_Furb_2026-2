namespace ChatDistribuido
{
    public sealed record PeerId(string Nickname, string Host, int Port)
    {
        public string EndpointKey => $"{Host}:{Port}";
    }
}
