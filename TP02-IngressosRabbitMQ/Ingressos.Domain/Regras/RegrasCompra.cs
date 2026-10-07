namespace Ingressos.Domain.Regras;

// Regras puras da compra, sem dependencia de banco ou mensageria (testaveis isoladamente).
public static class RegrasCompra
{
    // Teto por compra e limite padrao por CPF (Usuario.LimiteIngressos). Uma compra gera um
    // ingresso por assento, e cada ingresso gera uma reserva.
    public const int QuantidadeMaximaPorCompra = 4;

    public static bool QuantidadeValida(int quantidade) =>
        quantidade >= 1 && quantidade <= QuantidadeMaximaPorCompra;

    // Meia-entrada: 50% do preco do setor (Lei 12.933/2013, simplificada para a demonstracao).
    public static decimal CalcularValor(decimal precoSetor, bool meiaEntrada) =>
        meiaEntrada ? precoSetor / 2m : precoSetor;
}
