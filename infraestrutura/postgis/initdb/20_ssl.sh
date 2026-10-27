#!/usr/bin/env bash
# =====================================================================
# 20_ssl.sh - primeira subida: certificado autoassinado do servidor no
# PGDATA (mesmo papel do database/gerar_certificado_ssl.sh no CasaOS).
# Pre-requisito da secao 7 do roles.sql (30_mapas.sh), que liga o SSL.
# Roda como usuario postgres, entao dono e permissoes ja ficam corretos.
# =====================================================================
set -Eeuo pipefail

cd "$PGDATA"
if [[ -s server.crt && -s server.key ]]; then
    echo ">> 20_ssl: server.crt/server.key ja existem; nada a fazer."
    exit 0
fi

umask 077
openssl req -new -x509 -nodes -days 825 \
    -newkey rsa:3072 \
    -subj "/CN=postgis" \
    -addext "subjectAltName=DNS:postgis,DNS:localhost" \
    -keyout server.key -out server.crt
chmod 600 server.key
chmod 644 server.crt
echo ">> 20_ssl: certificado gerado em $PGDATA (validade 825 dias)."
