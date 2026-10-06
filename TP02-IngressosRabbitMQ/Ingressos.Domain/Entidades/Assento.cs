namespace Ingressos.Domain.Entidades;

public enum StatusAssento
{
    Disponivel,
    Reservado,
    Vendido
}

public class Assento
{
    public Guid Id { get; set; }
    public Guid SetorId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public StatusAssento Status { get; set; } = StatusAssento.Disponivel;
}
