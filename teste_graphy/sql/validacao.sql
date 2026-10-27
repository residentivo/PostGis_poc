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

-- F3.6 (S3) Restricoes PK/UNIQUE em public.mapas.
-- Antes da migracao (database/migracao_mapas_pk.sql): nenhuma linha.
-- Depois (PO3.8, D-N11): 1 linha - mapas_pkey | p | PRIMARY KEY (id).
-- Na consulta de colunas acima, id passa a identity_generation = ALWAYS.
SELECT conname, contype, pg_get_constraintdef(oid) AS definicao
FROM pg_constraint
WHERE conrelid = 'public.mapas'::regclass
  AND contype IN ('p', 'u');

-- PO3.8 (informativo) PK exata em id: esperado pk_exata = t e
-- id_always = t depois da migracao (f antes). Com t, cada INSERT do
-- programa (sem id) ganha um id proprio e o DELETE da limpeza dos testes
-- de integracao (WHERE id = @id) apaga no maximo 1 linha.
SELECT EXISTS (SELECT 1
               FROM pg_constraint k
               JOIN pg_index i ON i.indexrelid = k.conindid
               JOIN pg_attribute a ON a.attrelid = k.conrelid
                                  AND a.attname = 'id' AND NOT a.attisdropped
               WHERE k.conrelid = 'public.mapas'::regclass
                 AND k.contype = 'p' AND k.conname = 'mapas_pkey'
                 AND k.conkey = ARRAY[a.attnum] AND NOT k.condeferrable
                 AND i.indisvalid AND i.indisunique AND i.indisprimary) AS pk_exata,
       EXISTS (SELECT 1 FROM pg_attribute a
               WHERE a.attrelid = 'public.mapas'::regclass AND a.attname = 'id'
                 AND a.attidentity = 'a') AS id_always;

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
