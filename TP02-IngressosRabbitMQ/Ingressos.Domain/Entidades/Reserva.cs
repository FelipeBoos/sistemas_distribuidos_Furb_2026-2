namespace Ingressos.Domain.Entidades;

public enum StatusReserva
{
    Solicitada,
    Reservada,
    AguardandoPagamento,
    Paga,
    Expirada,
    Recusada,
    Rejeitada
}

public class Reserva
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid AssentoId { get; set; }
    public DateTime CriadaEm { get; set; }
    public DateTime ExpiraEm { get; set; }
    public StatusReserva Status { get; set; } = StatusReserva.Solicitada;
}
