using Ingressos.Messaging;
using Ingressos.Persistence;
using Microsoft.EntityFrameworkCore;

// Aplica as migrations do EF Core e encerra. Roda uma vez, antes de subir os 11 servicos de
// aplicacao (veja README para a ordem de execucao via Visual Studio ou "dotnet run").
DotEnv.Carregar();

var tentativas = 0;
const int maxTentativas = 10;

while (true)
{
    try
    {
        await using var contexto = new IngressosDbContext(PostgresOptions.BuildDbContextOptions());
        Console.WriteLine("Aplicando migrations...");
        await contexto.Database.MigrateAsync();
        Console.WriteLine("Migrations aplicadas com sucesso.");
        return 0;
    }
    catch (Exception ex) when (tentativas < maxTentativas)
    {
        tentativas++;
        Console.WriteLine($"Postgres ainda nao disponivel (tentativa {tentativas}/{maxTentativas}): {ex.Message}");
        await Task.Delay(TimeSpan.FromSeconds(3));
    }
}
