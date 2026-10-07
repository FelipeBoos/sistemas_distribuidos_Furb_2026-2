# 3 Configuração do RabbitMQ

## 3.1 Parâmetros de Configuração

A topologia descrita na Seção 2.2 (exchanges, filas, bindings e argumentos especiais) foi
implementada de forma idempotente na classe `Ingressos.Messaging.Topologia`, declarada no
início da execução de cada serviço. O Quadro 3 detalha os parâmetros efetivamente usados.

Quadro 3 – Parâmetros de configuração implementados

| Exchange/Fila | Tipo/Argumentos | Observação |
|---|---|---|
| `ingressos.eventos` | topic, durável | Canal principal de toda a jornada da compra |
| `ingressos.dlx` | fanout, durável | Recebe reservas expiradas via dead-letter |
| `ingressos.retry` | topic, durável | Reencaminha pagamentos recusados |
| `sala-espera` | `x-max-priority: 10`, `x-max-length: 100000`, `x-overflow: reject-publish` | Backpressure: novas entradas são rejeitadas quando a fila está cheia, em vez de derrubar o backend |
| `alocacao.<setor>` | `x-queue-type: quorum`, `x-single-active-consumer: true` (desligável via `ALOCADOR_SINGLE_ACTIVE_CONSUMER=false`) | Uma fila por setor de demonstração (pista, cadeira, camarote) |
| `reservas-pendentes` | `x-message-ttl: 600000`, `x-dead-letter-exchange: ingressos.dlx` | TTL de 10 minutos para pagamento |
| `pagamento.retry.5s` / `.30s` / `.2m` | `x-message-ttl` crescente, `x-dead-letter-exchange: ingressos.eventos`, `x-dead-letter-routing-key: pagamento.solicitado` | Cada fila expira e devolve a mensagem para nova tentativa de cobrança |
| `pagamento-parking-lot` | fila durável comum | Revisão manual após 3 tentativas de retry esgotadas |
| `antifraude` | fila durável comum | Consumida em padrão RPC (`reply_to` + `correlation_id`) |
| `ingressos.dlq` / `dlq.ingressos` | fanout durável, fila durável comum | Dead-letter das filas de consumo (`alocacao.*`, `pagamento`, `antifraude`, `emissao`, `notificacao`): mensagens rejeitadas sem *requeue* ficam aqui para análise, em vez de serem descartadas |
| `auditoria-stream` | `x-queue-type: stream`, `x-max-age: 1h`, `x-max-length-bytes: 50 MB`; bindings `fila.#`, `antifraude.#`, `compra.#`, `reserva.#`, `pagamento.#`, `ingresso.#`, `assento.#` | Histórico de eventos de negócio com replay desde o início. Não recebe o heartbeat `sala-espera.status` |

A contagem de tentativas de pagamento é controlada por um header de aplicação
(`x-tentativa`) propagado nas mensagens republicadas, em vez de depender do header `x-death`
gerado automaticamente pelo RabbitMQ — isso deixa explícito, no próprio código do
`Ingressos.ServicoPagamento`, qual fila de retry (5s, 30s ou 2min) deve receber a próxima
tentativa.

## 3.2 Requisitos de Segurança

- **Autenticação e autorização**: o RabbitMQ roda em uma instância gerenciada do CloudAMQP, com
  usuário e *vhost* dedicados e exclusivos da instância (não há usuário `guest`/padrão
  acessível externamente); o PostgreSQL roda em um projeto Neon com usuário e senha próprios do
  projeto. Em ambos os casos, as credenciais reais ficam em um arquivo `.env` não versionado
  (`.env.example` documenta as chaves esperadas) e nunca são hardcoded em `appsettings.json`.
- **Criptografia em trânsito**: tanto o CloudAMQP (AMQPS, esquema `amqps://` na URI de conexão)
  quanto o Neon (`sslmode=require` por padrão em toda conexão) exigem TLS — a aplicação não
  precisou implementar nada adicional para isso: `RabbitMQ.Client` habilita SSL automaticamente
  a partir do esquema da URI, e o Npgsql respeita o `sslmode` presente na connection string
  fornecida pelo Neon.

