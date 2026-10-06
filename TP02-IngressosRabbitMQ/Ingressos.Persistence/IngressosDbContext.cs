using Ingressos.Domain.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Ingressos.Persistence;

public class IngressosDbContext(DbContextOptions<IngressosDbContext> options) : DbContext(options)
{
    public DbSet<Evento> Eventos => Set<Evento>();
    public DbSet<Setor> Setores => Set<Setor>();
    public DbSet<Assento> Assentos => Set<Assento>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Reserva> Reservas => Set<Reserva>();
    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();
    public DbSet<Ingresso> Ingressos => Set<Ingresso>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Setor>()
            .HasIndex(s => s.EventoId);

        modelBuilder.Entity<Assento>()
            .HasIndex(a => a.SetorId);

        modelBuilder.Entity<Usuario>()
            .HasIndex(u => u.Cpf)
            .IsUnique();

        // Defesa em profundidade contra overbooking (Topico 2.3.3): um assento so pode ter uma
        // reserva "ativa" por vez (Solicitada/Reservada/AguardandoPagamento/Paga). Indice unico
        // filtrado, complementar ao Single Active Consumer do Alocador.
        // Status e armazenado como inteiro (conversao padrao de enum do EF Core), entao o filtro
        // precisa comparar com os valores numericos do enum, nao com o texto.
        modelBuilder.Entity<Reserva>()
            .HasIndex(r => r.AssentoId)
            .IsUnique()
            .HasFilter(
                $"\"{nameof(Reserva.Status)}\" IN ({(int)StatusReserva.Solicitada}, {(int)StatusReserva.Reservada}, {(int)StatusReserva.AguardandoPagamento}, {(int)StatusReserva.Paga})");

        modelBuilder.Entity<Pagamento>()
            .HasIndex(p => p.IdempotencyKey)
            .IsUnique();

        modelBuilder.Entity<Ingresso>()
            .HasIndex(i => i.PagamentoId)
            .IsUnique();

        modelBuilder.Entity<OutboxMessage>()
            .HasIndex(o => o.PublicadaEm);
    }
}
