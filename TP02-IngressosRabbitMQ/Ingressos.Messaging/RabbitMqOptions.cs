namespace Ingressos.Messaging;

// Le a configuracao de conexao a partir de variaveis de ambiente. Caminho atual: RABBITMQ_URL
// com a URI pronta de um broker gerenciado (ex.: CloudAMQP, amqps://usuario:senha@host/vhost).
// Os campos discretos (HostNames/Port/VirtualHost/UserName/Password) continuam existindo para o
// caminho via Docker Compose (cluster de 3 nos auto-hospedado), descartado mas mantido no repo
// como referencia - ver docs/guia-apresentacao.md.
public class RabbitMqOptions
{
    public Uri? Uri { get; init; }

    public string[] HostNames { get; init; } = ["localhost"];
    public int Port { get; init; } = 5672;
    public string VirtualHost { get; init; } = "/ingressos";
    public string UserName { get; init; } = "ingressos";
    public string Password { get; init; } = "ingressos";

    public static RabbitMqOptions FromEnvironment()
    {
        var url = Environment.GetEnvironmentVariable("RABBITMQ_URL");
        if (!string.IsNullOrWhiteSpace(url))
        {
            return new RabbitMqOptions { Uri = new Uri(url) };
        }

        var hosts = Environment.GetEnvironmentVariable("RABBITMQ_HOSTS") ?? "localhost";

        return new RabbitMqOptions
        {
            HostNames = hosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Port = int.TryParse(Environment.GetEnvironmentVariable("RABBITMQ_PORT"), out var port) ? port : 5672,
            VirtualHost = Environment.GetEnvironmentVariable("RABBITMQ_VHOST") ?? "/ingressos",
            UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "ingressos",
            Password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "ingressos"
        };
    }
}
