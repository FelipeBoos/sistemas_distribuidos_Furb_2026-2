# Demonstracao rapida de uma compra de ingresso, pelo terminal, contra CloudAMQP + Neon + Mailtrap
# (ver README, secao "Demonstracao rapida"). Sobe os servicos necessarios, cria um evento de
# demonstracao, faz o pedido, paga cada reserva e mostra o resultado. Ao final encerra todos os
# processos que ele mesmo iniciou (arvore inteira, via taskkill /T).
#
# Pre-requisito: TP02-IngressosRabbitMQ/.env preenchido e Ingressos.Migrator executado uma vez.
#
# Uso (a partir de TP02-IngressosRabbitMQ/): ./infra/demo-compra.ps1 [-Setor pista] [-Quantidade 2] [-MeiaEntrada]

param(
    [string]$Setor = "pista",
    [int]$Quantidade = 2,
    [switch]$MeiaEntrada,
    [string]$ApiUrl = "http://localhost:5224",
    # O Antifraude sorteia risco alto com esta taxa (ANTIFRAUDE_TAXA_RISCO_ALTO do .env). Aqui fica
    # em 0 para a demonstracao nao falhar por sorteio; passe outro valor para ver recusas.
    [string]$TaxaRiscoAlto = "0"
)

$ErrorActionPreference = "Stop"
$raiz = Split-Path -Parent $PSScriptRoot
$solucao = Join-Path (Split-Path -Parent $raiz) "sistemas_distribuidos_Furb_2026-2.slnx"

# Servicos necessarios para o fluxo feliz. Um Alocador por setor, porque cada setor tem a sua fila.
$servicos = @(
    "Ingressos.ApiVendas", "Ingressos.Antifraude", "Ingressos.ServicoPagamento",
    "Ingressos.EmissorIngresso", "Ingressos.Notificador", "Ingressos.OutboxRelay"
)
$alocadoresPorSetor = @("pista", "cadeira", "camarote")

$processos = @()

# Saida de cada servico vai para arquivos em %TEMP%\ingressos-demo (ajuda quando algo falha).
$logDir = Join-Path $env:TEMP "ingressos-demo"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

