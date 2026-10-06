using Ingressos.Admissor;

Ingressos.Messaging.DotEnv.Carregar();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
