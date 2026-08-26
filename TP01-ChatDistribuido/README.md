# TP01 — Chat Distribuído em Malha (Full Mesh)

Chat entre N participantes, sem servidor central, sem coordenador e sem repositório
privilegiado da lista de participantes. Cada instância é, ao mesmo tempo, servidor
(aceita conexões) e cliente (inicia conexões), formando uma **malha completa**: todo
par de participantes conversa por uma conexão TCP direta, sem intermediários.

Evolução do projeto `SocketChat/` (chat ponto-a-ponto entre exatamente dois pares) —
o framing por prefixo de tamanho (`Frames.cs`) foi reaproveitado sem alterações.

Restrição de tecnologia respeitada: apenas a classe `Socket` sobre TCP. A única
biblioteca usada além do transporte é `System.Text.Json` (parte do .NET, usada só
para serializar o protocolo próprio descrito abaixo — não é biblioteca de rede/P2P).

## Como rodar

Requer .NET 10 SDK.

```
dotnet run -- --port <porta> --name <apelido> [--host <host-anunciado>] [--peers host:porta,host:porta,...]
```

ou via arquivo de configuração:

```
dotnet run -- --config alice.json
```
```json
{ "port": 9001, "name": "alice", "peers": ["127.0.0.1:9002", "127.0.0.1:9003"] }
```

`--host` (padrão `127.0.0.1`) é o endereço pelo qual **outros peers alcançam este
nó** — usado no handshake e na regra de desempate de conexão duplicada (seção
"Decisões de design"). Em máquinas diferentes na mesma rede, use o IP real da
máquina; em teste local, o padrão já serve.

### Exemplo com 3 participantes (3 terminais)

```
Terminal 1: dotnet run -- --port 9001 --name alice --peers 127.0.0.1:9002,127.0.0.1:9003
Terminal 2: dotnet run -- --port 9002 --name bob   --peers 127.0.0.1:9001,127.0.0.1:9003
Terminal 3: dotnet run -- --port 9003 --name carol --peers 127.0.0.1:9001,127.0.0.1:9002
```

Cada peer conecta-se aos demais listados em `--peers` e também aceita conexões dos
outros — a lista de pares configurada é estática, não há descoberta dinâmica: cada
instância só conhece, no start, os endereços informados na sua própria configuração.

### Subindo as 3 instâncias de uma vez (Windows/cmd)

Em vez de abrir cada terminal manualmente, rode este comando a partir da raiz do
repositório — ele abre uma janela `cmd` por participante, já com os `--peers`
preenchidos:

```
start "alice" cmd /k dotnet run --project TP01-ChatDistribuido -- --port 9001 --name alice --peers 127.0.0.1:9002,127.0.0.1:9003
start "bob"   cmd /k dotnet run --project TP01-ChatDistribuido -- --port 9002 --name bob   --peers 127.0.0.1:9001,127.0.0.1:9003
start "carol" cmd /k dotnet run --project TP01-ChatDistribuido -- --port 9003 --name carol --peers 127.0.0.1:9001,127.0.0.1:9002
```

### Comandos

| Comando | Efeito |
|---|---|
| `texto livre` | Envia mensagem em broadcast a todos os peers conectados |
| `/list` | Lista os participantes atualmente conectados a **este** nó |
| `/msg apelido texto` | Envia mensagem privada, direto, apenas ao destinatário |
| `/quit` | Anuncia a saída (`Bye`) a todos os peers e encerra |

`/list` mostra sempre uma visão **local**: os peers com quem este nó tem conexão
direta agora. Como a malha é completa, isso coincide com "todos os participantes
vivos", mas cada nó chega a essa lista de forma independente — não existe uma lista
global sincronizada em nenhum lugar.

## Arquivos

| Arquivo | Responsabilidade |
|---|---|
| `Frames.cs` | Framing por prefixo de tamanho (4 bytes big-endian + payload, máx. 64KB), reaproveitado do `SocketChat` |
| `Connection.cs` | Criação do listener e `TryConnectAsync` (connect com timeout, sem lançar em falha — o chamador decide o retry) |
| `WireMessage.cs` | Protocolo da malha: tipos de mensagem e serialização JSON sobre o frame |
| `PeerId.cs` | Identidade de um peer: apelido + host:porta de escuta |
| `PeerConfig.cs` | Parsing de argumentos de linha de comando e de arquivo de configuração JSON |
| `PeerConnection.cs` | Uma conexão TCP ativa: fila de envio, receive loop, send loop, heartbeat, isolados por peer |
| `MeshNode.cs` | Accept loop, dial-out por peer conhecido, dicionário de peers conectados, desempate, broadcast, roteamento |
| `ConsoleUi.cs` | Loop de leitura de stdin e interpretação de comandos |
| `ConsoleLog.cs` | `Console.WriteLine` com lock, para não intercalar linhas entre a UI e as receive loops concorrentes |
| `Program.cs` | Ponto de entrada: parse de configuração e ciclo de vida do nó |

