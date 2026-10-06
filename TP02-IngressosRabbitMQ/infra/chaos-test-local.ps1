# Teste de caos (Topico 4 / Topico 2.3.3 do documento) adaptado para o caminho via nuvem
# (CloudAMQP + Neon): como o plano gratuito do CloudAMQP e uma instancia unica compartilhada,
# nao da pra derrubar um "no" do broker como no teste original via Docker (infra/chaos-test.ps1,
# mantido como referencia da abordagem descartada - ver docs/guia-apresentacao.md).
#
# Este teste simula a falha no lado da aplicacao: sobe 2 instancias locais do Alocador para o
# mesmo setor (Single Active Consumer decide qual delas processa), derruba uma durante a carga, e
# confirma que a outra assume sem overbooking nem venda duplicada.
#
# Pre-requisito: TP02-IngressosRabbitMQ/.env preenchido com RABBITMQ_URL e
# POSTGRES_CONNECTION_STRING apontando para o CloudAMQP/Neon, e Ingressos.Migrator ja executado
# uma vez (schema aplicado).
#
# Uso (a partir de TP02-IngressosRabbitMQ/): ./infra/chaos-test-local.ps1 [-Setor pista] [-Assentos 2] [-Solicitacoes 30]

param(
    [string]$Setor = "pista",
    [int]$Assentos = 2,
    [int]$Solicitacoes = 30
)

$raiz = Split-Path -Parent $PSScriptRoot

Write-Host "Subindo 2 instancias locais do Alocador para o setor '$Setor' (Single Active Consumer decide qual processa)..."
$env:ALOCADOR_SETOR = $Setor

# Caminhos com espaco (ex.: "Area de Trabalho") quebram o particionamento automatico de
# -ArgumentList quando o array mistura strings e numeros - por isso cada comando e montado como
# uma unica string, com o caminho entre aspas duplas explicitas.
$caminhoAlocador = Join-Path $raiz "Ingressos.Alocador"
$caminhoDemonstracoes = Join-Path $raiz "Ingressos.Demonstracoes"

$alocador1 = Start-Process dotnet -ArgumentList "run --project `"$caminhoAlocador`"" -PassThru -WindowStyle Minimized
$alocador2 = Start-Process dotnet -ArgumentList "run --project `"$caminhoAlocador`"" -PassThru -WindowStyle Minimized

Write-Host "Aguardando os dois processos conectarem ao RabbitMQ..."
Start-Sleep -Seconds 8

Write-Host "Disparando o gerador de carga ($Solicitacoes solicitacoes concorrentes)..."
$demonstracoes = Start-Process dotnet -ArgumentList "run --project `"$caminhoDemonstracoes`" -- $Setor $Assentos $Solicitacoes" -PassThru -NoNewWindow -Wait:$false

Start-Sleep -Seconds 3
Write-Host "Derrubando uma das instancias do Alocador (PID $($alocador1.Id)) no meio da carga..."
Stop-Process -Id $alocador1.Id -Force -ErrorAction SilentlyContinue

Write-Host "Aguardando o gerador de carga terminar (ele mesmo imprime o resultado da verificacao)..."
Wait-Process -Id $demonstracoes.Id -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Encerrando a instancia restante do Alocador (PID $($alocador2.Id))..."
Stop-Process -Id $alocador2.Id -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Teste de caos (local) concluido. Confira acima se o resultado foi 'nenhum overbooking' -"
Write-Host "isso confirma que a instancia restante do Alocador assumiu o processamento sem duplicar reservas."