# 4 Exemplos de Uso

Casos de uso implementados e executáveis com os serviços rodando localmente (Visual Studio ou
`dotnet run`), conectados ao RabbitMQ (CloudAMQP) e ao PostgreSQL (Neon) em nuvem — ver README
do módulo para o passo a passo completo de configuração.

**Caso 1 – Fluxo feliz (compra completa)**
Entrada: `POST /compras` na API de Vendas com usuário, setor e quantidade.
Processamento: Alocador consulta o Antifraude (RPC), valida limite de CPF e cota de
meia-entrada, aloca um assento e grava `reserva.criada` via outbox; cliente confirma com
`POST /pagamentos`; Serviço de Pagamento aprova a cobrança; Emissor de Ingresso gera o QR Code;
Notificador envia e-mail (visível na caixa de entrada do sandbox do Mailtrap).
Saída esperada: ingresso emitido, e-mail de confirmação recebido, assento com status `Vendido`
no banco.

**Caso 2 – Reserva expira sem pagamento**
Entrada: reserva criada, cliente não confirma pagamento.
Processamento: após 10 minutos (TTL da fila `reservas-pendentes`), a mensagem é
dead-lettered para `ingressos.dlx`; o Liberador de Reserva marca a reserva como `Expirada` e
libera o assento.
Saída esperada: assento volta a `Disponivel`; reserva expirada no banco.

**Caso 3 – Pagamento recusado com retry**
Entrada: `POST /pagamentos` com taxa de recusa simulada (`SERVICOPAGAMENTO_TAXA_RECUSA`).
Processamento: cada recusa reencaminha a cobrança pelas filas `pagamento.retry.5s` → `.30s` →
`.2m`; após a terceira tentativa recusada, a mensagem vai para `pagamento-parking-lot`.
Saída esperada: até 3 novas tentativas automáticas de cobrança, espaçadas; revisão manual
registrada no parking lot se todas falharem.

**Caso 4 – Comparativo com/sem Single Active Consumer**
Entrada: `Ingressos.Demonstracoes` (`dotnet run -- <setor> <assentos> <solicitações>`),
disparando dezenas de solicitações concorrentes contra um setor com poucos assentos.
Processamento: com `ALOCADOR_SINGLE_ACTIVE_CONSUMER=true` (padrão), apenas um consumidor
processa a fila do setor por vez; com `=false` e múltiplas réplicas do Alocador, mais de um
consumidor pode processar mensagens da mesma fila simultaneamente.
Saída esperada: sem SAC, é possível observar mais de uma reserva ativa para o mesmo assento
(overbooking) antes do índice único do banco rejeitar a segunda gravação; com SAC, isso não
ocorre.

**Caso 5 – Teste de caos (nível de aplicação)**
Entrada: `infra/chaos-test-local.ps1`, que sobe 2 instâncias locais do Alocador para o mesmo
setor, dispara o gerador de carga e derruba uma das duas instâncias (`Stop-Process`) no meio da
execução.
Processamento: com Single Active Consumer habilitado, apenas uma das duas instâncias processa a
fila por vez; ao derrubar a instância ativa, o RabbitMQ detecta a queda da conexão e promove a
instância restante a consumidora ativa, que continua processando as mensagens pendentes.
Saída esperada: nenhuma venda duplicada, mesmo com a queda de uma das instâncias durante a
carga. (O teste original, que derrubava um nó do cluster RabbitMQ, usava um cluster de 3 nós
auto-hospedado via Docker — abordagem descartada; o plano gratuito do CloudAMQP é uma instância
única gerenciada, então esse teste específico não é mais aplicável. Ver
`docs/guia-apresentacao.md`.)

# 5 Considerações Técnicas

## 5.1 Tecnologias e Linguagens

