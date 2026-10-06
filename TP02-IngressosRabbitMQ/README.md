# TP02 — Sistema de Mensageria com RabbitMQ (Venda de Ingressos)

Esqueleto de código da plataforma de venda de ingressos para eventos de grande demanda,
descrita em `Sistema Ingressos RabbitMQ.pdf` (raiz do repositório). Esta etapa cobre apenas a
estrutura de projetos referente à Arquitetura da Solução (Tópico 2 do trabalho); a configuração
real de exchanges, filas, bindings e políticas de retry do RabbitMQ (Tópico 3) ainda não foi
implementada — os pontos de integração futuros estão marcados com `// TODO (Topico 3)` em cada
projeto.

## Projetos

| Projeto | Tipo | Papel |
|---|---|---|
| `Ingressos.Domain` | Class Library | Entidades de domínio (Evento, Setor, Assento, Usuario, Reserva, Pagamento, Ingresso) |
| `Ingressos.Contracts` | Class Library | DTOs de comandos e eventos trocados via RabbitMQ (routing keys do Quadro 2) |
| `Ingressos.ApiVendas` | ASP.NET Core Web API | Ponto de entrada HTTP/REST do cliente |
| `Ingressos.Admissor` | Worker Service | Controla quem entra na área de compra (sala de espera) |
| `Ingressos.Alocador` | Worker Service | Reserva o assento sem overbooking (Single Active Consumer, 1 por setor) |
| `Ingressos.ServicoPagamento` | Worker Service | Cobra o cliente no gateway de pagamento externo |
| `Ingressos.Antifraude` | Worker Service | Avalia o risco da compra (padrão RPC) |
| `Ingressos.LiberadorReserva` | Worker Service | Trata reservas expiradas (via Dead Letter Exchange) |
| `Ingressos.EmissorIngresso` | Worker Service | Gera o ingresso com QR Code |
| `Ingressos.Notificador` | Worker Service | Envia e-mails ao cliente |
| `Ingressos.GatewayWebSocket` | ASP.NET Core Web API | Repassa a posição na fila ao navegador em tempo real |
| `Ingressos.Auditoria` | Worker Service | Registra todo o histórico de eventos (fila Stream) |
| `Ingressos.OutboxRelay` | Worker Service | Republica pendências da tabela outbox no RabbitMQ |
| `Ingressos.Tests` | xUnit | Testes das entidades e contratos compartilhados |

Todos os projetos de serviço referenciam `Ingressos.Domain` e `Ingressos.Contracts`.

## Fora de escopo nesta etapa

- Pacotes NuGet de conectividade (`RabbitMQ.Client`/`MassTransit`, `Npgsql`, `MailKit`).
- `docker-compose.yml` com RabbitMQ, PostgreSQL e Mailpit.
- Configuração de exchanges, filas, bindings, TTL, Dead Letter Exchange e retry.
- Lógica de negócio (validação de CPF, cota de meia-entrada, idempotência, etc.).

## Build e testes

```
dotnet build
dotnet test TP02-IngressosRabbitMQ/Ingressos.Tests/Ingressos.Tests.csproj
```
