using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ingressos.Persistence;

// Usado pelas ferramentas de design-time do EF (dotnet ef migrations add/update) para criar o
// DbContext sem precisar subir um host completo.
public class IngressosDbContextFactory : IDesignTimeDbContextFactory<IngressosDbContext>
{
    public IngressosDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IngressosDbContext>()
            .UseNpgsql(PostgresOptions.ConnectionStringFromEnvironment())
            .Options;

        return new IngressosDbContext(options);
    }
}