function Iniciar-Projeto([string]$nome, [string]$setorAlocador) {
    $caminho = Join-Path $raiz $nome
    $sufixo = ""
    if ($setorAlocador) { $sufixo = "-$setorAlocador"; $env:ALOCADOR_SETOR = $setorAlocador }
    $base = Join-Path $logDir "$nome$sufixo"
    # Caminho entre aspas dentro de uma unica string: -ArgumentList com array quebra com espacos.
    $proc = Start-Process dotnet -ArgumentList "run --no-build --project `"$caminho`"" -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput "$base.out.log" -RedirectStandardError "$base.err.log"
    if ($setorAlocador) { Remove-Item Env:ALOCADOR_SETOR -ErrorAction SilentlyContinue }
    return $proc
}

# Devolve as reservas do usuario como itens individuais. Nao usar @(Invoke-RestMethod ...): no
# PowerShell 5.1, o pipeline dentro de @() deixa o array JSON como um unico item.
function Obter-Reservas([string]$uid) {
    $json = (Invoke-WebRequest -Uri "$ApiUrl/reservas?usuarioId=$uid" -UseBasicParsing).Content
    $lista = ConvertFrom-Json $json
    return $lista
}

function Mostrar-Logs {
    Write-Host ""
    Write-Host "Ultimas linhas dos logs (pasta: $logDir):" -ForegroundColor Yellow
    Get-ChildItem -Path $logDir -Filter "*.log" | Where-Object { $_.Length -gt 0 } | ForEach-Object {
        Write-Host "--- $($_.Name)"
        Get-Content $_.FullName -Tail 15 | ForEach-Object { Write-Host "  $_" }
    }
}

try {
    Write-Host "Compilando a solucao (uma vez, para os processos subirem sem recompilar)..."
    dotnet build "$solucao" --nologo -v:quiet
    if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar a solucao." }

    Write-Host "Subindo os servicos..."
    $env:ANTIFRAUDE_TAXA_RISCO_ALTO = $TaxaRiscoAlto
    foreach ($nome in $servicos) { $processos += Iniciar-Projeto $nome $null }
    foreach ($s in $alocadoresPorSetor) { $processos += Iniciar-Projeto "Ingressos.Alocador" $s }

    Write-Host "Aguardando a API responder em $ApiUrl ..."
    $fim = (Get-Date).AddSeconds(90)
    do {
        Start-Sleep -Seconds 2
        try { Invoke-RestMethod -Uri "$ApiUrl/saude" -TimeoutSec 3 | Out-Null; $pronta = $true } catch { $pronta = $false }
    } until ($pronta -or (Get-Date) -gt $fim)
    if (-not $pronta) { throw "A API nao respondeu em $ApiUrl. Confira a porta no console do Ingressos.ApiVendas." }
    Start-Sleep -Seconds 5  # tempo para os consumidores declararem as filas e se conectarem

    Write-Host "Criando um evento de demonstracao (setor '$Setor')..."
    $semeadura = & dotnet run --no-build --project (Join-Path $raiz "Ingressos.Demonstracoes") -- $Setor 4 0 2>&1
    $linhaSetor = $semeadura | Where-Object { $_ -match "SetorId=" } | Select-Object -First 1
    if (-not $linhaSetor -or $linhaSetor -notmatch "SetorId=([0-9a-fA-F-]{36})") { throw "Nao foi possivel obter o SetorId da semeadura." }
    $setorId = $Matches[1]

    $usuarioId = [guid]::NewGuid().ToString()
    $corpoCompra = @{ usuarioId = $usuarioId; setorId = $setorId; quantidadeIngressos = $Quantidade; meiaEntrada = [bool]$MeiaEntrada } | ConvertTo-Json
    Write-Host "Pedido: $Quantidade ingresso(s) para o setor '$Setor'..."
    Invoke-RestMethod -Uri "$ApiUrl/compras" -Method Post -ContentType "application/json" -Body $corpoCompra | Out-Null

    Write-Host "Aguardando as reservas (uma por ingresso)..."
    $reservas = @()
    $fim = (Get-Date).AddSeconds(60)
    do {
        Start-Sleep -Seconds 1
        $reservas = @(Obter-Reservas $usuarioId)
        Write-Host "  reservas visiveis: $($reservas.Count) de $Quantidade"
    } until ($reservas.Count -ge $Quantidade -or (Get-Date) -gt $fim)
    if ($reservas.Count -lt $Quantidade) {
        throw "Apenas $($reservas.Count) de $Quantidade reserva(s) criada(s). Veja o log do Ingressos.Alocador (motivo da rejeicao)."
    }

    Write-Host "Pagando cada reserva..."
    foreach ($reserva in $reservas) {
        $corpoPagamento = @{ reservaId = $reserva.reservaId; idempotencyKey = [guid]::NewGuid().ToString() } | ConvertTo-Json
        Invoke-RestMethod -Uri "$ApiUrl/pagamentos" -Method Post -ContentType "application/json" -Body $corpoPagamento | Out-Null
    }

    Write-Host "Aguardando aprovacao e emissao dos ingressos..."
    $fim = (Get-Date).AddSeconds(60)
    do {
        Start-Sleep -Seconds 1
        $reservas = @(Obter-Reservas $usuarioId)
        $emitidos = @($reservas | Where-Object { $_.qrCode })
    } until ($emitidos.Count -ge $Quantidade -or (Get-Date) -gt $fim)

    Write-Host ""
    Write-Host "Resultado:"
    $reservas | Select-Object @{n="Assento";e={$_.assento}}, @{n="Reserva";e={$_.status}}, @{n="Pagamento";e={$_.statusPagamento}}, @{n="Valor";e={"{0:N2}" -f $_.valor}}, @{n="QR code";e={$_.qrCode}} | Format-Table -AutoSize | Out-String | Write-Host

    # Os e-mails saem pelo Notificador (um por vez, com novas tentativas por causa do limite do
    # Mailtrap). Espera-se o envio terminar antes de encerrar os servicos.
    Write-Host "Aguardando o envio dos e-mails (Mailtrap limita a taxa de envio)..."
    Start-Sleep -Seconds 25

    if ($emitidos.Count -lt $Quantidade) {
        Write-Host "Atencao: nem todos os ingressos foram emitidos. Pagamentos recusados sao reenviados automaticamente (5 s, 30 s, 2 min)."
    } else {
        Write-Host "Demonstracao concluida: $($emitidos.Count) ingresso(s) emitido(s). Confira o e-mail no Mailtrap."
    }
}
catch {
    Write-Host ""
    Write-Host "ERRO: $($_.Exception.Message)" -ForegroundColor Red
    Mostrar-Logs
    throw
}
finally {
    Write-Host ""
    Write-Host "Encerrando os processos iniciados por este script..."
    foreach ($proc in $processos) {
        if ($proc -and -not $proc.HasExited) {
            # /T encerra tambem o processo filho do "dotnet run" (o servico em si).
            taskkill /PID $proc.Id /T /F | Out-Null
        }
    }
}
