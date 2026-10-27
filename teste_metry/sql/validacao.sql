-- =====================================================================
-- teste_metry/sql/validacao.sql  (M3.1 - M3.4, M3.6)
-- Base: casaos | Servidor: 192.168.168.106 | Alvo: public.mapas.metry
-- SOMENTE LEITURA: apenas SELECT/SHOW. Nenhum DDL/DML, nenhum objeto
-- ou role criado/alterado.
-- Execucao sugerida (senha via PGPASSWORD no ambiente, nunca na linha de
-- comando nem em arquivo dentro do projeto):
--   psql "host=192.168.168.106 dbname=casaos user=$PG_USER sslmode=require" \
--        -v ON_ERROR_STOP=1 -f validacao.sql
-- RQ-9: ANTES de executar M3.7/M6.6, confirmar que a role usada NAO e
-- superusuario (ver bloco M3.4: rolsuper deve ser false). Se for true,
-- parar e usar uma role com apenas INSERT/SELECT (e DELETE so para os
-- testes de integracao) em public.mapas.
-- =====================================================================

-- M3.1 Extensao PostGIS e versao
SELECT extname, extversion
FROM pg_extension
WHERE extname = 'postgis';

SELECT postgis_full_version();

-- M3.2 Tipo/SRID da coluna metry
-- A DDL declara public.geometry sem typmod: esperado type 'GEOMETRY',
-- srid 0 (nao restrito) e coord_dimension 2. O SRID de cada linha vem
-- de PG_SRID (parametro @srid), portanto metry pode ter SRIDs misturados.
SELECT f_table_schema, f_table_name, f_geometry_column,
       coord_dimension, srid, type
FROM geometry_columns
WHERE f_table_schema = 'public'
  AND f_table_name   = 'mapas'
  AND f_geometry_column = 'metry';

-- Tipo de metry e identity de id (esperado: metry USER-DEFINED/geometry;
-- id is_identity = YES, identity_generation = BY DEFAULT antes da
-- migracao database/migracao_mapas_pk.sql e ALWAYS depois)
SELECT column_name, data_type, udt_schema, udt_name,
       is_nullable, is_identity, identity_generation, column_default
FROM information_schema.columns
WHERE table_schema = 'public'
  AND table_name   = 'mapas'
ORDER BY ordinal_position;

-- M3.3 SRIDs permitidos (esperado: 4 linhas, todas EPSG)
SELECT srid, auth_name, auth_srid
FROM spatial_ref_sys
WHERE srid IN (4326, 4674, 3857, 31983)
ORDER BY srid;

-- M3.3 Restricoes em public.mapas.
-- Antes da migracao: nenhuma linha (S3: sem PK/UNIQUE em id).
-- Depois (PO3.8, D-N11): 2 linhas - mapas_pkey | p | PRIMARY KEY (id) e
-- mapas_nao_vazia | c | CHECK (((graphy IS NOT NULL) OR (metry IS NOT
-- NULL))) (com "NOT VALID" ate o VALIDATE). Continua sem CHECK de SRID
-- sobre metry (ex.: enforce_srid).
SELECT conname, contype, pg_get_constraintdef(oid) AS definicao
FROM pg_constraint
WHERE conrelid = 'public.mapas'::regclass;

-- PO3.8 (informativo) PK exata em id: esperado pk_exata = t e
-- id_always = t depois da migracao (f antes). Com t, cada INSERT do
-- programa (sem id) ganha um id proprio e o DELETE da limpeza dos testes
-- de integracao (WHERE id = @id ...) apaga no maximo 1 linha.
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

-- M3.3 Triggers de usuario em public.mapas (esperado: nenhuma linha)
SELECT tgname, tgenabled, pg_get_triggerdef(oid) AS definicao
FROM pg_trigger
WHERE tgrelid = 'public.mapas'::regclass
  AND NOT tgisinternal;

-- Informativo (REC-2): distribuicao atual de SRIDs em metry
SELECT ST_SRID(metry) AS srid, count(*) AS linhas
FROM public.mapas
WHERE metry IS NOT NULL
GROUP BY ST_SRID(metry)
ORDER BY 1;

-- M3.4 (S1) Privilegios do current_user em public.mapas
-- Esperado: INSERT e SELECT = true. DELETE so e necessario para os testes
-- de integracao (limpeza RQ-6); o programa nunca executa DELETE.
SELECT current_user,
       has_table_privilege(current_user, 'public.mapas', 'INSERT') AS pode_insert,
       has_table_privilege(current_user, 'public.mapas', 'SELECT') AS pode_select,
       has_table_privilege(current_user, 'public.mapas', 'DELETE') AS pode_delete;

-- M3.4 (S1 / RQ-9) Atributos da role. Esperado: todos false.
-- rolsuper = true -> NAO prosseguir com M3.7/M6.6 (RQ-9).
SELECT rolname, rolsuper, rolcreaterole, rolcreatedb
FROM pg_roles
WHERE rolname = current_user;

-- M3.4 (S2) Transporte e hash de senha
SHOW ssl;                  -- 'on' permite PG_SSLMODE=Require/VerifyFull
SHOW password_encryption;  -- esperado: scram-sha-256

-- M3.4 (REC-4) A sessao atual esta cifrada? Esperado: ssl = true.
-- Orientacao: no programa usar PG_SSLMODE=Require (minimo) ou VerifyFull
-- (preferivel, com certificado do servidor confiavel). Prefer pode cair
-- para conexao sem TLS.
SELECT ssl, version, cipher
FROM pg_stat_ssl
WHERE pid = pg_backend_pid();

-- ---------------------------------------------------------------------
-- M3.6 (OPCIONAL) Conferencia dos exemplos fixos D-M8 via ST_Transform.
-- Somente SELECT sobre literais; nao le nem grava public.mapas.
-- Os SRIDs aqui sao literais apenas porque esta e uma consulta manual de
-- conferencia, fora do programa. No programa o SRID e sempre @srid (RQ-1).
-- Compare as colunas *_3857 e *_31983 com os WKT de ExemplosFixos.Para()
-- (precisao nao e requisito: diferencas de metros sao aceitaveis).
-- ---------------------------------------------------------------------
WITH exemplos(nome, wkt_4326) AS (
    VALUES
      ('ponto',    'POINT(-46.6333 -23.5505)'),
      ('linha',    'LINESTRING(-46.6333 -23.5505, -46.6566 -23.5614, -46.6750 -23.5700)'),
      ('poligono', 'POLYGON((-46.6400 -23.5400, -46.6200 -23.5400, -46.6200 -23.5600, -46.6400 -23.5600, -46.6400 -23.5400))')
)
SELECT nome,
       wkt_4326,
       ST_AsText(ST_Transform(ST_GeomFromText(wkt_4326, 4326), 3857), 2)  AS wkt_3857,
       ST_AsText(ST_Transform(ST_GeomFromText(wkt_4326, 4326), 31983), 2) AS wkt_31983
FROM exemplos
ORDER BY nome;
