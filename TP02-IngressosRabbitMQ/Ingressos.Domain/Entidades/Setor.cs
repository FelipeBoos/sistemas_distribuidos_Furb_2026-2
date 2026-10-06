namespace Ingressos.Domain.Entidades;

public class Setor
{
    public Guid Id { get; set; }
    public Guid EventoId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public int Capacidade { get; set; }
    public decimal Preco { get; set; }
}
