namespace Ingressos.Domain.Entidades;

public class Evento
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public DateTime DataHora { get; set; }
}
