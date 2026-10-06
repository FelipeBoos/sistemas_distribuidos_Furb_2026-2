using Ingressos.Messaging;
using Ingressos.Notificador;
using Ingressos.Persistence;
using Microsoft.EntityFrameworkCore;

DotEnv.Carregar();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContextFactory<IngressosDbContext>(options =>
    options.UseNpgsql(PostgresOptions.ConnectionStringFromEnvironment()));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
