#!/bin/sh
# Inicia o RabbitMQ com o entrypoint padrao da imagem e, se RABBITMQ_JOIN_CLUSTER_NODE estiver
# definido, entra no cluster daquele no apos o broker local subir. Usado pelos nos 2 e 3 do
# cluster de 3 nos (o no 1 sobe normalmente, sem essa variavel).
set -e

docker-entrypoint.sh rabbitmq-server &
PID=$!

if [ -n "$RABBITMQ_JOIN_CLUSTER_NODE" ]; then
  echo "Aguardando broker local ficar pronto..."
  until rabbitmqctl -q await_startup >/dev/null 2>&1; do
    sleep 2
  done

  if ! rabbitmqctl cluster_status | grep -q "$RABBITMQ_JOIN_CLUSTER_NODE"; then
    echo "Entrando no cluster via $RABBITMQ_JOIN_CLUSTER_NODE..."
    rabbitmqctl stop_app
    rabbitmqctl join_cluster "$RABBITMQ_JOIN_CLUSTER_NODE"
    rabbitmqctl start_app
    echo "Cluster formado."
  fi
fi

wait "$PID"
