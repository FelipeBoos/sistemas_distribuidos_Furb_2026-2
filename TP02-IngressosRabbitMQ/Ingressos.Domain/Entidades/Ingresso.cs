namespace Ingressos.Domain.Entidades;

public class Ingresso
{
    public Guid Id { get; set; }
    public Guid PagamentoId { get; set; }
    public string QrCode { get; set; } = string.Empty;
    public DateTime EmitidoEm { get; set; }
}
