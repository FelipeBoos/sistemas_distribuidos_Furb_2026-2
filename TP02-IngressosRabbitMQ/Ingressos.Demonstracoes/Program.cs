using Ingressos.Contracts.Comandos;
using Ingressos.Domain.Entidades;
using Ingressos.Messaging;
using Ingressos.Persistence;
using Microsoft.EntityFrameworkCore;

// Gerador de carga para a demonstracao do Single Active Consumer (Topico 4): semeia um setor com
// poucos assentos disponiveis e dispara N solicitacoes de compra concorrentes para o mesmo setor,
// depois verifica se algum assento acabou com mais de uma reserva ativa (overbooking). Rode duas
// vezes - com ALOCADOR_SINGLE_ACTIVE_CONSUMER=true e =false no docker-compose - para comparar.
//
// Uso: dotnet run --project Ingressos.Demonstracoes -- <setor> <quantidadeAssentos> <quantidadeSolicitacoes>
// Exemplo: dotnet run --project Ingressos.Demonstracoes -- pista 2 20

DotEnv.Carregar();

var setorNome = args.ElementAtOrDefault(0) ?? "pista";
var quantidadeAssentos = int.TryParse(args.ElementAtOrDefault(1), out var qa) ? qa : 2;
var quantidadeSolicitacoes = int.TryParse(args.ElementAtOrDefault(2), out var qs) ? qs : 20;

await using var db = new IngressosDbContext(PostgresOptions.BuildDbContextOptions());

Console.WriteLine($"Semeando evento de demonstracao: setor '{setorNome}', {quantidadeAssentos} assento(s) disponivel(is)...");
var (eventoId, setorId) = await SemearAsync(db, setorNome, quantidadeAssentos);
Console.WriteLine($"EventoId={eventoId} SetorId={setorId}");

await using var conexao = await RabbitMqConnection.ConectarAsync(RabbitMqOptions.FromEnvironment(), "Ingressos.Demonstracoes");
var canal = await conexao.AbrirCanalAsync();
await Topologia.DeclararAsync(canal);
var publisher = new MensagemPublisher(canal);

Console.WriteLine($"Disparando {quantidadeSolicitacoes} solicitacoes concorrentes de compra.solicitada.{setorNome}...");
var routingKey = NomesTopologia.RoutingKeyCompraSolicitada(setorNome);

var tarefas = Enumerable.Range(0, quantidadeSolicitacoes).Select(_ =>
{
    var comando = new CompraSolicitadaCommand(Guid.NewGuid(), setorId, QuantidadeIngressos: 1, MeiaEntrada: false);
    return publisher.PublicarAsync(routingKey, comando);
});
await Task.WhenAll(tarefas);

Console.WriteLine("Solicitacoes enviadas. Aguardando processamento pelo(s) Alocador(es)...");
await Task.Delay(TimeSpan.FromSeconds(10));

await VerificarOverbookingAsync(eventoId);

static async Task<(Guid EventoId, Guid SetorId)> SemearAsync(IngressosDbContext db, string setorNome, int quantidadeAssentos)
{
    var evento = new Evento { Id = Guid.NewGuid(), Nome = $"Demonstracao SAC ({DateTime.UtcNow:O})", DataHora = DateTime.UtcNow.AddDays(30) };
    var setor = new Setor { Id = Guid.NewGuid(), EventoId = evento.Id, Nome = setorNome, Capacidade = quantidadeAssentos, Preco = 100m };

    db.Eventos.Add(evento);
    db.Setores.Add(setor);
    for (var i = 0; i < quantidadeAssentos; i++)
    {
        db.Assentos.Add(new Assento { Id = Guid.NewGuid(), SetorId = setor.Id, Codigo = $"{setorNome}-{i + 1}", Status = StatusAssento.Disponivel });
    }

    await db.SaveChangesAsync();
    return (evento.Id, setor.Id);
}

async Task VerificarOverbookingAsync(Guid eventoIdVerificar)
{
    await using var dbVerificacao = new IngressosDbContext(PostgresOptions.BuildDbContextOptions());

    var reservasPorAssento = await (
        from a in dbVerificacao.Assentos
        join s in dbVerificacao.Setores on a.SetorId equals s.Id
        where s.EventoId == eventoIdVerificar
        join r in dbVerificacao.Reservas on a.Id equals r.AssentoId into reservas
        select new { Assento = a.Codigo, TotalReservas = reservas.Count() }
    ).ToListAsync();

    Console.WriteLine();
    Console.WriteLine("Resultado:");
    var algumOverbooking = false;
    foreach (var linha in reservasPorAssento)
    {
        var marcador = linha.TotalReservas > 1 ? " <-- OVERBOOKING!" : "";
        if (linha.TotalReservas > 1)
        {
            algumOverbooking = true;
        }

        Console.WriteLine($"  Assento {linha.Assento}: {linha.TotalReservas} reserva(s){marcador}");
    }

    Console.WriteLine();
    Console.WriteLine(algumOverbooking
        ? "RESULTADO: overbooking detectado (esperado quando ALOCADOR_SINGLE_ACTIVE_CONSUMER=false)."
        : "RESULTADO: nenhum overbooking (esperado quando ALOCADOR_SINGLE_ACTIVE_CONSUMER=true).");
}
