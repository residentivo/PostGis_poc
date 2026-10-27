#!/usr/bin/env bash
# =====================================================================
# database/backup_mapas.sh  (U-4 / RP-16 / S-5 - P3.6)
#
# Backup de public.mapas com pg_dump -t public.mapas, em arquivo datado.
# Obrigatorio ANTES de definir PG_ESCRITA_* na api_mapas (U-4): o PUT
# apaga e regrava linhas, e o DELETE da role api_mapas_escrita vale para
# a tabela inteira (risco aceito em U-2). Quem roda e o usuario/DBA; os
# agentes NAO executam este script.
# Roda no Git Bash do Windows e no bash do Linux. No PowerShell, use
# database/backup_mapas.ps1 (mesmo comportamento, sem --container).
#
# NENHUMA CREDENCIAL NESTE SCRIPT. Nao existe opcao de senha. A senha vem,
# nesta ordem, de: PGPASSWORD (variavel de ambiente da sessao), arquivo
# .pgpass (Windows: %APPDATA%\postgresql\pgpass.conf) ou do prompt do
# pg_dump. Nunca escreva a senha na linha de comando nem em arquivo
# versionado.
#
# Uso:
#   Modo TCP (pg_dump local, conexao SSL ao servidor):
#     bash database/backup_mapas.sh --host 192.168.168.106 --user casaos
#   Modo container (no HOST do servidor, socket local, sem senha):
#     bash database/backup_mapas.sh --container <nome-ou-id>
#
# Opcoes (cada uma tambem pode vir da variavel indicada):
#   --host <h>        servidor (PGHOST). Obrigatorio no modo TCP.
#   --port <p>        porta (PGPORT, padrao 5432)
#   --db <base>       base (PGDATABASE, padrao casaos)
#   --user <role>     role (PGUSER). Obrigatorio no modo TCP; no modo
#                     container o padrao e casaos.
#   --container <c>   usa "docker exec <c> pg_dump" (DOCKER="sudo docker"
#                     se precisar de sudo). Ignora --host/--port.
#   --out-dir <dir>   destino (BACKUP_DIR). Deve ficar FORA do repositorio
#                     e do OneDrive e ser DEDICADO (C7): um diretorio que
#                     ainda nao existe (o script cria e aplica chmod 700)
#                     ou um diretorio vazio chamado backups_postgis. Outro
#                     diretorio existente faz o script abortar sem mexer na
#                     permissao dele. Sem --out-dir: cria a subpasta nova
#                     ~/backups_postgis/AAAAMMDD_HHMMSS a cada execucao.
#   -h | --help       esta ajuda
#
# Role: use casaos (dono da tabela). As roles da API e de teste NAO
# servem: o pg_dump le a sequencia da identity (mapas_id_seq) para gravar
# o setval, e nenhuma delas tem privilegio na sequencia (de proposito,
# R-3). Pelo pg_hba, casaos so entra local; nesse caso use o modo
# container no host do servidor, ou rode o modo TCP a partir de uma
# origem liberada para casaos.
#
# SSL (modo TCP): PGSSLMODE padrao "require". Aceita tambem verify-ca e
# verify-full (com PGSSLROOTCERT apontando o ca.crt). disable, allow e
# prefer sao recusados.
#
# Saida: <out-dir>/mapas_backup_AAAAMMDD_HHMMSS.sql (formato texto do
# pg_dump, permissao so do dono). O script sai com codigo diferente de
# zero se o dump falhar, ficar vazio, nao tiver o COPY de public.mapas ou
# nao tiver o rodape "dump complete"; nesse caso o arquivo parcial e
# removido. Mostra a quantidade de linhas e o SHA-256 do arquivo; compare
# com SELECT count(*) FROM public.mapas;
#
# ---------------------------------------------------------------------
# RESTAURACAO (NAO executada pelo script; so com o usuario de acordo)
# ---------------------------------------------------------------------
# Substitui a tabela INTEIRA pelo conteudo do backup. Antes, faca um
# backup do estado atual (rode este script de novo). Tudo numa unica
# transacao: se qualquer comando falhar, nada muda.
# Use psql da MESMA versao do pg_dump, ou mais nova (dumps recentes trazem
# \restrict, que psql antigo nao reconhece).
#   Modo container (no host do servidor):
#     sudo docker exec -i <container> psql -U casaos -d casaos \
#         -v ON_ERROR_STOP=1 --single-transaction \
#         -c 'DROP TABLE public.mapas;' -f - < ~/backups_postgis/AAAAMMDD_HHMMSS/mapas_backup_AAAAMMDD_HHMMSS.sql
#   Modo TCP:
#     psql "host=<host> dbname=casaos user=casaos sslmode=require" \
#         -v ON_ERROR_STOP=1 --single-transaction \
#         -c 'DROP TABLE public.mapas;' -f ~/backups_postgis/AAAAMMDD_HHMMSS/mapas_backup_AAAAMMDD_HHMMSS.sql
# O DROP remove tambem a sequencia da identity; o dump recria a tabela, a
# identity com o valor atual (setval), os dados e os GRANTs. Depois,
# rode database/roles.sql de novo (psql -v pular_ssl=1) para reaplicar
# as guardas e conferir os privilegios.
# =====================================================================
set -euo pipefail
umask 077

