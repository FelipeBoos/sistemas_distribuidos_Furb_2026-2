using Ingressos.Contracts.Eventos;
using Ingressos.Messaging;
using Ingressos.Persistence;
using MailKit.Net.Smtp;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using RabbitMQ.Client;

namespace Ingressos.Notificador;

// Envia e-mails ao cliente reagindo a reserva.*, pagamento.*, ingresso.emitido e
// compra.rejeitada.* (fila "notificacao", com bindings multiplos - Topico 2.2). O tipo da
// mensagem vem no BasicProperties.Type (preenchido pelo MensagemPublisher), evitando reanalisar
// a routing key para saber qual contrato desserializar.
public class Worker(ILogger<Worker> logger, IDbContextFactory<IngressosDbContext> dbFactory) : ConsumidorBase(logger)
{
    protected override string Fila => NomesTopologia.FilaNotificacao;

    // Um e-mail por vez: com o prefetch padrao (10), envios paralelos estouram o limite de taxa do
    // Mailtrap e as mensagens excedentes acabam na DLQ.
    protected override ushort Prefetch => 1;

    private const int TentativasEnvio = 4;

    // Caminho atual (Mailtrap, sandbox SMTP em nuvem): exige STARTTLS + autenticacao. O caminho
    // descartado (Mailpit via Docker) nao exige nenhum dos dois - por isso SMTP_USER/PASSWORD
    // sao opcionais e so autenticam quando preenchidos.
    private static readonly string SmtpHost = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "localhost";
    private static readonly int SmtpPort = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var p) ? p : 1025;
    private static readonly string? SmtpUser = Environment.GetEnvironmentVariable("SMTP_USER");
    private static readonly string? SmtpPassword = Environment.GetEnvironmentVariable("SMTP_PASSWORD");

    protected override async Task ProcessarAsync(ReadOnlyMemory<byte> corpo, IReadOnlyBasicProperties propriedades, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var email = propriedades.Type switch
        {
            nameof(ReservaCriada) => await MontarReservaCriadaAsync(db, corpo, ct),
            nameof(CompraRejeitada) => MontarCompraRejeitada(corpo),
            nameof(PagamentoAprovado) => await MontarPagamentoAprovadoAsync(db, corpo, ct),
            nameof(PagamentoRecusado) => await MontarPagamentoRecusadoAsync(db, corpo, ct),
            nameof(IngressoEmitido) => await MontarIngressoEmitidoAsync(db, corpo, ct),
            nameof(AssentoLiberado) => null,
            _ => null
        };

        if (email is null)
        {
            return;
        }

        await EnviarAsync(email.Value.Destinatario, email.Value.Assunto, email.Value.Corpo, ct);
    }

    private async Task<(string Destinatario, string Assunto, string Corpo)?> MontarReservaCriadaAsync(IngressosDbContext db, ReadOnlyMemory<byte> corpo, CancellationToken ct)
    {
        var evento = MensagemPublisher.Desserializar<ReservaCriada>(corpo);
        var destinatario = await db.Usuarios.Where(u => u.Id == evento.UsuarioId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        return destinatario is null ? null :
            (destinatario, "Reserva criada", $"Sua reserva {evento.ReservaId} foi criada e expira em {evento.ExpiraEm:HH:mm}.");
    }

    private (string Destinatario, string Assunto, string Corpo)? MontarCompraRejeitada(ReadOnlyMemory<byte> corpo)
    {
        var evento = MensagemPublisher.Desserializar<CompraRejeitada>(corpo);
        // CompraRejeitada so carrega UsuarioId (nem sempre ja cadastrado na tabela Usuarios neste
        // ponto do fluxo); a demonstracao registra em log em vez de enviar e-mail.
        logger.LogInformation("Compra rejeitada para usuario {UsuarioId}: {Motivo}", evento.UsuarioId, evento.Motivo);
        return null;
    }

    private async Task<(string Destinatario, string Assunto, string Corpo)?> MontarPagamentoAprovadoAsync(IngressosDbContext db, ReadOnlyMemory<byte> corpo, CancellationToken ct)
    {
        var evento = MensagemPublisher.Desserializar<PagamentoAprovado>(corpo);
        var destinatario = await EmailPorReservaAsync(db, evento.ReservaId, ct);
        return destinatario is null ? null :
            (destinatario, "Pagamento aprovado", $"Seu pagamento de R$ {evento.Valor:F2} foi aprovado.");
    }

    private async Task<(string Destinatario, string Assunto, string Corpo)?> MontarPagamentoRecusadoAsync(IngressosDbContext db, ReadOnlyMemory<byte> corpo, CancellationToken ct)
    {
        var evento = MensagemPublisher.Desserializar<PagamentoRecusado>(corpo);
        var destinatario = await EmailPorReservaAsync(db, evento.ReservaId, ct);
        return destinatario is null ? null :
            (destinatario, "Pagamento recusado", $"Seu pagamento foi recusado (tentativa {evento.TentativaNumero}). Motivo: {evento.Motivo}.");
    }

    private async Task<(string Destinatario, string Assunto, string Corpo)?> MontarIngressoEmitidoAsync(IngressosDbContext db, ReadOnlyMemory<byte> corpo, CancellationToken ct)
    {
        var evento = MensagemPublisher.Desserializar<IngressoEmitido>(corpo);
        var reservaId = await db.Ingressos
            .Where(i => i.Id == evento.IngressoId)
            .Join(db.Pagamentos, i => i.PagamentoId, p => p.Id, (i, p) => p.ReservaId)
            .FirstOrDefaultAsync(ct);

        if (reservaId == Guid.Empty)
        {
            return null;
        }

        var email = await EmailPorReservaAsync(db, reservaId, ct);
        return email is null ? null :
            (email, "Ingresso emitido", $"Seu ingresso foi emitido! QR Code: {evento.QrCode}");
    }

    private static Task<string?> EmailPorReservaAsync(IngressosDbContext db, Guid reservaId, CancellationToken ct) =>
        db.Reservas
            .Where(r => r.Id == reservaId)
            .Join(db.Usuarios, r => r.UsuarioId, u => u.Id, (r, u) => u.Email)
            .FirstOrDefaultAsync(ct);

    private async Task EnviarAsync(string destinatario, string assunto, string corpo, CancellationToken ct)
    {
        var mensagem = new MimeMessage();
        mensagem.From.Add(MailboxAddress.Parse("nao-responda@ingressos.local"));
        mensagem.To.Add(MailboxAddress.Parse(destinatario));
        mensagem.Subject = assunto;
        mensagem.Body = new TextPart("plain") { Text = corpo };

        // Mailtrap (plano gratuito) recusa rajadas ("Too many emails per second"). Tentativas com
        // espera crescente absorvem o limite; so depois de esgotar as tentativas a mensagem vai
        // para a DLQ.
        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                await EnviarUmaVezAsync(mensagem, ct);
                break;
            }
            catch (SmtpCommandException ex) when (tentativa < TentativasEnvio)
            {
                var espera = TimeSpan.FromSeconds(Math.Pow(2, tentativa));
                logger.LogWarning("SMTP recusou o e-mail (tentativa {Tentativa}): {Motivo}; nova tentativa em {Espera}",
                    tentativa, ex.Message, espera);
                await Task.Delay(espera, ct);
            }
        }

        logger.LogInformation("E-mail '{Assunto}' enviado para {Destinatario}", assunto, destinatario);
    }

    private async Task EnviarUmaVezAsync(MimeMessage mensagem, CancellationToken ct)
    {
        var autenticado = !string.IsNullOrEmpty(SmtpUser) && !string.IsNullOrEmpty(SmtpPassword);

        // Host remoto nunca recebe conexao em texto puro: sem STARTTLS, a senha e o e-mail trafegam
        // abertos. So o localhost (Mailpit do caminho descartado) dispensa criptografia.
        var usaStartTls = autenticado || SmtpHost != "localhost";

        using var cliente = new SmtpClient();
        await cliente.ConnectAsync(
            SmtpHost,
            SmtpPort,
            usaStartTls ? MailKit.Security.SecureSocketOptions.StartTls : MailKit.Security.SecureSocketOptions.None,
            ct);

        if (autenticado)
        {
            await cliente.AuthenticateAsync(SmtpUser!, SmtpPassword!, ct);
        }

        await cliente.SendAsync(mensagem, ct);
        await cliente.DisconnectAsync(true, ct);
    }
}
