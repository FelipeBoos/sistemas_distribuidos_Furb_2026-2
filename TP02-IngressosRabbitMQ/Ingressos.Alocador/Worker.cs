using System.Text.Json;
using Ingressos.Contracts.Comandos;
using Ingressos.Contracts.Eventos;
using Ingressos.Domain.Entidades;
using Ingressos.Messaging;
using Ingressos.Persistence;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace Ingressos.Alocador;

// Reserva o assento sem overbooking (Topico 2.1). 1 instancia logica por setor (fila dedicada,
// consumida com Single Active Consumer quando ALOCADOR_SINGLE_ACTIVE_CONSUMER != "false").
// Valida o limite de ingressos por CPF e a cota de meia-entrada (Lei 12.933/2013) antes de
// gravar a reserva e o evento de outbox na mesma transacao.
public class Worker(ILogger<Worker> logger, IDbContextFactory<IngressosDbContext> dbFactory) : ConsumidorBase(logger)
{
    // Cota legal minima de meia-entrada por setor (simplificacao didatica da Lei 12.933/2013).
    private const double CotaMeiaEntrada = 0.4;

    private static readonly string Setor = Environment.GetEnvironmentVariable("ALOCADOR_SETOR") ?? "pista";

    // Lista (em vez de um metodo auxiliar) porque o EF Core precisa traduzir a comparacao para
    // SQL (IN) - uma chamada de metodo C# arbitraria dentro da query LINQ nao e traduzivel.
    private static readonly StatusReserva[] StatusesAtivos =
    [
        StatusReserva.Solicitada, StatusReserva.Reservada, StatusReserva.AguardandoPagamento, StatusReserva.Paga
    ];

    protected override string Fila => NomesTopologia.FilaAlocacao(Setor);
    protected override ushort Prefetch => 1;

    protected override async Task ProcessarAsync(ReadOnlyMemory<byte> corpo, IReadOnlyBasicProperties propriedades, CancellationToken ct)
    {
        var comando = MensagemPublisher.Desserializar<CompraSolicitadaCommand>(corpo);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var setorEntidade = await db.Setores.FirstOrDefaultAsync(s => s.Id == comando.SetorId, ct);
        if (setorEntidade is null)
        {
            await PublicarRejeicaoAsync(comando.UsuarioId, "setor_inexistente");
            return;
        }

        var usuario = await ObterOuCriarUsuarioAsync(db, comando.UsuarioId, ct);

        var reservasAtivas = await db.Reservas.CountAsync(r => r.UsuarioId == usuario.Id && StatusesAtivos.Contains(r.Status), ct);
        if (reservasAtivas + comando.QuantidadeIngressos > usuario.LimiteIngressos)
        {
            await PublicarRejeicaoAsync(comando.UsuarioId, "limite_cpf_excedido");
            return;
        }

        if (comando.MeiaEntrada && !await CotaMeiaEntradaDisponivelAsync(db, setorEntidade, ct))
        {
            await PublicarRejeicaoAsync(comando.UsuarioId, "cota_meia_entrada_excedida");
            return;
        }

        if (!await RiscoAceitavelAsync(usuario.Id, ct))
        {
            await PublicarRejeicaoAsync(comando.UsuarioId, "risco_antifraude_alto");
            return;
        }

        var assento = await db.Assentos.FirstOrDefaultAsync(a => a.SetorId == comando.SetorId && a.Status == StatusAssento.Disponivel, ct);
        if (assento is null)
        {
            if (Publisher is not null)
            {
                await Publisher.PublicarAsync("assento.indisponivel", new AssentoIndisponivel(comando.UsuarioId, comando.SetorId, "sem_assentos_disponiveis"));
            }

            return;
        }

        assento.Status = StatusAssento.Reservado;

        var reserva = new Reserva
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            AssentoId = assento.Id,
            CriadaEm = DateTime.UtcNow,
            ExpiraEm = DateTime.UtcNow.AddMinutes(10),
            // O diagrama de estados (Figura 5) separa "Reservada" de "AguardandoPagamento", mas
            // nesta implementacao o assento ja fica aguardando pagamento assim que alocado - nao
            // ha evento distinto entre as duas etapas (ver docs/guia-apresentacao.md).
            Status = StatusReserva.AguardandoPagamento,
            MeiaEntrada = comando.MeiaEntrada
        };
        db.Reservas.Add(reserva);

