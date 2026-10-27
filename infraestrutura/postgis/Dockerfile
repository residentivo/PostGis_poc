# =====================================================================
# infraestrutura/postgis/Dockerfile
# PostgreSQL 17 + PostGIS 3.5 (mesma linha do servidor 192.168.168.106)
# com a base casaos, a tabela public.mapas ja migrada (PK + CHECK), as
# roles de database/roles.sql e SSL ligado.
#
# Build (contexto = raiz do repositorio):
#   docker build -f infraestrutura/postgis/Dockerfile -t postgis_mapas:local .
#
# Os scripts de /docker-entrypoint-initdb.d so rodam com o volume de dados
# VAZIO (primeira subida). Depois disso, nada e reaplicado sozinho.
# =====================================================================
FROM postgis/postgis:17-3.5

# openssl: gera o certificado autoassinado do servidor na primeira subida.
# Vem do Debian; o repositorio PGDG (bullseye-pgdg saiu de apt.postgresql.org
# e responde 404) fica fora so durante esta instalacao e volta em seguida.
RUN mkdir -p /tmp/pgdg \
    && mv /etc/apt/sources.list.d/pgdg* /tmp/pgdg/ \
    && apt-get update \
    && apt-get install -y --no-install-recommends openssl \
    && mv /tmp/pgdg/* /etc/apt/sources.list.d/ \
    && rm -rf /var/lib/apt/lists/* /tmp/pgdg

# Fonte oficial do banco: database/. Os scripts sao copiados sem alteracao.
COPY database/mapas.sql \
     database/migracao_mapas_pk.sql \
     database/validar_check_mapas.sql \
     database/roles.sql \
     /opt/mapas/sql/
COPY infraestrutura/postgis/pg_hba.conf /opt/mapas/pg_hba.conf

# O 10_postgis.sh da imagem base cria as extensoes; estes rodam depois.
COPY --chmod=755 infraestrutura/postgis/initdb/ /docker-entrypoint-initdb.d/

ENV POSTGRES_DB=casaos \
    POSTGRES_USER=casaos \
    POSTGRES_INITDB_ARGS="--auth-local=trust --auth-host=scram-sha-256"
