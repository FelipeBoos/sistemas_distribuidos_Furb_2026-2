#!/bin/sh
# Equivalente POSIX de chaos-test.ps1 - ver esse arquivo para a explicacao completa.
# Uso (a partir de TP02-IngressosRabbitMQ/): ./infra/chaos-test.sh [setor] [assentos] [solicitacoes] [no-alvo]
set -e

SETOR="${1:-pista}"
ASSENTOS="${2:-2}"
SOLICITACOES="${3:-30}"
NO_ALVO="${4:-rabbitmq2}"

echo "Subindo infraestrutura (docker compose up -d)..."
docker compose up -d --build

echo "Disparando o gerador de carga em segundo plano..."
CONTAINER_ID=$(docker compose run -d --rm ingressos-demonstracoes "$SETOR" "$ASSENTOS" "$SOLICITACOES")

sleep 3
echo "Derrubando o no '$NO_ALVO' do cluster RabbitMQ no meio da carga..."
docker compose kill "$NO_ALVO"

echo "Aguardando o gerador de carga terminar..."
docker wait "$CONTAINER_ID" >/dev/null
docker logs "$CONTAINER_ID"
docker rm "$CONTAINER_ID" >/dev/null

echo ""
echo "Religando o no derrubado ($NO_ALVO)..."
docker compose start "$NO_ALVO"

echo ""
echo "Teste de caos concluido. Confira acima se o resultado foi 'nenhum overbooking'."