.NET 9 / C# 13 em todos os serviços; RabbitMQ.Client 7.x (API assíncrona) para mensageria;
Entity Framework Core com Npgsql para persistência em PostgreSQL; MailKit para envio de e-mail
via SMTP. A infraestrutura de apoio (broker RabbitMQ, banco PostgreSQL, sandbox SMTP) roda em
serviços gerenciados gratuitos — **CloudAMQP**, **Neon** e **Mailtrap**, respectivamente — em
vez de containers auto-hospedados, eliminando a necessidade de instalar e operar essa
infraestrutura localmente.

**Pré-requisito de ambiente**: apenas o **.NET 9 SDK** (ou Visual Studio Community) e contas
gratuitas em CloudAMQP, Neon e Mailtrap — não é necessário instalar PostgreSQL ou RabbitMQ
localmente. O passo a passo de configuração está em `TP02-IngressosRabbitMQ/README.md`.

## 5.2 Padrão de Mensagens

As mensagens trocadas entre os serviços são serializadas em **JSON** (`System.Text.Json`),
como `records` imutáveis definidos em `Ingressos.Contracts`. A escolha prioriza legibilidade
durante o desenvolvimento e depuração (o conteúdo é inspecionável diretamente pela interface de
gerenciamento do RabbitMQ) e evita a necessidade de um *schema registry* externo, adequado ao
escopo acadêmico do trabalho. Formatos binários como Avro ou Protobuf trariam menor overhead de
serialização e validação de schema mais rígida, mas exigiriam infraestrutura adicional
(registro de schemas) não justificada pelo volume e pela natureza didática desta entrega.

## 5.3 Boas Práticas Adotadas

- **Idempotência**: `Pagamento.IdempotencyKey` com índice único evita cobrança duplicada em
  reentregas; serviços de emissão e alocação verificam existência antes de gravar.
- **Publisher confirms + ack manual**: o produtor aguarda confirmação de persistência do
  broker; o consumidor só confirma (`ack`) após concluir o processamento com sucesso,
  encaminhando falhas para `nack` sem *requeue*: a mensagem segue para `ingressos.dlq`
  (ver Quadro 3). Se o serviço estiver sendo desligado no meio de um processamento, a mensagem é
  devolvida à fila (`requeue`), para ser reprocessada no próximo start.
- **Padrão Outbox**: gravação da entidade de domínio e do evento a publicar na mesma transação
  (`Ingressos.Persistence.OutboxMessage`), com o `Ingressos.OutboxRelay` publicando de forma
  assíncrona — elimina o risco de gravar no banco sem publicar o evento correspondente (ou
  vice-versa).
- **Defesa em profundidade contra overbooking**: Single Active Consumer por setor *e* índice
  único filtrado em `Reserva.AssentoId` (apenas para reservas ativas) no banco de dados.
- **Configuração via ambiente**: nenhuma credencial ou endereço de infraestrutura é hardcoded;
  tudo vem de variáveis de ambiente (`RabbitMqOptions.FromEnvironment()`,
  `PostgresOptions.ConnectionStringFromEnvironment()`), permitindo rodar o mesmo código em
  qualquer máquina apenas trocando o `.env`.

## 5.4 Revisão final e correções encontradas na execução

Após a implementação, o código passou por uma revisão final (uma revisão com contexto e uma
revisão cega, sem acesso à narrativa do trabalho) e por testes contra a infraestrutura real
(CloudAMQP, Neon e Mailtrap). Os principais pontos tratados:

- **Dead-letter em todas as filas de consumo.** Antes, só `reservas-pendentes` tinha DLX, e uma
  falha em qualquer consumidor descartava a mensagem sem registro. Agora as filas de consumo
  enviam rejeições para `ingressos.dlq` (Quadro 3).
- **Quantidade de ingressos.** Cada ingresso gera uma reserva (um assento por reserva). A
  compra é tudo ou nada: se o setor não tem assentos para a quantidade toda, nenhuma reserva é
  criada. A quantidade é validada entre 1 e 4 na API de Vendas e novamente no Alocador.
