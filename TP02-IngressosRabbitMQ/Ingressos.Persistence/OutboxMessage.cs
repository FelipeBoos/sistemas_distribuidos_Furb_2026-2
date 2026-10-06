namespace Ingressos.Persistence;

// Tabela do padrao Outbox (Topico 2.3.2): gravada na mesma transacao que a entidade de dominio;
// o Ingressos.OutboxRelay le as linhas pendentes e publica no RabbitMQ.
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RoutingKey { get; set; } = string.Empty;
    public string TipoMensagem { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime CriadaEm { get; set; } = DateTime.UtcNow;
    public DateTime? PublicadaEm { get; set; }
}
