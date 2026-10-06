namespace Ingressos.Domain.Entidades;

public class Usuario
{
    public Guid Id { get; set; }
    public string Cpf { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int LimiteIngressos { get; set; }
}
