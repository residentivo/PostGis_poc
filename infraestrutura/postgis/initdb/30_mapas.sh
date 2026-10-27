#!/usr/bin/env bash
# =====================================================================
# 30_mapas.sh - primeira subida: aplica os scripts de database/ na ordem
# do PU-DBA (database/README.md), define as senhas das roles da API a
# partir dos secrets do compose e instala o pg_hba.conf restrito.
#
# Ordem: mapas.sql -> migracao_mapas_pk.sql (-1) -> validar_check_mapas.sql
#        (-1) -> roles.sql (com a secao 7, SSL) -> senhas -> pg_hba.conf.
# Qualquer erro aborta a inicializacao do container (o volume fica
# incompleto: apague-o e suba de novo).
# =====================================================================
set -Eeuo pipefail

SQL=/opt/mapas/sql
SEGREDOS=/run/secrets

if [[ "$POSTGRES_DB" != "casaos" ]]; then
    echo "30_mapas: POSTGRES_DB deve ser casaos (os scripts de database/ exigem)." >&2
    exit 1
fi

for arquivo in pg_senha_leitura pg_senha_escrita; do
    if [[ ! -s "$SEGREDOS/$arquivo" ]]; then
        echo "30_mapas: secret $arquivo ausente ou vazio em $SEGREDOS." >&2
        exit 1
    fi
done

psql_casaos() {
    psql -X -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" "$@"
}

echo ">> 30_mapas: tabela public.mapas"
psql_casaos -f "$SQL/mapas.sql"

echo ">> 30_mapas: migracao para PRIMARY KEY"
psql_casaos -1 -f "$SQL/migracao_mapas_pk.sql"

echo ">> 30_mapas: VALIDATE do CHECK mapas_nao_vazia"
psql_casaos -1 -f "$SQL/validar_check_mapas.sql"

echo ">> 30_mapas: roles e SSL (roles.sql)"
psql_casaos -f "$SQL/roles.sql"

# Senhas: o psql le os arquivos por crase (\set), entao o valor nao aparece
# em argumento de processo nem no historico. log_statement fica no padrao
# (none), entao o ALTER ROLE nao vai para o log do servidor.
echo ">> 30_mapas: senhas de api_mapas_leitura e api_mapas_escrita"
psql_casaos <<'EOSQL'
\set senha_leitura `cat /run/secrets/pg_senha_leitura`
\set senha_escrita `cat /run/secrets/pg_senha_escrita`
SET password_encryption = 'scram-sha-256';
ALTER ROLE api_mapas_leitura PASSWORD :'senha_leitura';
ALTER ROLE api_mapas_escrita PASSWORD :'senha_escrita';
\unset senha_leitura
\unset senha_escrita
EOSQL

echo ">> 30_mapas: pg_hba.conf restrito"
cp /opt/mapas/pg_hba.conf "$PGDATA/pg_hba.conf"
chmod 600 "$PGDATA/pg_hba.conf"

echo ">> 30_mapas: concluido."