## Protocolo da malha (`WireMessage`)

Cada frame entregue pelo framing existente carrega um JSON UTF-8:

```csharp
enum MessageType { Hello, Chat, Private, PeerJoined, Bye, Ping }
record WireMessage(MessageType Type, string From, string? To, string? Text,
                    string? ListenHost, int? ListenPort, DateTimeOffset Timestamp);
```

- **Hello** — primeira mensagem trocada nos dois sentidos em **toda** conexão nova
  (tanto a que eu disquei quanto a que aceitei), com timeout de 5s. Informa o apelido
  e a porta de escuta de quem enviou. É necessário porque quem *aceita* uma conexão
  só vê a porta efêmera de origem do socket, não a porta em que o outro lado está
  escutando — sem o Hello não seria possível saber isso, nem aplicar a regra de
  desempate abaixo.
- **Chat** — mensagem de broadcast; o nó a envia para todos os peers atualmente
  conectados, cada um recebendo diretamente, sem retransmissão por terceiros.
- **Private** — `/msg`; enviada só na conexão já existente com o destinatário. Se o
  apelido não corresponder a um peer conectado, o erro é tratado localmente (a malha
  não faz busca via terceiros, o que violaria "sem repositório privilegiado").
- **PeerJoined** — aviso informativo enviado aos demais peers quando um novo peer
  completa o handshake, só para efeito de log ("X entrou na conversa").
- **Bye** — enviado a todas as conexões ativas quando o usuário digita `/quit`, antes
  de fechar os sockets.
- **Ping** — heartbeat periódico (ver timeouts).

## Decisões de design

### Formação da malha sem coordenador

Ao iniciar, o nó sobe um **accept loop** contínuo na porta configurada e, em
paralelo, uma **task de dial-out por peer conhecido**, com retry e backoff
exponencial (2s a 15s, com jitter) — cobre o caso comum de vários processos
subindo em momentos ligeiramente diferentes: quem ainda não conseguiu conectar
continua tentando até o outro lado subir.

**Conexão duplicada**: como não há coordenador, é normal que dois peers configurados
um para o outro disquem simultaneamente, resultando em duas conexões TCP para o
mesmo par lógico. Resolvido de forma determinística, sem coordenação extra: depois
do handshake, cada lado compara seu próprio `host:porta` de escuta (`EndpointKey`)
com o do peer remoto por ordem lexicográfica (`string.CompareOrdinal`) e aplica a
regra **"quem tem o `EndpointKey` menor mantém a conexão que ACEITOU (inbound); quem
tem o maior mantém a que DISCOU (outbound)"**. Como os dois lados calculam a mesma
comparação de forma independente, chegam à mesma decisão sobre qual das duas
conexões físicas sobrevive, cada um fechando apenas a sua própria metade da conexão
perdedora.

### Isolamento de falhas (requisito 7)

Cada `PeerConnection` tem seu **próprio** `CancellationTokenSource` (apenas
vinculado ao token global do nó, nunca o contrário) e seu próprio par de tarefas
(recepção e envio). Uma falha em qualquer uma delas — erro de socket, frame
inválido, timeout — cancela só aquele `PeerConnection`; as conexões com os demais
peers continuam intactas. Testado manualmente com `kill -9` em um dos processos:
os demais detectam a queda (via exceção de leitura) e seguem conversando
normalmente entre si (ver seção de testes).

### Anúncio de saída (requisito 8)

Saída limpa (`/quit`) e queda abrupta são tratadas pelo mesmo caminho de código: a
receive loop de um `PeerConnection` detecta a falha (frame nulo em close limpo,
exceção em close no meio de um frame, timeout de leitura ociosa, ou recebimento
explícito de `Bye`) e o nó remove aquele peer do seu dicionário local, imprime o
aviso e reativa o redial se o peer ainda está na lista configurada.

Como a malha é completa, cada nó tem conexão **direta** com todos os outros — por
isso o anúncio de saída é sempre local: quando A cai, tanto B quanto C percebem a
queda diretamente pela própria conexão com A, cada um exibindo o aviso por conta
própria. Não é necessário nem correto (nesse modelo sem coordenador) que B
retransmita "A saiu" para C — isso reintroduziria um papel de intermediário que o
enunciado proíbe.