DOCKER="${DOCKER:-docker}"
PG_HOST="${PGHOST:-}"
PG_PORT="${PGPORT:-5432}"
PG_DB="${PGDATABASE:-casaos}"
PG_ROLE="${PGUSER:-}"
CONTAINER=""
OUT_DIR="${BACKUP_DIR:-}"
CARIMBO="$(date +%Y%m%d_%H%M%S)"

erro() { echo "ERRO: $*" >&2; exit 1; }
info() { echo ">> $*"; }
ajuda() { awk 'NR > 1 && /^set -euo/ { exit } NR > 1' "$0" | sed 's/^# \{0,1\}//'; exit 0; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --host)      PG_HOST="${2:-}"; shift 2 ;;
        --port)      PG_PORT="${2:-}"; shift 2 ;;
        --db)        PG_DB="${2:-}"; shift 2 ;;
        --user)      PG_ROLE="${2:-}"; shift 2 ;;
        --container) CONTAINER="${2:-}"; shift 2 ;;
        --out-dir)   OUT_DIR="${2:-}"; shift 2 ;;
        -h|--help)   ajuda ;;
        *)           erro "opcao desconhecida: $1 (use --help). Nao ha opcao de senha: use PGPASSWORD, .pgpass ou o prompt." ;;
    esac
done

# ---------------------------------------------------------------------
# Validacoes (sem nada que venha do usuario ir para eval/shell)
# ---------------------------------------------------------------------
[[ "$PG_DB" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]] || erro "base invalida: $PG_DB"
[[ "$PG_PORT" =~ ^[0-9]{1,5}$ ]] && (( PG_PORT >= 1 && PG_PORT <= 65535 )) || erro "porta invalida: $PG_PORT"

if [[ -n "$CONTAINER" ]]; then
    MODO="container"
    [[ "$CONTAINER" =~ ^[A-Za-z0-9][A-Za-z0-9_.-]*$ ]] || erro "nome de container invalido: $CONTAINER"
    PG_ROLE="${PG_ROLE:-casaos}"
else
    MODO="tcp"
    [[ -n "$PG_HOST" ]] || erro "informe --host (ou PGHOST), ou use --container no host do servidor"
    [[ "$PG_HOST" =~ ^[A-Za-z0-9]([A-Za-z0-9.:-]*[A-Za-z0-9])?$ ]] || erro "host invalido: $PG_HOST"
    [[ -n "$PG_ROLE" ]] || erro "informe --user (ou PGUSER); use casaos (ver --help)"
    SSLMODE="${PGSSLMODE:-require}"
    case "$SSLMODE" in
        require|verify-ca|verify-full) ;;
        *) erro "PGSSLMODE=$SSLMODE recusado: use require, verify-ca ou verify-full" ;;
    esac
    export PGSSLMODE="$SSLMODE"
