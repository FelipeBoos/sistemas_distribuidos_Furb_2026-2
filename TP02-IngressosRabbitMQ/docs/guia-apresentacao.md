# Guia de apresentação (uso pessoal — não entregar)

Material de apoio para explicar o projeto na apresentação de 10 min. Não é parte da
documentação formal (isso está em `docs/topicos-3-4-5.md` e no PDF original).

## ⚠️ Lembrete: credenciais para o professor

Como o caminho atual usa contas gratuitas em nuvem (CloudAMQP/Neon/Mailtrap) em vez de Docker,
o professor precisa das credenciais reais do time para rodar o projeto sem criar as próprias
contas. Isso **não pode ir para o GitHub** (o enunciado pede o link do repositório no AVA, e o
repositório não deve conter segredos). Fluxo:
1. Manter `TP02-IngressosRabbitMQ/credenciais-professor.txt` atualizado (já gitignored) com o
   bloco pronto para colar em um `.env`.
2. Enviar o conteúdo desse arquivo pelo **AVA ou e-mail**, separado do link do repositório —
   nunca como commit, issue ou comentário público.
3. Antes da apresentação, confirmar com o professor se ele vai rodar o projeto por conta própria
   ou só assistir à demonstração ao vivo — se for só assistir, o envio das credenciais pode nem
   ser necessário.

## Por que cada decisão técnica foi tomada

- **RabbitMQ.Client puro, sem MassTransit**: dá para mostrar exatamente as exchanges, filas,
  bindings, SAC e RPC "na mão" — é isso que o trabalho pede para explicar (Tópico 3). MassTransit
  esconderia essas decisões atrás de convenção.
- **EF Core + Migrator dedicado**: cada máquina que roda o projeto (colegas, professor) pode não
  ter Postgres instalado — e com o Neon, nem precisa. Um serviço `Ingressos.Migrator` que só
  aplica as migrations e encerra garante que o schema existe antes de qualquer outro serviço
  subir — sem ninguém precisar rodar `dotnet ef database update` manualmente, basta rodá-lo uma
  vez antes dos demais.
- **Cluster de 3 nós RabbitMQ (implementado via Docker, depois descartado)**: o documento
  original já desenhava isso na Figura 2 (tolerância a falhas); com 3 nós dava pra fazer o teste
  de caos original (derrubar um nó e mostrar que o sistema continua funcionando). Foi
  implementado por completo antes de ser substituído pelo CloudAMQP — ver "Por que o Docker foi
  descartado" abaixo.
- **Outbox pattern**: evita o cenário clássico "gravei no banco mas a mensagem não foi publicada
  (ou o contrário)". Grava a entidade e o evento pendente na mesma transação; um serviço separado
  (`OutboxRelay`) publica depois. Fica fácil de explicar com um exemplo: se o RabbitMQ cair bem no
  momento em que o Alocador ia publicar `reserva.criada`, a mensagem não se perde — fica na tabela
  outbox até o relay conseguir publicar.
- **SAC configurável por variável de ambiente**: permite ligar/desligar o Single Active Consumer
  sem recompilar nada, só pra demonstração comparativa (Tópico 4). Em produção ficaria sempre
  ligado.
- **Retry em cascata via header `x-tentativa`**: ao invés de depender do header `x-death`
  automático do RabbitMQ (que é mais difícil de explicar ao vivo), o próprio `ServicoPagamento`
  decide, a cada recusa, para qual fila de retry mandar a mensagem — fica mais claro no código e
  mais fácil de debugar em tempo real durante a apresentação.
- **CloudAMQP + Neon + Mailtrap no lugar de Docker**: ver seção "Por que o Docker foi descartado"
  abaixo. Resultado prático: ninguém precisa instalar RabbitMQ, Postgres ou Docker — só criar 3
  contas gratuitas e preencher um `.env`.

## Como foi montado (ordem)

1. Esqueleto de projetos (`TP02-IngressosRabbitMQ/`, 1 projeto por serviço) — já estava pronto
   antes desta etapa.
2. `Ingressos.Messaging`: conexão RabbitMQ, declaração de topologia, publisher, consumidor base,
   cliente RPC.
3. `Ingressos.Persistence`: `DbContext`, entidades, migration inicial, `Ingressos.Migrator`.
4. Lógica de negócio de cada serviço (Alocador é o mais complexo: valida CPF/meia-entrada, chama
   o Antifraude via RPC, aloca o assento, grava outbox).
