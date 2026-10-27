#!/usr/bin/env bash
# =====================================================================
# infraestrutura/servidor/implantar.sh - roda na VM, chamado pelo Jenkins
# (estagio "Implantar") depois de copiar docker-compose.yml e nginx/.
#
#   echo "$ACR_SENHA" | bash /opt/api_mapas/implantar.sh \
#       <registro> <api_tag> <postgis_tag> <acr_usuario>
#
# A senha do ACR chega pela entrada padrao (nunca por argumento).
# 1. docker login no ACR; 2. grava o .env do compose; 3. pull + up;
# 4. teste de fumaca na API (direto em 127.0.0.1:5080, por dentro do nginx);
# 5. docker logout.
# =====================================================================
set -Eeuo pipefail

REGISTRO=${1:?registro}
API_TAG=${2:?api_tag}
POSTGIS_TAG=${3:?postgis_tag}
ACR_USUARIO=${4:?acr_usuario}
BASE=/opt/api_mapas

cd "$BASE"
for item in docker-compose.yml nginx/api_mapas.conf segredos/api.env segredos/htpasswd certs/fullchain.pem; do
    [[ -e $item ]] || { echo "Falta $BASE/$item (rodou o preparar_servidor.sh?)." >&2; exit 1; }
done

trap 'docker logout "$REGISTRO" >/dev/null 2>&1 || true' EXIT
docker login "$REGISTRO" -u "$ACR_USUARIO" --password-stdin >/dev/null

# Mantem os limites/opcoes ja definidos no .env e troca so registro e tags.
touch .env
grep -v -E '^(REGISTRO|API_TAG|POSTGIS_TAG)=' .env > .env.novo || true
{
    echo "REGISTRO=$REGISTRO"
    echo "API_TAG=$API_TAG"
    echo "POSTGIS_TAG=$POSTGIS_TAG"
} >> .env.novo
mv .env.novo .env
chmod 644 .env

echo ">> Baixando imagens"
docker compose pull

echo ">> Subindo postgis (so e recriado se a imagem mudou)"
docker compose up -d --wait --wait-timeout 300 postgis

echo ">> Recriando nginx e api (config do nginx e montada por arquivo)"
docker compose up -d --force-recreate --remove-orphans nginx api

echo ">> Teste de fumaca"
codigo=""
for tentativa in $(seq 1 30); do
    codigo=$(docker compose exec -T nginx sh -c \
        "wget -S -q -O /dev/null http://127.0.0.1:5080/api/mapas/1/graphy 2>&1 | awk '/HTTP\//{print \$2}' | tail -n 1" || true)
    if [[ $codigo == 200 || $codigo == 404 ]]; then
        echo "   GET /api/mapas/1/graphy -> $codigo (ok)"
        break
    fi
    sleep 2
done
if [[ $codigo != 200 && $codigo != 404 ]]; then
    echo "   API nao respondeu como esperado (ultimo codigo: ${codigo:-nenhum})." >&2
    docker compose logs --tail 80 api >&2
    exit 1
fi

if docker compose logs api 2>&1 | grep -q "Escrita (PUT e POST) DESLIGADA"; then
    echo "   Escrita (PUT/POST) desligada (padrao sem HABILITAR_ESCRITA=1; motivo em 'docker compose logs api')."
fi

docker image prune -f >/dev/null
echo ">> Implantado: api_mapas:$API_TAG, postgis_mapas:$POSTGIS_TAG"
