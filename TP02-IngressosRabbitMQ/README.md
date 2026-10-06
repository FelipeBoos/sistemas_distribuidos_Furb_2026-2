# TP02 — Sistema de Mensageria com RabbitMQ (Venda de Ingressos)

Plataforma de venda de ingressos para eventos de grande demanda, descrita em
`Sistema Ingressos RabbitMQ.pdf` (raiz do repositório). Implementa os Tópicos 1-5 do trabalho:
descrição do cenário e arquitetura (documento PDF), e configuração do RabbitMQ, exemplos de uso
e considerações técnicas (código funcional + `docs/topicos-3-4-5.md`).

## Pré-requisitos

- **.NET 9 SDK** e **Visual Studio Community** (ou `dotnet` CLI).
- Contas gratuitas em três serviços de nuvem (nenhuma instalação local de RabbitMQ/Postgres
  necessária):
  - **[CloudAMQP](https://www.cloudamqp.com)** — broker RabbitMQ gerenciado.
  - **[Neon](https://neon.tech)** — PostgreSQL serverless.
  - **[Mailtrap](https://mailtrap.io)** — sandbox SMTP para testar o envio de e-mails.

> Docker foi avaliado como alternativa e descartado — ver "Abordagem descartada: Docker" no
> final deste README e `docs/guia-apresentacao.md`.

> **Para quem vai apenas rodar o projeto já configurado (ex.: o professor avaliando a
> entrega)**: não é preciso criar contas novas. O `.env` com as credenciais reais do time foi
> enviado separadamente pelo AVA/e-mail (nunca pelo GitHub — ver `credenciais-professor.txt`,
> que também não é versionado). Basta colocar esse `.env` em `TP02-IngressosRabbitMQ/` e pular
> direto para o passo 3 abaixo.

## Como rodar

1. Crie as contas gratuitas acima (se tiver dúvida em algum passo, peça ajuda) e anote:
   - CloudAMQP: a **AMQP URL** da instância (página "Details" → "AMQP URL"), formato
     `amqps://usuario:senha@host.rmq2.cloudamqp.com/vhost`.
   - Neon: a **connection string** do projeto (Dashboard → Connect → formato ".NET"/"psql"),
     já com `sslmode=require`.
   - Mailtrap: as credenciais SMTP do sandbox (Email Testing → Integration → SMTP).
2. Copie `.env.example` para `.env` (dentro de `TP02-IngressosRabbitMQ/`) e preencha
   `RABBITMQ_URL`, `POSTGRES_CONNECTION_STRING` e `SMTP_HOST`/`SMTP_PORT`/`SMTP_USER`/`SMTP_PASSWORD`
   com os valores do passo 1.
3. Aplique o schema no banco (uma vez só): rode `Ingressos.Migrator`
   (`dotnet run --project Ingressos.Migrator`, ou F5 nele isolado no Visual Studio).
4. Abra `sistemas_distribuidos_Furb_2026-2.slnx` no Visual Studio Community. Configure múltiplos
   projetos de inicialização: botão direito na solução → **Propriedades** → **Startup Project** →
   **Multiple startup projects**, e marque "Start" nos 11 serviços de aplicação:
   `Ingressos.ApiVendas`, `Ingressos.Admissor`, `Ingressos.Alocador`,
   `Ingressos.ServicoPagamento`, `Ingressos.Antifraude`, `Ingressos.LiberadorReserva`,
   `Ingressos.EmissorIngresso`, `Ingressos.Notificador`, `Ingressos.GatewayWebSocket`,
   `Ingressos.Auditoria`, `Ingressos.OutboxRelay` (os demais projetos ficam em "None").
5. Rode com **F5**. A API de Vendas sobe em `https://localhost:xxxx` (porta definida no
   `launchSettings.json` do projeto — confira no console de saída de cada serviço).

Exemplo de fluxo completo via HTTP (ajuste a porta da API de Vendas conforme o passo 5):

```
curl -X POST https://localhost:7000/fila/entrar -H "Content-Type: application/json" -d "{\"usuarioId\":\"...\",\"eventoId\":\"...\"}"
curl -X POST https://localhost:7000/compras -H "Content-Type: application/json" -d "{\"usuarioId\":\"...\",\"setorId\":\"...\",\"setor\":\"pista\",\"quantidadeIngressos\":1,\"meiaEntrada\":false}"
curl -X POST https://localhost:7000/pagamentos -H "Content-Type: application/json" -d "{\"reservaId\":\"...\",\"valor\":100,\"idempotencyKey\":\"...\"}"
```

Ver `docs/topicos-3-4-5.md` (Tópico 4) para os casos de uso completos, incluindo as
demonstrações de Single Active Consumer e de tolerância a falhas (teste de caos).

## Projetos

| Projeto | Tipo | Papel |
|---|---|---|
| `Ingressos.Domain` | Class Library | Entidades de domínio (Evento, Setor, Assento, Usuario, Reserva, Pagamento, Ingresso) |
| `Ingressos.Contracts` | Class Library | DTOs de comandos e eventos trocados via RabbitMQ (routing keys do Quadro 2) |
| `Ingressos.Messaging` | Class Library | Conexão RabbitMQ, topologia, publisher, consumidor base, cliente RPC, carregamento do `.env` |
| `Ingressos.Persistence` | Class Library | `DbContext` (EF Core/Npgsql), entidades + tabela outbox, migrations |
| `Ingressos.Migrator` | Console | Aplica as migrations no banco (rodar uma vez, antes dos demais serviços) |
| `Ingressos.ApiVendas` | ASP.NET Core Web API | Ponto de entrada HTTP/REST do cliente |
| `Ingressos.Admissor` | Worker Service | Controla quem entra na área de compra (sala de espera) |
| `Ingressos.Alocador` | Worker Service | Reserva o assento sem overbooking (Single Active Consumer, 1 por setor) |
| `Ingressos.ServicoPagamento` | Worker Service | Cobra o cliente no gateway de pagamento externo (simulado) |
| `Ingressos.Antifraude` | Worker Service | Avalia o risco da compra (padrão RPC) |
| `Ingressos.LiberadorReserva` | Worker Service | Trata reservas expiradas (via Dead Letter Exchange) |
| `Ingressos.EmissorIngresso` | Worker Service | Gera o ingresso com QR Code |
| `Ingressos.Notificador` | Worker Service | Envia e-mails ao cliente (via Mailtrap) |
| `Ingressos.GatewayWebSocket` | ASP.NET Core Web API | Repassa a posição na fila ao navegador em tempo real (WebSocket) |
| `Ingressos.Auditoria` | Worker Service | Registra todo o histórico de eventos (fila Stream, replay) |
| `Ingressos.OutboxRelay` | Worker Service | Republica pendências da tabela outbox no RabbitMQ |
| `Ingressos.Demonstracoes` | Console | Gerador de carga para as demonstrações do Tópico 4 |
| `Ingressos.Tests` | xUnit | Testes das entidades e contratos compartilhados |

Todos os projetos de serviço referenciam `Ingressos.Domain` e `Ingressos.Messaging`; os que
persistem dados também referenciam `Ingressos.Persistence`.

## Documentação

- `docs/topicos-3-4-5.md` — texto formal (configuração do RabbitMQ, exemplos de uso,
  considerações técnicas), para a entrega da equipe.
- `docs/guia-apresentacao.md` — justificativas das decisões técnicas e divergências em relação
  ao plano original (inclui o porquê do Docker ter sido descartado), material de apoio para a
  apresentação (não é parte da entrega).

## Build e testes

```
dotnet build
dotnet test TP02-IngressosRabbitMQ/Ingressos.Tests/Ingressos.Tests.csproj
```

## Abordagem descartada: Docker

O projeto chegou a ser todo preparado para rodar via `docker compose up` (cluster RabbitMQ de 3
nós, PostgreSQL e Mailpit em containers, um `Dockerfile` por serviço). Esses arquivos
(`docker-compose.yml`, `Dockerfile`, `infra/rabbitmq/join-cluster.sh`,
`infra/chaos-test.ps1`/`.sh`) continuam no repositório como referência, mas **não são o caminho
usado atualmente** — a instalação do Docker Desktop no Windows exige WSL2 com privilégios de
administrador, que não estavam disponíveis no ambiente de desenvolvimento. Ver
`docs/guia-apresentacao.md` para a explicação completa.
