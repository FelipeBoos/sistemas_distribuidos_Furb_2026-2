using Ingressos.Domain.Regras;

namespace Ingressos.Tests;

public class RegrasCompraTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(5)]
    [InlineData(1000)]
    public void QuantidadeValida_DeveRejeitarForaDoIntervalo(int quantidade)
    {
        Assert.False(RegrasCompra.QuantidadeValida(quantidade));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void QuantidadeValida_DeveAceitarDeUmAoLimite(int quantidade)
    {
        Assert.True(RegrasCompra.QuantidadeValida(quantidade));
    }

    [Fact]
    public void CalcularValor_SemMeiaEntrada_DeveUsarPrecoIntegral()
    {
        Assert.Equal(100m, RegrasCompra.CalcularValor(100m, meiaEntrada: false));
    }

    [Fact]
    public void CalcularValor_ComMeiaEntrada_DeveCobrarMetade()
    {
        Assert.Equal(50m, RegrasCompra.CalcularValor(100m, meiaEntrada: true));
    }
}
