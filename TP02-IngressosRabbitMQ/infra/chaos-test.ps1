# Teste de caos (Topico 4 / Topico 2.3.3 do documento): dispara o gerador de carga contra o
# Alocador e, no meio da execucao, derruba um no do cluster RabbitMQ para simular uma falha real.
# Verifica ao final que nenhum assento foi vendido em duplicidade.
#
# Uso (a partir de TP02-IngressosRabbitMQ/): ./infra/chaos-test.ps1 [-Setor pista] [-Assentos 2] [-Solicitacoes 30] [-NoAlvo rabbitmq2]

param(
    [string]$Setor = "pista",
    [int]$Assentos = 2,
    [int]$Solicitacoes = 30,
    [string]$NoAlvo = "rabbitmq2"
)

Write-Host "Subindo infraestrutura (docker compose up -d)..."
docker compose up -d --build

Write-Host "Disparando o gerador de carga em segundo plano..."
$containerId = (docker compose run -d --rm ingressos-demonstracoes $Setor $Assentos $Solicitacoes).Trim()

Start-Sleep -Seconds 3
Write-Host "Derrubando o no '$NoAlvo' do cluster RabbitMQ no meio da carga..."
docker compose kill $NoAlvo

Write-Host "Aguardando o gerador de carga terminar..."
docker wait $containerId | Out-Null
docker logs $containerId
docker rm $containerId | Out-Null

Write-Host ""
Write-Host "Religando o no derrubado ($NoAlvo)..."
docker compose start $NoAlvo

Write-Host ""
Write-Host "Teste de caos concluido. Confira acima se o resultado foi 'nenhum overbooking'."
Write-Host "Para conferir tambem que nenhum pagamento foi cobrado duas vezes, use a IdempotencyKey"
Write-Host "unica em Pagamentos (indice unico no banco - ver Ingressos.Persistence.IngressosDbContext)."