        var reservaCriada = new ReservaCriada(reserva.Id, reserva.UsuarioId, reserva.AssentoId, reserva.ExpiraEm);
        db.OutboxMessages.Add(new OutboxMessage
        {
            RoutingKey = "reserva.criada",
            TipoMensagem = nameof(ReservaCriada),
            PayloadJson = JsonSerializer.Serialize(reservaCriada, JsonSerializacao.Opcoes)
        });

        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Reserva {ReservaId} criada para assento {AssentoId} no setor {Setor}", reserva.Id, assento.Id, Setor);
        }
        catch (DbUpdateException)
        {
            // Indice unico de Reserva.AssentoId violado: outro processo reservou o mesmo assento
            // primeiro (defesa em profundidade citada no Topico 2.3.3, mesmo com SAC habilitado).
            await PublicarRejeicaoAsync(comando.UsuarioId, "assento_concorrencia_detectada");
        }
    }

    private static async Task<Usuario> ObterOuCriarUsuarioAsync(IngressosDbContext db, Guid usuarioId, CancellationToken ct)
    {
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
        if (usuario is not null)
        {
            return usuario;
        }

        // Nao ha fluxo de cadastro nesta demonstracao: o usuario e provisionado na primeira
        // compra, com um limite padrao (ver docs/guia-apresentacao.md - divergencias).
        usuario = new Usuario
        {
            Id = usuarioId,
            Cpf = usuarioId.ToString("N")[..11],
            Email = $"{usuarioId}@demo.ingressos.local",
            LimiteIngressos = 4
        };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync(ct);
        return usuario;
    }

    private static async Task<bool> CotaMeiaEntradaDisponivelAsync(IngressosDbContext db, Setor setor, CancellationToken ct)
    {
        var cotaMaxima = (int)(setor.Capacidade * CotaMeiaEntrada);
        var meiaEntradaAtivas = await (
            from r in db.Reservas
            join a in db.Assentos on r.AssentoId equals a.Id
            where a.SetorId == setor.Id && r.MeiaEntrada && StatusesAtivos.Contains(r.Status)
            select r.Id).CountAsync(ct);

        return meiaEntradaAtivas < cotaMaxima;
    }

    // Conexao AMQP dedicada para o RPC do Antifraude - precisa ser uma conexao (TCP) totalmente
    // separada da usada para consumir alocacao.<setor>, nao apenas um canal diferente na mesma
    // conexao: o loop de leitura de frames e compartilhado por todos os canais de uma conexao, e
    // fica bloqueado enquanto o handler da mensagem atual nao retorna - inclusive para abrir um
    // canal novo na mesma conexao. Prefetch=1 garante que ProcessarAsync nunca roda
    // concorrentemente, entao essa inicializacao preguicosa sem lock e segura.
    private RabbitMqConnection? _conexaoRpc;
    private IChannel? _canalRpc;

    private async Task<bool> RiscoAceitavelAsync(Guid usuarioId, CancellationToken ct)
    {
        try
        {
            if (_canalRpc is null)
            {
                _conexaoRpc = await RabbitMqConnection.ConectarAsync(RabbitMqOptions.FromEnvironment(), "Ingressos.Alocador.Rpc");
                _canalRpc = await _conexaoRpc.AbrirCanalAsync();
            }

            var rpc = new RpcCliente(_canalRpc);
            var resposta = await rpc.ChamarAsync<AntifraudeSolicitadoCommand, AntifraudeResposta>(
                "antifraude.solicitado",
                new AntifraudeSolicitadoCommand(usuarioId, Guid.Empty, 0m),
                TimeSpan.FromSeconds(5));
            return resposta.RiscoAceitavel;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Timeout na consulta ao antifraude para o usuario {UsuarioId}; assumindo risco aceitavel", usuarioId);
            return true;
        }
    }

    private async Task PublicarRejeicaoAsync(Guid usuarioId, string motivo)
    {
        if (Publisher is not null)
        {
            await Publisher.PublicarAsync($"compra.rejeitada.{Setor}", new CompraRejeitada(usuarioId, motivo));
        }
    }
}
