using System.Text.Json;
using Ingressos.Contracts.Comandos;
using Ingressos.Contracts.Eventos;
using Ingressos.Domain.Entidades;
using Ingressos.Domain.Regras;
using Ingressos.Messaging;
using Ingressos.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RabbitMQ.Client;

namespace Ingressos.Alocador;

// Reserva os assentos sem overbooking (Topico 2.1). 1 instancia logica por setor (fila dedicada,
// consumida com Single Active Consumer quando ALOCADOR_SINGLE_ACTIVE_CONSUMER != "false").
// Cada ingresso da compra vira uma reserva. Limite por CPF e cota de meia-entrada (Lei 12.933/2013)
// sao validados dentro de uma transacao que trava a linha do usuario, para que compras do mesmo
// CPF em setores diferentes (filas diferentes) nao leiam a contagem ao mesmo tempo.
public class Worker(ILogger<Worker> logger, IDbContextFactory<IngressosDbContext> dbFactory) : ConsumidorBase(logger)
{
    // Cota legal minima de meia-entrada por setor (simplificacao didatica da Lei 12.933/2013).
    private const double CotaMeiaEntrada = 0.4;

    private const string SqlStateViolacaoUnica = "23505";
    private const string IndiceAssentoUnico = "IX_Reservas_AssentoId";

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
        if (!RegrasCompra.QuantidadeValida(comando.QuantidadeIngressos))
        {
            await PublicarRejeicaoAsync(comando.UsuarioId, "quantidade_invalida");
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var setorEntidade = await db.Setores.FirstOrDefaultAsync(s => s.Id == comando.SetorId, ct);
        if (setorEntidade is null)
        {
            await PublicarRejeicaoAsync(comando.UsuarioId, "setor_inexistente");
            return;
        }

        var usuario = await ObterOuCriarUsuarioAsync(db, comando.UsuarioId, ct);

        // Antifraude fica fora da transacao: a consulta RPC leva ate 5 s e nao deve segurar o lock
        // da linha do usuario durante esse tempo.
        var motivoRejeicaoRisco = await AvaliarRiscoAsync(usuario.Id, ct);
        if (motivoRejeicaoRisco is not null)
        {
            await PublicarRejeicaoAsync(comando.UsuarioId, motivoRejeicaoRisco);
            return;
        }

        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        // Trava a linha do usuario: serializa compras do mesmo CPF entre setores. Sem isso, duas
        // compras em filas diferentes poderiam ler a mesma contagem e ultrapassar o limite.
        await db.Usuarios
            .FromSqlInterpolated($"SELECT * FROM \"Usuarios\" WHERE \"Id\" = {usuario.Id} FOR UPDATE")
            .AsNoTracking()
            .FirstAsync(ct);

        var quantidade = comando.QuantidadeIngressos;

        var reservasAtivas = await db.Reservas.CountAsync(r => r.UsuarioId == usuario.Id && StatusesAtivos.Contains(r.Status), ct);
        if (reservasAtivas + quantidade > usuario.LimiteIngressos)
        {
            await PublicarRejeicaoAsync(comando.UsuarioId, "limite_cpf_excedido");
            return;
        }

        if (comando.MeiaEntrada && !await CotaMeiaEntradaDisponivelAsync(db, setorEntidade, quantidade, ct))
        {
            await PublicarRejeicaoAsync(comando.UsuarioId, "cota_meia_entrada_excedida");
            return;
        }

        // Tudo ou nada: se o setor nao tem assentos para a quantidade toda, nenhuma reserva e criada.
        var assentos = await db.Assentos
            .Where(a => a.SetorId == setorEntidade.Id && a.Status == StatusAssento.Disponivel)
            .OrderBy(a => a.Codigo)
            .Take(quantidade)
            .ToListAsync(ct);

        if (assentos.Count < quantidade)
        {
            if (Publisher is not null)
            {
                await Publisher.PublicarAsync("assento.indisponivel", new AssentoIndisponivel(comando.UsuarioId, comando.SetorId, "sem_assentos_disponiveis"));
            }

            return;
        }

        foreach (var assento in assentos)
        {
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
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            logger.LogInformation("{Quantidade} reserva(s) criada(s) para o usuario {UsuarioId} no setor {Setor}", quantidade, usuario.Id, Setor);
        }
        catch (DbUpdateException ex) when (EhViolacaoUnica(ex, IndiceAssentoUnico))
        {
            // Indice unico de Reserva.AssentoId violado: outro processo reservou o mesmo assento
            // primeiro (defesa em profundidade citada no Topico 2.3.3, mesmo com SAC habilitado).
            // Qualquer outro erro de banco segue como excecao e vai para a DLQ.
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
            LimiteIngressos = RegrasCompra.QuantidadeMaximaPorCompra
        };
        db.Usuarios.Add(usuario);

        try
        {
            await db.SaveChangesAsync(ct);
            return usuario;
        }
        catch (DbUpdateException ex) when (EhViolacaoUnica(ex))
        {
            // Outra compra do mesmo usuario (em outro setor) criou o registro ao mesmo tempo:
            // descarta a entidade local e relê o registro que ja existe.
            db.Entry(usuario).State = EntityState.Detached;
            return await db.Usuarios.FirstAsync(u => u.Id == usuarioId, ct);
        }
    }