fi
[[ "$PG_ROLE" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]] || erro "role invalida: $PG_ROLE"

case "$PG_ROLE" in
    api_mapas_leitura|api_mapas_escrita|mapas_teste)
        erro "a role $PG_ROLE nao tem privilegio na sequencia da identity e o pg_dump falharia; use casaos" ;;
esac

if [[ "$MODO" == "tcp" ]]; then
    command -v pg_dump >/dev/null 2>&1 || erro "pg_dump nao encontrado no PATH (instale o cliente PostgreSQL 17 ou mais novo)"
else
    $DOCKER inspect --type container "$CONTAINER" >/dev/null 2>&1 || erro "container '$CONTAINER' nao encontrado (precisa de sudo? DOCKER=\"sudo docker\")"
fi

# ---------------------------------------------------------------------
# Diretorio de saida: fora do OneDrive e de repositorio git, do usuario
# atual, permissao 700 (mesma regra de gerar_certificado_ssl.sh).
# ---------------------------------------------------------------------
dentro_onedrive() {
    local caminho="$1" r=1
    shopt -s nocasematch
    if [[ "$caminho" =~ /OneDrive(\ -[^/]*)?(/|$) ]]; then
        r=0
    fi
    shopt -u nocasematch
    return $r
}

# C7 (SecBrain, P5): a permissao do diretorio so e restringida (chmod 700)
# quando o PROPRIO script o criou, ou quando ele ja existe, esta VAZIO e se
# chama backups_postgis. Em qualquer outro caso o script aborta, para nunca
# mudar a permissao de um diretorio de uso geral (ex.: --out-dir ~).
# Sem --out-dir/BACKUP_DIR, cada execucao cria uma subpasta nova
# ~/backups_postgis/<AAAAMMDD_HHMMSS> (criada pelo script; a pasta-mae,
# se for criada agora, nasce 700 pelo umask 077 e nunca recebe chmod).
[[ -n "$OUT_DIR" ]] || OUT_DIR="${HOME}/backups_postgis/${CARIMBO}"
[[ "$OUT_DIR" == /* ]] || OUT_DIR="$PWD/$OUT_DIR"
dentro_onedrive "$OUT_DIR" && erro "--out-dir esta dentro do OneDrive ($OUT_DIR); use um diretorio local"

CRIADO_PELO_SCRIPT=0
if [[ -e "$OUT_DIR" || -L "$OUT_DIR" ]]; then
    [[ -d "$OUT_DIR" && ! -L "$OUT_DIR" ]] || erro "--out-dir ($OUT_DIR) existe e nao e um diretorio comum; use um diretorio dedicado"
else
    mkdir -p -- "$OUT_DIR"
    CRIADO_PELO_SCRIPT=1
fi
# Se alguma checagem abaixo falhar, remove o diretorio que acabou de criar
# (rmdir: so se ainda estiver vazio).
OUT_DIR_CRIADO="$OUT_DIR"
desfazer_dir() { if (( CRIADO_PELO_SCRIPT )); then rmdir -- "$OUT_DIR_CRIADO" 2>/dev/null || true; fi; }
trap desfazer_dir EXIT

OUT_DIR="$(cd -- "$OUT_DIR" && pwd -P)"
OUT_DIR_CRIADO="$OUT_DIR"
dentro_onedrive "$OUT_DIR" && erro "--out-dir esta dentro do OneDrive ($OUT_DIR); use um diretorio local"
if command -v git >/dev/null 2>&1 && git -C "$OUT_DIR" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    erro "--out-dir esta dentro de um repositorio git ($OUT_DIR); use um diretorio fora do repo"
fi
[[ "$(stat -c %u -- "$OUT_DIR")" == "$(id -u)" ]] \
    || erro "--out-dir ($OUT_DIR) nao pertence ao usuario atual (uid $(id -u)); use outro diretorio"
if (( ! CRIADO_PELO_SCRIPT )); then
    [[ "$(basename -- "$OUT_DIR")" == "backups_postgis" && -z "$(ls -A -- "$OUT_DIR")" ]] \
        || erro "--out-dir ($OUT_DIR) ja existe e nao e um diretorio dedicado vazio chamado backups_postgis. A permissao dele NAO foi alterada. Informe um diretorio NOVO (o script cria) ou omita --out-dir (cria ~/backups_postgis/<data>)."
fi
chmod 700 -- "$OUT_DIR"

ARQUIVO="$OUT_DIR/mapas_backup_${CARIMBO}.sql"
PARCIAL="$ARQUIVO.parcial"
[[ ! -e "$ARQUIVO" && ! -e "$PARCIAL" ]] || erro "ja existe $ARQUIVO; aguarde 1 s e rode de novo"

limpar() { rm -f -- "$PARCIAL"; desfazer_dir; }
trap limpar EXIT

# ---------------------------------------------------------------------
# Dump
# ---------------------------------------------------------------------
set -o noclobber
if [[ "$MODO" == "container" ]]; then
    info "pg_dump -t public.mapas via docker exec $CONTAINER (role $PG_ROLE, base $PG_DB, socket local)"
    $DOCKER exec "$CONTAINER" pg_dump -U "$PG_ROLE" -d "$PG_DB" -t public.mapas --no-password > "$PARCIAL" \
        || erro "pg_dump falhou dentro do container (codigo $?)"
else
    info "pg_dump -t public.mapas em $PG_HOST:$PG_PORT (role $PG_ROLE, base $PG_DB, sslmode=$PGSSLMODE)"
    PREFIXO=()
    # Git Bash (mintty): o prompt de senha do pg_dump.exe precisa de um
    # console; o winpty (incluido no Git for Windows) fornece. Desligue com
    # PG_SEM_WINPTY=1 se o seu terminal ja for um console do Windows.
    if [[ -n "${MSYSTEM:-}" && -z "${PGPASSWORD:-}" && -t 0 && -z "${PG_SEM_WINPTY:-}" ]] \
       && command -v winpty >/dev/null 2>&1; then
        PREFIXO=(winpty)
    fi
    # Caminho no formato do Windows para o pg_dump.exe (o winpty nao
    # converte /c/... como o Git Bash faz).
    DESTINO_PG="$PARCIAL"
    if [[ -n "${MSYSTEM:-}" ]] && command -v cygpath >/dev/null 2>&1; then
        DESTINO_PG="$(cygpath -w -- "$PARCIAL")"
    fi
    ${PREFIXO[@]+"${PREFIXO[@]}"} pg_dump -h "$PG_HOST" -p "$PG_PORT" -U "$PG_ROLE" -d "$PG_DB" \
        -t public.mapas -f "$DESTINO_PG" \
        || erro "pg_dump falhou (codigo $?). Nada foi gravado."
fi
set +o noclobber

# ---------------------------------------------------------------------
# Conferencia do arquivo
# ---------------------------------------------------------------------
[[ -s "$PARCIAL" ]] || erro "dump vazio. Nada foi gravado."
grep -q '^COPY public\.mapas ' "$PARCIAL" \
    || erro "dump sem 'COPY public.mapas' (tabela nao exportada). Nada foi gravado."
grep -q 'PostgreSQL database dump complete' "$PARCIAL" \
    || erro "dump incompleto (sem o rodape 'dump complete'). Nada foi gravado."

LINHAS="$(awk '/^COPY public\.mapas /{dentro=1; next} dentro && /^\\\.$/{dentro=0} dentro{n++} END{print n+0}' "$PARCIAL")"

mv -- "$PARCIAL" "$ARQUIVO"
chmod 600 -- "$ARQUIVO"
trap - EXIT

info "Backup OK: $ARQUIVO"
info "Linhas de public.mapas no dump: $LINHAS (compare com SELECT count(*) FROM public.mapas;)"
if command -v sha256sum >/dev/null 2>&1; then
    info "SHA-256: $(sha256sum -- "$ARQUIVO" | cut -d' ' -f1)"
fi
info "Guarde o arquivo fora do repositorio e do OneDrive. Restauracao: ver --help."
