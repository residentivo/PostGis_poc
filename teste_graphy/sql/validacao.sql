-- =====================================================================
-- teste_graphy/sql/validacao.sql  (F3.1 - F3.4, F3.6)
-- Base: casaos | Servidor: 192.168.168.106 | Alvo: public.mapas
-- SOMENTE LEITURA: apenas SELECT/SHOW. Nenhum DDL/DML, nenhum objeto
-- ou role criado/alterado.
-- Execucao sugerida (senha via PGPASSWORD, nunca na linha de comando):
--   psql -h 192.168.168.106 -d casaos -U "$PG_USER" -v ON_ERROR_STOP=1 -f validacao.sql
-- =====================================================================

-- F3.1 Extensao PostGIS e versao
SELECT extname, extversion
FROM pg_extension
WHERE extname = 'postgis';

SELECT postgis_full_version();

-- F3.2 Tipo/SRID da coluna graphy
-- A DDL declara public.geography sem typmod: srid esperado 0 (nao restrito)
-- e type 'Geometry'. O programa forca SRID 4326 no INSERT.
SELECT f_table_schema, f_table_name, f_geography_column,
       coord_dimension, srid, type
FROM geography_columns
WHERE f_table_schema = 'public'
  AND f_table_name   = 'mapas';

SELECT column_name, data_type, udt_schema, udt_name,
       is_nullable, is_identity, identity_generation, column_default
FROM information_schema.columns
WHERE table_schema = 'public'
  AND table_name   = 'mapas'
ORDER BY ordinal_position;

-- F3.6 (S3) Restricoes PK/UNIQUE em public.mapas (esperado: nenhuma linha)
SELECT conname, contype, pg_get_constraintdef(oid) AS definicao
FROM pg_constraint
WHERE conrelid = 'public.mapas'::regclass
  AND contype IN ('p', 'u');

-- F3.3 (S1) Privilegios do current_user em public.mapas
SELECT current_user,
       has_table_privilege(current_user, 'public.mapas', 'INSERT') AS pode_insert,
       has_table_privilege(current_user, 'public.mapas', 'SELECT') AS pode_select,
       has_table_privilege(current_user, 'public.mapas', 'DELETE') AS pode_delete;

-- F3.3 (S1) Atributos da role (sinalizar se qualquer um for true)
SELECT rolname, rolsuper, rolcreaterole, rolcreatedb
FROM pg_roles
WHERE rolname = current_user;

-- F3.4 (S2) Transporte e hash de senha
SHOW ssl;                  -- 'on' permite PG_SSLMODE=require/verify-full
SHOW password_encryption;  -- esperado: scram-sha-256