### Backpressure / peer lento (requisito 9)

**Política adotada**: fila de envio limitada por peer
(`Channel<byte[]>`, capacidade 64, `BoundedChannelFullMode.DropOldest`) consumida
por uma send loop dedicada, mais um timeout de 5s por envio de frame; após 3
timeouts de envio consecutivos, o peer é considerado travado e desconectado como
qualquer outra queda.

Justificativa: o produtor de mensagens (broadcast a partir do console, ou
retransmissão de handshake/PeerJoined) nunca faz I/O de socket diretamente — ele só
enfileira (`TryWrite`, não bloqueante). Se um peer específico parar de consumir (não
faz mais `recv`), sua fila enche e passa a descartar as mensagens **mais antigas**
em favor das mais recentes — quem está "atrasado" perde histórico, mas quem está em
dia continua recebendo tudo, e o restante da malha nunca fica bloqueado esperando
por ele. `DropOldest` foi preferido a bloquear o produtor (que travaria a conversa
de todos por causa de um único peer lento — exatamente o que o requisito 9 proíbe) e
a descartar sempre a mensagem nova (que penalizaria justamente quem está
acompanhando). O timeout de envio + desconexão após falhas persistentes cobre o
caso extremo descrito no requisito: um participante que **parou** de consumir de
vez, não só um atraso passageiro.

### Timeouts de rede (requisito 6)

| Operação | Prazo |
|---|---|
| Connect (dial-out) | 10s |
| Handshake (`Hello`, nos dois sentidos) | 5s |
| Envio de um frame | 5s (3 falhas consecutivas → peer desconectado) |
| Leitura ociosa | heartbeat (`Ping`) a cada 15s + timeout de leitura de 45s, resetado a cada frame recebido |

O close limpo (FIN) ou abrupto (RST) do socket já é detectado sem timeout adicional
pelo próprio framing (`Frames.ReadAsync` retorna `null` ou lança). O heartbeat +
timeout de leitura ociosa cobre o caso que o SO sozinho não detecta: um peer que
trava (processo suspenso, laço infinito) sem nunca fechar o socket — sem esse
mecanismo, a leitura ficaria bloqueada indefinidamente, violando "toda operação de
rede possui prazo definido".

### Limitações conhecidas

- Apelidos duplicados não são validados entre peers — é responsabilidade de quem
  sobe a malha configurar apelidos distintos.
- Sem persistência de histórico de mensagens.
- `/list` reflete apenas os peers com conexão direta ativa neste nó (por design,
  não é um bug — ver "Anúncio de saída" acima).
- `PeerJoined` (`MeshNode.cs:201,216-218`) é um aviso informativo retransmitido por
  um peer sobre a entrada de um terceiro na malha ("[X entrou na conversa]") — não
  afeta a entrega de mensagens de chat ou privadas (sempre diretas), mas é
  tecnicamente uma exceção ao princípio de "toda comunicação é direta entre dois
  pares" da arquitetura exigida. Vale confirmar com o professor se isso é aceitável.

## Conformidade com o enunciado (auditoria)

Checklist verificado lendo o código-fonte (não apenas a documentação abaixo):

| # | Requisito | Status |
|---|---|---|
| — | Restrição tecnológica (apenas `Socket` sobre TCP, sem SignalR/gRPC/WebSocket/MQTT/libs de P2P/fila/BD) | ✅ |
| — | Nenhum nó eleito servidor/coordenador/repositório da lista de participantes | ✅ (ver ressalva do `PeerJoined` acima) |
| 1 | Porta, apelido e lista de pares via argumento ou arquivo de config | ✅ |
| 2 | Conecta aos pares conhecidos e aceita conexões dos demais, formando malha completa | ✅ |
| 3 | Mensagem digitada é entregue a todos os demais | ✅ |
| 4 | Cada mensagem exibida identifica o autor | ✅ |
| 5 | Mensagens delimitadas corretamente (sem truncar/grudar) | ✅ |
| 6 | Toda operação de rede tem prazo definido | ✅ |
| 7 | Queda de um par não derruba nem trava os demais | ✅ |
| 8 | Par que encerra (limpo ou abrupto) é removido e a saída é anunciada | ✅ |
| 9 | Peer lento não trava a conversa dos outros, política documentada e justificada | ✅ |
| 10 | `/list` e `/quit` | ✅ |
| 11 | `/msg` privado entregue direto ao destinatário | ✅ |

Todos os 11 requisitos numerados, a arquitetura exigida e a restrição de tecnologia
foram confirmados no código, não apenas descritos aqui.

