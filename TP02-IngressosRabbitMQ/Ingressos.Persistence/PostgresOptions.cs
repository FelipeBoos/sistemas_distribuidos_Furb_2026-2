using Microsoft.EntityFrameworkCore;

namespace Ingressos.Persistence;

// Monta a connection string a partir de variaveis de ambiente. Caminho atual: POSTGRES_CONNECTION_STRING
// com a string pronta de um banco gerenciado (ex.: Neon, ja com sslmode=require). Os campos
// discretos (POSTGRES_HOST/PORT/DB/USER/PASSWORD) continuam existindo para o caminho via Docker
// Compose, descartado mas mantido no repo como referencia - ver docs/guia-apresentacao.md.
public static class PostgresOptions
{
    public static string ConnectionStringFromEnvironment()
    {
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
        var database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "ingressos";
        var user = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "ingressos";
        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "ingressos";

        return $"Host={host};Port={port};Database={database};Username={user};Password={password}";
    }

    public static DbContextOptions<IngressosDbContext> BuildDbContextOptions() =>
        new DbContextOptionsBuilder<IngressosDbContext>()
            .UseNpgsql(ConnectionStringFromEnvironment())
            .Options;
}
