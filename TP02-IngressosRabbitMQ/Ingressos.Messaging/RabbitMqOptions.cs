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

        // Sem RABBITMQ_URL nao ha credencial padrao: falha explicita em vez de tentar um usuario
        // default que pode nao existir (ou, pior, existir com outra senha).
        var hosts = Environment.GetEnvironmentVariable("RABBITMQ_HOSTS")
            ?? throw new InvalidOperationException("Defina RABBITMQ_URL no arquivo .env (ver .env.example).");

        return new RabbitMqOptions
        {
            HostNames = hosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Port = int.TryParse(Environment.GetEnvironmentVariable("RABBITMQ_PORT"), out var port) ? port : 5672,
            VirtualHost = Environment.GetEnvironmentVariable("RABBITMQ_VHOST") ?? "/ingressos",
            UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER")
                ?? throw new InvalidOperationException("Defina RABBITMQ_USER no arquivo .env."),
            Password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD")
                ?? throw new InvalidOperationException("Defina RABBITMQ_PASSWORD no arquivo .env.")
        };
    }
}
