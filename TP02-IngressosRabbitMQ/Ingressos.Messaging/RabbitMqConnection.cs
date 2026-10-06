using RabbitMQ.Client;

namespace Ingressos.Messaging;

// Uma unica conexao AMQP por processo (cada servico abre canais a partir dela). Conecta na lista
// de nos do cluster (RABBITMQ_HOSTS) para que o cliente faca failover automatico se o no que
// respondeu primeiro cair (ver Topico 2.3.3 - Tolerancia a Falhas).
public sealed class RabbitMqConnection : IAsyncDisposable
{
    private readonly IConnection _connection;

    private RabbitMqConnection(IConnection connection)
    {
        _connection = connection;
    }

    public static async Task<RabbitMqConnection> ConectarAsync(RabbitMqOptions options, string nomeAplicacao)
    {
        if (options.Uri is not null)
        {
            // Caminho atual (CloudAMQP): URI unica com credenciais e TLS (esquema "amqps").
            // RabbitMQ.Client habilita SSL automaticamente a partir do esquema da URI.
            var factoryUri = new ConnectionFactory
            {
                Uri = options.Uri,
                ClientProvidedName = nomeAplicacao
            };

            var conexaoUnica = await factoryUri.CreateConnectionAsync();
            return new RabbitMqConnection(conexaoUnica);
        }

        // Caminho descartado (Docker Compose, cluster de 3 nos auto-hospedado): lista de
        // enderecos para o cliente fazer failover automatico se o no que respondeu primeiro cair.
        var factory = new ConnectionFactory
        {
            UserName = options.UserName,
            Password = options.Password,
            VirtualHost = options.VirtualHost,
            ClientProvidedName = nomeAplicacao
        };

        var endpoints = options.HostNames
            .Select(host => new AmqpTcpEndpoint(host, options.Port))
            .ToList();

        var connection = await factory.CreateConnectionAsync(endpoints);
        return new RabbitMqConnection(connection);
    }

    // Publisher confirms habilitados por canal: o produtor so considera a mensagem publicada
    // apos a confirmacao de persistencia do broker (Topico 2.3.2 - Confiabilidade).
    public Task<IChannel> AbrirCanalAsync() =>
        _connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));

    public async ValueTask DisposeAsync()
    {
        await _connection.CloseAsync();
        _connection.Dispose();
    }
}
