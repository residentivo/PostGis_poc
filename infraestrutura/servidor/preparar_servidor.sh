#!/usr/bin/env bash
# =====================================================================
# infraestrutura/servidor/preparar_servidor.sh - roda UMA vez na VM
# (sudo), antes da primeira implantacao pelo Jenkins.
#
#   scp infraestrutura/servidor/preparar_servidor.sh azureuser@<vm>:
#   ssh azureuser@<vm> 'sudo bash preparar_servidor.sh [dominio]'
#   ssh azureuser@<vm> 'sudo HABILITAR_ESCRITA=1 bash preparar_servidor.sh'  (liga PUT/POST)
#
# Cria /opt/api_mapas com:
#   segredos/pg_senha_casaos|leitura|escrita  senhas aleatorias (uid 999 = postgres)
#   segredos/api.env                          PG_USER/PG_PASSWORD/PG_ESCRITA_* da API
#   segredos/htpasswd + basic_auth.txt        usuario do nginx (texto claro so para o operador)
#   certs/fullchain.pem + privkey.pem         autoassinado, se nao houver certificado
#
# Idempotente: o que ja existe NAO e sobrescrito (trocar senha do banco
# depois da primeira subida e feito com \password, ver README).
# Nenhuma senha e impressa na tela.
# =====================================================================
set -Eeuo pipefail

BASE=/opt/api_mapas
SEGREDOS=$BASE/segredos
CERTS=$BASE/certs
DOMINIO=${1:-$(hostname -f)}
USUARIO_API=${USUARIO_API:-mapas}
UID_POSTGRES=999   # usuario postgres da imagem postgis/postgis (Debian)
UID_NGINX=101      # usuario nginx da imagem nginx:alpine

if [[ $EUID -ne 0 ]]; then
    echo "Rode com sudo." >&2
    exit 1
fi

command -v docker >/dev/null || { echo "docker nao instalado (ver azure/cloud-init.yaml)." >&2; exit 1; }
command -v openssl >/dev/null || { echo "openssl nao instalado." >&2; exit 1; }

umask 077
install -d -m 755 "$BASE" "$BASE/nginx"
# O Jenkins copia docker-compose.yml, implantar.sh e nginx/ por scp, sem sudo.
chown "${SUDO_USER:-root}:" "$BASE" "$BASE/nginx"
install -d -m 711 "$SEGREDOS"
install -d -m 700 "$CERTS"

# Senha aleatoria sem caracteres que atrapalhem connection string ou env_file.
gerar_senha() { openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | head -c 32; }

criar_senha() {
    local arquivo=$SEGREDOS/$1
    if [[ -s $arquivo ]]; then
        echo "   $1 ja existe; mantido."
    else
        gerar_senha > "$arquivo"
        echo "   $1 criado."
    fi
    chown "$UID_POSTGRES:$UID_POSTGRES" "$arquivo"
    chmod 400 "$arquivo"
}

echo ">> Senhas do banco"
criar_senha pg_senha_casaos
criar_senha pg_senha_leitura
criar_senha pg_senha_escrita

if cmp -s "$SEGREDOS/pg_senha_leitura" "$SEGREDOS/pg_senha_escrita"; then
    echo "pg_senha_leitura e pg_senha_escrita iguais (RP-8): apague uma e rode de novo." >&2
    exit 1
fi

# U-1: a escrita sem autenticacao da API nao deve ser exposta por proxy. Aqui
# ela fica atras do nginx com TLS + autenticacao basica, mas so e ligada com
# HABILITAR_ESCRITA=1 (sem PG_ESCRITA_*, PUT responde 405 e POST 404).
echo ">> api.env"
if [[ -s $SEGREDOS/api.env ]]; then
    echo "   api.env ja existe; mantido."
else
    {
        echo "PG_USER=api_mapas_leitura"
        echo "PG_PASSWORD=$(cat "$SEGREDOS/pg_senha_leitura")"
        if [[ ${HABILITAR_ESCRITA:-0} == 1 ]]; then
            echo "PG_ESCRITA_USER=api_mapas_escrita"
            echo "PG_ESCRITA_PASSWORD=$(cat "$SEGREDOS/pg_senha_escrita")"
        fi
    } > "$SEGREDOS/api.env"
    if [[ ${HABILITAR_ESCRITA:-0} == 1 ]]; then
        echo "   api.env criado com escrita (PUT/POST) habilitada."
    else
        echo "   api.env criado so com leitura (HABILITAR_ESCRITA=1 liga PUT/POST)."
    fi
fi
chown root:root "$SEGREDOS/api.env"
chmod 600 "$SEGREDOS/api.env"

echo ">> Usuario do nginx (autenticacao basica)"
if [[ -s $SEGREDOS/htpasswd ]]; then
    echo "   htpasswd ja existe; mantido."
else
    senha_api=$(gerar_senha)
    printf '%s:%s\n' "$USUARIO_API" "$(openssl passwd -6 -stdin <<< "$senha_api")" > "$SEGREDOS/htpasswd"
    printf '%s:%s\n' "$USUARIO_API" "$senha_api" > "$SEGREDOS/basic_auth.txt"
    unset senha_api
    echo "   htpasswd criado; credencial em $SEGREDOS/basic_auth.txt (so root le)."
fi
chown "$UID_NGINX:$UID_NGINX" "$SEGREDOS/htpasswd"
chmod 400 "$SEGREDOS/htpasswd"
[[ -f $SEGREDOS/basic_auth.txt ]] && chmod 600 "$SEGREDOS/basic_auth.txt"

echo ">> Certificado do nginx"
if [[ -s $CERTS/fullchain.pem && -s $CERTS/privkey.pem ]]; then
    echo "   certificado ja existe; mantido."
else
    openssl req -new -x509 -nodes -days 365 -newkey rsa:3072 \
        -subj "/CN=$DOMINIO" -addext "subjectAltName=DNS:$DOMINIO" \
        -keyout "$CERTS/privkey.pem" -out "$CERTS/fullchain.pem" 2>/dev/null
    echo "   autoassinado para $DOMINIO (trocar por um certificado valido, ver README)."
fi
chmod 600 "$CERTS/privkey.pem"
chmod 644 "$CERTS/fullchain.pem"

echo ">> Pronto. Proximo passo: rodar o pipeline no Jenkins."
