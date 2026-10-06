using Ingressos.Domain.Entidades;

namespace Ingressos.Tests;

public class EntidadesTests
{
    [Fact]
    public void Reserva_DeveIniciarComStatusSolicitada()
    {
        var reserva = new Reserva();

        Assert.Equal(StatusReserva.Solicitada, reserva.Status);
    }

    [Fact]
    public void Assento_DeveIniciarComoDisponivel()
    {
        var assento = new Assento();

        Assert.Equal(StatusAssento.Disponivel, assento.Status);
    }
}