- **Preço no servidor.** O cliente não informa mais o valor do pagamento. O `ServicoPagamento`
  calcula o valor a partir do preço do setor da reserva, com meia-entrada em 50% do preço.
- **Limite por CPF entre setores.** Cada setor tem sua própria fila (com Single Active Consumer),
  então duas compras do mesmo usuário em setores diferentes podiam ler a contagem ao mesmo tempo.
  O Alocador agora trava a linha do usuário (`SELECT ... FOR UPDATE`) dentro da transação de
  reserva. A consulta ao antifraude fica fora dessa transação, para não segurar o lock durante a
  chamada RPC.
- **Antifraude fail-closed.** Se o antifraude não responder em 5 segundos, ou houver falha de
  infraestrutura na chamada, a compra é rejeitada com o motivo `antifraude_indisponivel`. Antes a
  compra era aprovada nesse caso.
- **Criação concorrente de usuário.** Duas primeiras compras simultâneas do mesmo CPF não geram mais
  erro de chave primária: o segundo processo relê o registro criado pelo primeiro.
- **Resposta honesta da API.** Quando o broker não confirma a publicação, a API responde 503
  em vez de 202. A routing key de uma compra é derivada do setor lido no banco, nunca de texto
  livre enviado pelo cliente.

### Problemas que só apareceram com a infraestrutura real

- **Limite de taxa do Mailtrap.** O plano gratuito recusa rajadas de envio (`Too many emails per
  second`). Com prefetch alto no Notificador, os e-mails excedentes iam para a DLQ e se perdiam.
  O Notificador agora processa um e-mail por vez e tenta de novo, com espera crescente, antes de
  desistir.
- **Heartbeat do Admissor na auditoria.** O Admissor publicava o status da sala de espera a
  cada segundo, e o binding `#` da stream de auditoria copiava cada publicação. Isso gerava
  cerca de 3.600 mensagens por hora com a aplicação rodando, mesmo sem nenhuma compra. Agora o
  status é publicado só quando o total muda (ou a cada 30 segundos), e a stream recebe apenas os
  eventos de negócio.
- **Retenção da stream.** A stream não apaga mensagens ao ser consumida, e a retenção só é
  aplicada quando um segmento fecha. A conta gratuita impõe segmentos de 5 MB (política
  `stream-limits`), então com volume baixo a limpeza por tempo quase não tem efeito. Por isso a
  correção principal foi reduzir o volume enviado à stream.
- **Antifraude aleatório na demonstração.** O antifraude sorteia risco alto com a taxa de
  `ANTIFRAUDE_TAXA_RISCO_ALTO`. Com 5%, uma demonstração falhava por sorteio. O script de
  demonstração fixa a taxa em 0 (configurável por parâmetro).
- **Leitura de JSON no PowerShell 5.1.** A chamada `@(Invoke-RestMethod ...)` com pipeline
  desempacota o array como um único item. O script de demonstração usa uma função que devolve
  os itens individualmente.

### Demonstração para o professor

Há duas formas de mostrar uma compra completa (ver `README.md`, seção "Demonstração rápida"):

- **Tela no navegador**, servida pela própria API em `http://localhost:5224/`. Mostra a linha do
  tempo da compra: reserva criada, pagamento solicitado, pagamento aprovado e ingresso emitido,
  com o QR code.
- **Script de terminal** (`infra/demo-compra.ps1`). Faz a mesma sequência e encerra, ao final,
  todos os processos que iniciou (árvore inteira, via `taskkill /T`). Os logs de cada serviço
  ficam em `%TEMP%\ingressos-demo`.

Validado em execução real: compra de 2 ingressos gerando 2 reservas, 2 pagamentos aprovados e
2 ingressos emitidos; limite por CPF de 4 respeitado com duas compras simultâneas em setores
diferentes (3 reservas, não 6).
