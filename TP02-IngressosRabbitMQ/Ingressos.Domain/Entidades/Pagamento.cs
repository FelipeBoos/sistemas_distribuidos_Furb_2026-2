namespace Ingressos.Domain.Entidades;

public enum StatusPagamento
{
    Solicitado,
    Aprovado,
    Recusado
}

public class Pagamento
{
    public Guid Id { get; set; }
    public Guid ReservaId { get; set; }
    public decimal Valor { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public StatusPagamento Status { get; set; } = StatusPagamento.Solicitado;
}