    private static async Task<bool> CotaMeiaEntradaDisponivelAsync(IngressosDbContext db, Setor setor, int quantidade, CancellationToken ct)
    {
        var cotaMaxima = (int)(setor.Capacidade * CotaMeiaEntrada);
        var meiaEntradaAtivas = await (
            from r in db.Reservas
            join a in db.Assentos on r.AssentoId equals a.Id
            where a.SetorId == setor.Id && r.MeiaEntrada && StatusesAtivos.Contains(r.Status)
            select r.Id).CountAsync(ct);

        return meiaEntradaAtivas + quantidade <= cotaMaxima;
    }

    // Conexao AMQP dedicada para o RPC do Antifraude - precisa ser uma conexao (TCP) totalmente
    // separada da usada para consumir alocacao.<setor>, nao apenas um canal diferente na mesma
    // conexao: o loop de leitura de frames e compartilhado por todos os canais de uma conexao, e
    // fica bloqueado enquanto o handler da mensagem atual nao retorna - inclusive para abrir um
    // canal novo na mesma conexao. Prefetch=1 garante que ProcessarAsync nunca roda
    // concorrentemente, entao essa inicializacao preguicosa sem lock e segura.
    private RabbitMqConnection? _conexaoRpc;
    private IChannel? _canalRpc;

    // Fail-closed: se o antifraude nao responder (timeout ou falha de infraestrutura), a compra e
    // rejeitada. Retorna null quando o risco e aceitavel, ou o motivo da rejeicao.
    private async Task<string?> AvaliarRiscoAsync(Guid usuarioId, CancellationToken ct)
    {
        try
        {
            if (_canalRpc is null)
            {
                var conexao = await RabbitMqConnection.ConectarAsync(RabbitMqOptions.FromEnvironment(), "Ingressos.Alocador.Rpc");
                try
                {
                    _canalRpc = await conexao.AbrirCanalAsync();
                    _conexaoRpc = conexao;
                }
                catch
                {
                    await conexao.DisposeAsync();
                    throw;
                }
            }

            var rpc = new RpcCliente(_canalRpc);
            var resposta = await rpc.ChamarAsync<AntifraudeSolicitadoCommand, AntifraudeResposta>(
                "antifraude.solicitado",
                new AntifraudeSolicitadoCommand(usuarioId, Guid.Empty, 0m),
                TimeSpan.FromSeconds(5));
            return resposta.RiscoAceitavel ? null : "risco_antifraude_alto";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Timeout na consulta ao antifraude para o usuario {UsuarioId}; compra rejeitada (fail-closed)", usuarioId);
            return "antifraude_indisponivel";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha na consulta ao antifraude para o usuario {UsuarioId}; compra rejeitada (fail-closed)", usuarioId);
            return "antifraude_indisponivel";
        }
    }

    private async Task PublicarRejeicaoAsync(Guid usuarioId, string motivo)
    {
        if (Publisher is not null)
        {
            await Publisher.PublicarAsync($"compra.rejeitada.{Setor}", new CompraRejeitada(usuarioId, motivo));
        }
    }

    // Verifica a violacao de unicidade pelo SQLSTATE 23505 (e, se informado, pelo nome do indice).
    private static bool EhViolacaoUnica(DbUpdateException ex, string? indice = null) =>
        ex.InnerException is PostgresException { SqlState: SqlStateViolacaoUnica } postgres
        && (indice is null || postgres.ConstraintName == indice);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (_conexaoRpc is not null)
        {
            await _conexaoRpc.DisposeAsync();
        }
    }
}