## Testes automatizados

Projeto `TP01-ChatDistribuido.Tests/` (xUnit), cobrindo a lógica testável sem
depender de múltiplos processos reais:

| Arquivo | Cobre |
|---|---|
| `FramesTests.cs` | Round-trip de payload sobre um par de sockets TCP real em loopback; rajada de mensagens curtas chegando sem grude; mensagem longa sem truncar; payload acima do limite lança `ArgumentException`; leitura após close limpo retorna `null` |
| `WireMessageTests.cs` | Serialização/desserialização (round-trip) de cada tipo de mensagem do protocolo |
| `PeerConfigTests.cs` | Parsing de argumentos de linha de comando e de arquivo de configuração JSON, incluindo casos de erro (porta/nome ausente, par mal formatado) |
| `ConnectionTieBreakTests.cs` | Regra de desempate de conexão duplicada — inclusive que os dois lados de um par sempre chegam à mesma decisão sobre qual conexão sobrevive |
| `PeerIdTests.cs` | Formato do `EndpointKey` |

A lógica de desempate foi extraída para `ConnectionTieBreak.cs` (usada por
`MeshNode`) justamente para poder ser testada isoladamente, sem precisar simular
sockets ou concorrência real.

Rodar com:

```
dotnet test TP01-ChatDistribuido.Tests
```

Não há testes automatizados de ponta a ponta da malha (múltiplos processos,
handshake, backpressure, detecção de queda) — esses cenários foram validados
manualmente conforme o roteiro abaixo, já que dependem de tempo real de rede e
múltiplos processos, difíceis de tornar determinísticos em um teste unitário.

### Lacunas de cobertura conhecidas

19 testes, todos passando (`dotnet test`, ~3,2s). Portas sempre dinâmicas
(`Bind(..., 0)`), sem `Sleep`/`Delay` fixo — baixo risco de flakiness. A cobertura,
porém, se concentra na camada "pura" (framing, serialização do protocolo, parsing
de config, regra de desempate de conexão duplicada) e **não chega a `MeshNode.cs`
nem a `PeerConnection.cs`** — a orquestração real (handshake, accept/dial loop,
timeouts, backpressure, remoção de peer, broadcast/roteamento de privada) roda sem
nenhum teste automatizado, dependendo do roteiro de teste manual abaixo. Gaps
concretos, testáveis do mesmo jeito que `FramesTests.cs` já testa sockets reais:

1. **Timeouts nunca são exercitados por teste** — `ConnectTimeout` (`Connection.cs`),
   `HandshakeTimeout`, `SendTimeout` e `IdleReadTimeout` (`PeerConnection.cs`) são
   constantes fixas sem nenhum teste que force o caminho de timeout.
2. **Backpressure/consumidor lento não é testada** — o canal limitado com
   `DropOldest` e a desconexão após falhas de envio consecutivas
   (`PeerConnection.cs`) não têm teste simulando um peer que para de ler.
3. **Sem teste de dado malformado vindo de um peer** — frame com `size` inválido do
   lado do leitor (`Frames.cs:29-30`) ou JSON de `WireMessage` corrompido não são
   exercitados.

## Roteiro de teste manual

Testado localmente com 3 instâncias em `127.0.0.1:9001/9002/9003` (alice, bob,
carol):

1. Subir os 3 terminais com os comandos do exemplo acima e aguardar as mensagens de
   `conectado`. Rodar `/list` em cada terminal e confirmar que cada um vê os outros
   dois — confirma malha completa.
2. Digitar uma mensagem em qualquer terminal e confirmar que aparece nos outros dois
   com o nome do autor.
3. `/msg bob mensagem privada` em alice — confirmar que aparece em bob como
   `[privado de alice]: mensagem privada` e **não aparece** em carol.
4. Enviar uma rajada de mensagens curtas seguida de uma mensagem bem longa —
   confirmar que nada chega truncado ou grudado (framing correto).
5. Encerrar um peer com `/quit` — confirmar que os demais imprimem
   `[X saiu da conversa]` e continuam funcionando normalmente.
6. Encerrar um peer de forma abrupta (`taskkill /F /PID <pid>` no Windows, ou
   `kill -9 <pid>` em ambiente Unix/WSL) — confirmar que os demais detectam a queda
   (`[X caiu: ...]`) rapidamente e **não travam**, seguindo a conversa entre si.

Os passos 5 e 6 foram validados durante o desenvolvimento: ao matar um peer com
`kill -9`, os demais registraram a queda (erro de leitura de socket) em menos de um
segundo e continuaram trocando mensagens normalmente entre si, sem travamento.
