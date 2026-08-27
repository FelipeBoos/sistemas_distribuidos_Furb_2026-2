using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatDistribuido
{
    public enum MessageType { Hello, Chat, Private, PeerJoined, Bye, Ping, PeerList }

    public sealed record PeerInfo(string Nickname, string Host, int Port);

    public sealed record WireMessage(
        MessageType Type,
        string From,
        string? To,
        string? Text,
        string? ListenHost,
        int? ListenPort,
        DateTimeOffset Timestamp,
        IReadOnlyList<PeerInfo>? Peers = null)
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public byte[] ToFrame() => JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);

        public static WireMessage FromFrame(byte[] frame) =>
            JsonSerializer.Deserialize<WireMessage>(frame, JsonOptions)
            ?? throw new InvalidDataException("Falha ao decodificar mensagem recebida.");

        public static WireMessage Hello(string nickname, string listenHost, int listenPort) =>
            new(MessageType.Hello, nickname, null, null, listenHost, listenPort, DateTimeOffset.UtcNow);

        public static WireMessage Chat(string from, string text) =>
            new(MessageType.Chat, from, null, text, null, null, DateTimeOffset.UtcNow);

        public static WireMessage Private(string from, string to, string text) =>
            new(MessageType.Private, from, to, text, null, null, DateTimeOffset.UtcNow);

        public static WireMessage PeerJoined(string from, string joinedNickname, string joinedHost, int joinedPort) =>
            new(MessageType.PeerJoined, from, null, joinedNickname, joinedHost, joinedPort, DateTimeOffset.UtcNow);

        public static WireMessage Bye(string from) =>
            new(MessageType.Bye, from, null, null, null, null, DateTimeOffset.UtcNow);

        public static WireMessage Ping(string from) =>
            new(MessageType.Ping, from, null, null, null, null, DateTimeOffset.UtcNow);

        public static WireMessage PeerList(string from, IReadOnlyList<PeerInfo> peers) =>
            new(MessageType.PeerList, from, null, null, null, null, DateTimeOffset.UtcNow, peers);
    }
}
