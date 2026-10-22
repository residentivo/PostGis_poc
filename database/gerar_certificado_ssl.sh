#!/usr/bin/env bash
# =====================================================================
# database/gerar_certificado_ssl.sh  (acao 5 do DBA / S-3)
#
# Gera uma CA propria + certificado de servidor assinado por ela e copia
# server.crt / server.key para o data_directory do container PostgreSQL.
# Roda NO HOST DO SERVIDOR (192.168.168.106), pelo DBA. Os agentes NAO
# executam este script. Passo 2 da ordem descrita em database/roles.sql;
# depois rodar roles.sql (secao 7 liga o SSL).
#
# Uso:
#   ./gerar_certificado_ssl.sh --container <nome-ou-id> [opcoes]
#
# Opcoes:
#   --container <c>   nome ou id do container PostgreSQL (obrigatorio)
#   --ip <ip>         IP do servidor no SAN (padrao 192.168.168.106)
#   --dns <host>      hostname extra no SAN (opcional)
#   --out-dir <dir>   diretorio de trabalho no host (padrao ~/pg_ssl);
#                     deve ficar FORA do repositorio e do OneDrive
#   --pguser <role>   role superusuaria para ler o data_directory via
#                     socket local (padrao casaos)
#   --rsa             RSA 3072 em vez de ECDSA P-256 (padrao)
#   --force           sobrescreve server.crt/server.key ja existentes no
#                     container (sem isso o script aborta)
#   -h | --help       esta ajuda
#
# Docker: se o usuario nao estiver no grupo docker, rode com sudo
# (o padrao de ~/pg_ssl passa a ser o HOME do root) ou defina
# DOCKER="sudo docker".
#
# Saida (no host, umask 077):
#   <out-dir>/ca.key      CHAVE DA CA - nunca vai para o container; guarde
#                         em lugar seguro (cofre/midia offline), fora do
#                         OneDrive e do repositorio
#   <out-dir>/ca.crt      certificado da CA - copiar para os clientes que
#                         forem usar PG_SSLMODE=VerifyFull
#   <out-dir>/server.*    certificado/chave do servidor (copia local)
# Se <out-dir>/ca.key ja existir, a CA e REUTILIZADA (clientes que ja
# confiam no ca.crt continuam validos).
# =====================================================================
set -euo pipefail
umask 077

DOCKER="${DOCKER:-docker}"
CONTAINER=""
IP="192.168.168.106"
DNS=""
OUT_DIR="${HOME}/pg_ssl"
PGUSER_LOCAL="casaos"
ALGO="ec"
FORCE=0
DIAS_CA=3650
DIAS_SERVIDOR=825

erro() { echo "ERRO: $*" >&2; exit 1; }
info() { echo ">> $*"; }
ajuda() { sed -n '2,40p' "$0" | sed 's/^# \{0,1\}//'; exit 0; }

while [[ $# -gt 0 ]]; do
    case "$1" in
        --container) CONTAINER="${2:-}"; shift 2 ;;
        --ip)        IP="${2:-}"; shift 2 ;;
        --dns)       DNS="${2:-}"; shift 2 ;;
        --out-dir)   OUT_DIR="${2:-}"; shift 2 ;;
        --pguser)    PGUSER_LOCAL="${2:-}"; shift 2 ;;
        --rsa)       ALGO="rsa"; shift ;;
        --force)     FORCE=1; shift ;;
        -h|--help)   ajuda ;;
        *)           erro "opcao desconhecida: $1 (use --help)" ;;
    esac
done

# ---------------------------------------------------------------------
# Validacoes
# ---------------------------------------------------------------------
[[ -n "$CONTAINER" ]] || erro "informe --container <nome-ou-id>"
[[ "$CONTAINER" =~ ^[A-Za-z0-9][A-Za-z0-9_.-]*$ ]] || erro "nome de container invalido: $CONTAINER"
[[ "$IP" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ || "$IP" =~ ^[0-9A-Fa-f:]+$ ]] || erro "IP invalido: $IP"
if [[ -n "$DNS" ]]; then
    [[ "$DNS" =~ ^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$ ]] || erro "hostname invalido: $DNS"