5. `docker-compose.yml` + `Dockerfile` genérico (um único Dockerfile parametrizado por
   `PROJECT_NAME`, reutilizado pelos 11 serviços — evita duplicar o mesmo Dockerfile 11 vezes).
6. Ferramentas de demonstração (`Ingressos.Demonstracoes` — gerador de carga; `chaos-test.ps1`/
   `.sh` — derruba um nó durante a carga).
7. **Migração do caminho principal para nuvem** (CloudAMQP + Neon + Mailtrap), depois que a
   instalação do Docker se mostrou inviável no ambiente de desenvolvimento disponível — ver
   próxima seção. `RabbitMqOptions`/`PostgresOptions` passaram a aceitar uma URI/connection
   string pronta (`RABBITMQ_URL`/`POSTGRES_CONNECTION_STRING`), o Notificador passou a autenticar
   no SMTP (Mailtrap exige, Mailpit não), e um novo `Ingressos.Messaging.DotEnv` carrega o `.env`
   direto no código (funciona igual via Visual Studio ou `dotnet run`, sem depender de variáveis
   exportadas no shell). Os arquivos Docker ficaram no repositório, intactos, como registro da
   abordagem avaliada.
8. Este texto e o `docs/topicos-3-4-5.md` (atualizados a cada mudança relevante).
9. **Validação de ponta a ponta contra a infra real** (CloudAMQP + Neon + Mailtrap) — rodando os
   serviços de verdade, não só `dotnet build`. Encontrados e corrigidos 5 bugs reais que só
   apareciam em runtime (detalhes em `MEMORY.md`, entrada "Fluxo completo validado..."):
   - `EstaAtiva` era um método C# chamado dentro de uma query EF Core — EF não traduz chamadas
     de método para SQL. Trocado por um array + `.Contains()`.
   - Serialização JSON inconsistente: o publisher principal usa camelCase, mas o RPC e os 4
     pontos de outbox serializavam em PascalCase (padrão) — campos chegavam com valor default
     no consumidor (`UsuarioId` virava `Guid.Empty`), **sem erro nenhum**, só efeito colateral
     silencioso. Centralizado num único `JsonSerializacao.Opcoes` usado em todo lugar.
   - RPC síncrono no mesmo canal/conexão do consumo principal causava deadlock (o despacho de
     mensagens numa conexão AMQP é sequencial). Resolvido com uma conexão TCP dedicada só para
     o RPC.
   - `double.TryParse` sem `CultureInfo.InvariantCulture` lia `"0.05"` errado no locale pt-BR do
     Windows, fazendo o Antifraude recusar quase toda compra.
   - O payload da fila de retry de pagamento usava o tipo errado (`PagamentoRecusado` em vez de
     `PagamentoSolicitadoCommand`), quebrando a reentrega depois do TTL.

   Isso é um ótimo exemplo prático para a apresentação: mostra por que testar só com
   `dotnet build`/testes unitários não basta para sistemas distribuídos — bugs de integração
   (serialização entre serviços, concorrência de canal AMQP, locale do SO) só aparecem rodando
   de verdade contra a infraestrutura real.

## Por que o Docker foi descartado

A ideia original (Tópico 3) era rodar tudo via `docker compose up --build`: cluster RabbitMQ de
3 nós, Postgres, Mailpit e os 11 serviços, tudo em containers — zero instalação manual além do
Docker em si.

Na prática, o Docker Desktop no Windows exige o WSL2 (Subsistema do Windows para Linux), e
habilitar o WSL2 exige rodar `wsl --install` com **privilégios de administrador** e depois
**reiniciar o Windows**. No ambiente de desenvolvimento disponível não havia acesso de
administrador nem possibilidade de reiniciar a máquina na hora — tentei automatizar via
`wsl --install` pelo terminal, mas falhou exatamente por falta de elevação (confirmado checando
`IsInRole(Administrator)` antes de tentar).