fi
[[ "$PGUSER_LOCAL" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]] || erro "role invalida: $PGUSER_LOCAL"

command -v openssl >/dev/null 2>&1 || erro "openssl nao encontrado no host (instale, ex.: apt install openssl)"
$DOCKER inspect --type container "$CONTAINER" >/dev/null 2>&1 || erro "container '$CONTAINER' nao encontrado (precisa de sudo?)"
[[ "$($DOCKER inspect -f '{{.State.Running}}' "$CONTAINER")" == "true" ]] || erro "container '$CONTAINER' nao esta rodando"

# ---------------------------------------------------------------------
# Diretorio de saida (R5): checa OneDrive ANTES de criar, cria, confere
# o dono e so entao restringe a permissao.
# ---------------------------------------------------------------------
# Casa so COMPONENTES de diretorio "OneDrive" ou "OneDrive - <org>", sem
# falso positivo em pastas que apenas contem a palavra no nome.
dentro_onedrive() {
    local caminho="$1" r=1
    shopt -s nocasematch
    if [[ "$caminho" =~ /OneDrive(\ -[^/]*)?(/|$) ]]; then
        r=0
    fi
    shopt -u nocasematch
    return $r
}

[[ "$OUT_DIR" == /* ]] || OUT_DIR="$PWD/$OUT_DIR"
if dentro_onedrive "$OUT_DIR"; then
    erro "--out-dir esta dentro do OneDrive ($OUT_DIR); use um diretorio local"
fi

mkdir -p -- "$OUT_DIR"
OUT_DIR="$(cd -- "$OUT_DIR" && pwd -P)"
# Repete com o caminho real (symlinks resolvidos).
if dentro_onedrive "$OUT_DIR"; then
    erro "--out-dir esta dentro do OneDrive ($OUT_DIR); use um diretorio local"
fi
if command -v git >/dev/null 2>&1 && git -C "$OUT_DIR" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    erro "--out-dir esta dentro de um repositorio git ($OUT_DIR); use um diretorio fora do repo"
fi
[[ "$(stat -c %u -- "$OUT_DIR")" == "$(id -u)" ]] \
    || erro "--out-dir ($OUT_DIR) nao pertence ao usuario atual (uid $(id -u)); use outro diretorio"
chmod 700 -- "$OUT_DIR"
cd -- "$OUT_DIR"

# ---------------------------------------------------------------------
# Configuracao minima do openssl: nao depende do openssl.cnf global (o do
# Git for Windows tem "authorityKeyIdentifier=keyid:nonss", recusado pelo
# "openssl req -x509"). Caminho RELATIVO ao OUT_DIR (cd acima), para
# funcionar igual no Linux e no Git Bash. Removida ao sair.
# ---------------------------------------------------------------------
CNF="openssl_min.cnf"
printf '%s\n' '[ req ]' 'distinguished_name = dn' 'prompt = no' '' '[ dn ]' 'CN = placeholder' > "$CNF"
export OPENSSL_CONF="./$CNF"
trap 'rm -f -- "$OUT_DIR/$CNF" "$OUT_DIR/server.csr" "$OUT_DIR/server.ext"' EXIT

# ---------------------------------------------------------------------
# C1: validacao da CA existente ANTES de gerar qualquer coisa
# ---------------------------------------------------------------------
REUSAR_CA=0
if [[ -e ca.key && -e ca.crt ]]; then
    [[ "$(openssl x509 -in ca.crt -noout -pubkey)" == "$(openssl pkey -in ca.key -pubout)" ]] \
        || erro "ca.key e ca.crt em $OUT_DIR nao correspondem. Nada foi gerado. Corrija ou use outro --out-dir."
    REUSAR_CA=1
elif [[ -e ca.key || -e ca.crt ]]; then
    erro "existe so ca.key ou so ca.crt em $OUT_DIR. Nada foi gerado. Restaure o par ou use outro --out-dir."
fi

# ---------------------------------------------------------------------
# data_directory do container e certificado existente
# ---------------------------------------------------------------------
PGDATA_C="$($DOCKER exec -u postgres "$CONTAINER" \
    psql -X -U "$PGUSER_LOCAL" -d casaos -Atc 'SHOW data_directory' 2>/dev/null || true)"
[[ -n "$PGDATA_C" && "$PGDATA_C" == /* ]] || \
    erro "nao consegui ler o data_directory via psql no container (role $PGUSER_LOCAL)"
info "data_directory no container: $PGDATA_C"

EXISTE_NO_CONTAINER=0
if $DOCKER exec -u root "$CONTAINER" sh -c 'test -e "$1/server.crt" || test -e "$1/server.key"' _ "$PGDATA_C"; then
    if [[ "$FORCE" -ne 1 ]]; then
        erro "ja existe server.crt/server.key em $PGDATA_C. Nada foi alterado. Use --force para sobrescrever."
    fi
    EXISTE_NO_CONTAINER=1
    info "--force: o certificado existente no container sera salvo como .bak e substituido"
fi

DONO="$($DOCKER exec -u root "$CONTAINER" stat -c '%u:%g' "$PGDATA_C")"
info "dono do data_directory (uid:gid): $DONO"

# ---------------------------------------------------------------------
# Geracao (no host)
# ---------------------------------------------------------------------
gerar_chave() {
    if [[ "$ALGO" == "rsa" ]]; then
        openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out "$1"
    else
        openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out "$1"
    fi
    chmod 600 "$1"
}

# C2: a CA so pode emitir certificados para o IP (e o DNS) deste servidor.
if [[ "$IP" == *:* ]]; then
    NC="permitted;IP:${IP}/ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff"
else
    NC="permitted;IP:${IP}/255.255.255.255"
fi
if [[ -n "$DNS" ]]; then
    NC="${NC},permitted;DNS:${DNS}"
fi

if [[ "$REUSAR_CA" -eq 1 ]]; then
    # O nameConstraints de uma CA antiga pode nao cobrir o IP/DNS atual:
    # o "openssl verify" abaixo detecta e aborta.
    info "reutilizando a CA existente em $OUT_DIR (par ca.key/ca.crt conferido)"
else
    info "gerando a CA ($ALGO, $DIAS_CA dias, nameConstraints $NC)"
    gerar_chave ca.key
    openssl req -config "$CNF" -x509 -new -key ca.key -sha256 -days "$DIAS_CA" \
        -subj "/CN=PostGis_poc CA" \
        -addext "basicConstraints=critical,CA:TRUE,pathlen:0" \
        -addext "keyUsage=critical,keyCertSign,cRLSign" \
        -addext "nameConstraints=critical,${NC}" \
        -out ca.crt
fi

SAN="IP:${IP}"
[[ -n "$DNS" ]] && SAN="${SAN},DNS:${DNS}"
KU="digitalSignature"
[[ "$ALGO" == "rsa" ]] && KU="digitalSignature,keyEncipherment"

info "gerando o certificado do servidor ($ALGO, $DIAS_SERVIDOR dias, SAN $SAN)"
gerar_chave server.key
openssl req -config "$CNF" -new -key server.key -subj "/CN=${DNS:-$IP}" -out server.csr
cat > server.ext <<EXT
basicConstraints=critical,CA:FALSE
keyUsage=critical,${KU}
extendedKeyUsage=serverAuth
subjectAltName=${SAN}
EXT
openssl x509 -req -in server.csr -CA ca.crt -CAkey ca.key -CAcreateserial \
    -days "$DIAS_SERVIDOR" -sha256 -extfile server.ext -out server.crt
rm -f server.csr server.ext
chmod 644 server.crt ca.crt

# Conferencias locais: cadeia valida e chave corresponde ao certificado.
openssl verify -CAfile ca.crt server.crt >/dev/null \
    || erro "server.crt nao valida contra ca.crt (CA de outro IP/DNS? ver nameConstraints)"
[[ "$(openssl x509 -in server.crt -noout -pubkey)" == "$(openssl pkey -in server.key -pubout)" ]] \
    || erro "server.key nao corresponde a server.crt"

# ---------------------------------------------------------------------
# Copia para o container + dono/permissoes
# ---------------------------------------------------------------------
# R1: com --force, guarda os arquivos antigos como .bak.<data> (mv mantem
# o dono; a chave antiga fica 0600).
if [[ "$EXISTE_NO_CONTAINER" -eq 1 ]]; then
    SUFIXO="bak.$(date +%Y%m%d%H%M%S)"
    info "salvando os arquivos antigos como *.${SUFIXO}"
    $DOCKER exec -u root "$CONTAINER" sh -c '
        set -e
        if [ -e "$1/server.crt" ]; then mv -- "$1/server.crt" "$1/server.crt.$2"; fi
        if [ -e "$1/server.key" ]; then
            mv -- "$1/server.key" "$1/server.key.$2"
            chmod 600 "$1/server.key.$2"
        fi
    ' _ "$PGDATA_C" "$SUFIXO"
fi

info "copiando server.crt e server.key para $CONTAINER:$PGDATA_C"
$DOCKER cp server.crt "$CONTAINER:$PGDATA_C/server.crt"
$DOCKER cp server.key "$CONTAINER:$PGDATA_C/server.key"

# Dono = dono do data_directory (usuario postgres do container).
$DOCKER exec -u root "$CONTAINER" sh -c '
    set -e
    chown "$2" "$1/server.crt" "$1/server.key" 2>/dev/null || chown postgres:postgres "$1/server.crt" "$1/server.key"
    chmod 644 "$1/server.crt"
    chmod 600 "$1/server.key"
' _ "$PGDATA_C" "$DONO"

info "permissoes no container:"
$DOCKER exec -u root "$CONTAINER" stat -c '  %A %U:%G %n' "$PGDATA_C/server.crt" "$PGDATA_C/server.key"
$DOCKER exec -u postgres "$CONTAINER" test -r "$PGDATA_C/server.key" \
    || erro "o usuario postgres nao consegue ler $PGDATA_C/server.key"

# ---------------------------------------------------------------------
# Resumo
# ---------------------------------------------------------------------
cat <<FIM

Concluido. Nada foi alterado na configuracao do PostgreSQL ainda.

  Validade do servidor : $(openssl x509 -in server.crt -noout -enddate | cut -d= -f2)
  SAN                  : $SAN
  CA (cliente)         : $OUT_DIR/ca.crt
  CHAVE DA CA          : $OUT_DIR/ca.key

- Copie $OUT_DIR/ca.crt para os clientes que usarem PG_SSLMODE=VerifyFull
  (ex.: psql ... sslmode=verify-full sslrootcert=<caminho>/ca.crt).
- NAO importe o ca.crt no repositorio de certificados do Windows nem do
  sistema; use-o so via sslrootcert / parametro de CA do cliente.
- Guarde $OUT_DIR/ca.key em lugar SEGURO (cofre/midia offline). Ela NAO
  foi copiada para o container. Quem tiver essa chave emite certificados
  aceitos pelos clientes. Nunca coloque no OneDrive nem no repositorio.
- Proximo passo: rodar database/roles.sql (a secao 7 liga o SSL com
  ALTER SYSTEM + pg_reload_conf). Logo depois, teste OBRIGATORIO de
  conexao TCP com sslmode=require (secao 7.4); se falhar, rollback
  imediato da 7.4 ANTES de qualquer restart do container.
FIM