Diante disso, a alternativa foi usar serviços gerenciados gratuitos que não exigem nenhuma
instalação: **CloudAMQP** (RabbitMQ) e **Neon** (Postgres), com **Mailtrap** no lugar do Mailpit
para o SMTP de teste. O trade-off: perde-se o cluster de 3 nós sob controle próprio (o plano
gratuito do CloudAMQP é uma instância única compartilhada), então o teste de caos precisou ser
adaptado para derrubar um processo consumidor local em vez de um nó do broker (ver Caso 5 do
Tópico 4 em `docs/topicos-3-4-5.md`). Em compensação, ganha-se TLS "de graça" (CloudAMQP e Neon
exigem conexão criptografada por padrão), o que no plano original com Docker local ficaria para
uma etapa futura.

Os arquivos Docker (`docker-compose.yml`, `Dockerfile`, scripts de cluster/chaos test) **não
foram apagados** — continuam no repositório como evidência de que a abordagem foi avaliada e
implementada por completo antes de ser substituída, não abandonada por falta de tentativa.

## Pontos que provavelmente vão gerar pergunta

- **"Por que o Antifraude não bloqueia a compra se der timeout?"** Decisão deliberada: se o
  Antifraude não responder em 5s, o Alocador assume risco aceitável (fail-open) para não travar
  vendas por uma falha de um serviço secundário. Isso prioriza disponibilidade sobre uma análise
  de risco perfeita — um trade-off real que sistemas de alta demanda fazem.
- **"Cadê o cadastro de usuário?"** Não existe fluxo de cadastro nesta demonstração. O Alocador
  cria o `Usuario` na primeira compra, com limite padrão de 4 ingressos e um CPF sintético
  derivado do `UsuarioId`. Em um sistema real, isso viria de um serviço de identidade separado.
- **"A cota de meia-entrada está exatamente como a lei pede?"** Não — é uma simplificação
  didática: no máximo 40% da capacidade do setor pode ser meia-entrada. A Lei 12.933/2013 tem
  mais nuances (documentação exigida, aplicação por evento, etc.) fora do escopo do trabalho.

## Divergências em relação ao plano original (documento dos Tópicos 1-2)

*(Atualizar esta seção se mais alguma coisa mudar durante o desenvolvimento.)*

- **Endpoint de pagamento não estava explícito no Quadro 1**: o documento original não detalhava
  quem publica `pagamento.solicitado`. Foi adicionado um endpoint `POST /pagamentos` na API de
  Vendas para fechar esse gap — consistente com o papel da API de Vendas como "ponto de entrada
  do cliente", só que o documento não tinha detalhado esse passo especificamente.
- **Estado "Reservada" do diagrama de estados (Figura 5) não é usado separadamente**: o código
  vai direto de `Solicitada` para `AguardandoPagamento` ao alocar o assento, porque não há nenhum
  evento ou processamento distinto entre "acabei de alocar" e "aguardando o cliente pagar" nesta
  implementação. O valor `Reservada` continua existindo no enum (fiel ao diagrama original), mas
  não é atribuído por nenhum serviço atualmente.
- **Retry de pagamento usa header de aplicação, não encadeamento puro de dead-letter entre as 3
  filas de retry**: ver justificativa acima ("Por que cada decisão..."). O efeito observável
  (espera crescente 5s/30s/2min, parking lot após 3 tentativas) é o mesmo descrito no documento.
- **Cluster de 3 nós RabbitMQ (auto-hospedado) substituído por CloudAMQP (instância gerenciada
  única)**: o documento original (Figura 2) desenhava um cluster de 3 nós via Docker. Isso foi
  implementado por completo (`docker-compose.yml`, `infra/rabbitmq/join-cluster.sh`) e depois
  descartado como caminho principal — ver "Por que o Docker foi descartado" acima. O RabbitMQ
  gerenciado (CloudAMQP) ainda garante alta disponibilidade e TLS, só que sob controle do
  provedor, não da nossa infraestrutura.
- **Mailpit substituído por Mailtrap**: mesmo papel (sandbox de e-mail para testar o
  Notificador sem enviar e-mails reais), só que em nuvem em vez de container local. Exigiu
  autenticação SMTP (STARTTLS + usuário/senha), que o Mailpit não pedia.
- **Teste de caos adaptado para o nível de aplicação**: o teste original (`infra/chaos-test.ps1`,
  mantido como referência) derrubava um nó do cluster RabbitMQ. O teste atual
  (`infra/chaos-test-local.ps1`) derruba uma réplica local do Alocador durante a carga — ainda
  demonstra failover do Single Active Consumer, só que no processo consumidor, não no broker.
