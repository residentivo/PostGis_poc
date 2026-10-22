-- =====================================================================
-- api_mapas/sql/consultas_referencia.sql  (A3.1 - A3.4)
-- Alvo: public.mapas (id int4 identity, graphy geography, metry geometry)
-- SOMENTE LEITURA. Nenhum DDL/DML foi executado. Os blocos de indice e
-- GRANT no fim estao COMENTADOS e sao apenas sugestao para o DBA.
--
-- Uso na API (A4.3): copiar as consultas Q1/Q2 como constantes, parametro
-- @id com NpgsqlDbType.Integer (Npgsql reescreve @id para $1). Nenhuma
-- concatenacao de texto. A coluna nunca vem do usuario: ha uma constante
-- por endpoint.
--
-- Contrato (AJ-1..AJ-3):
--   tipo      GeometryType (maiusculas: POINT, LINESTRING, POLYGON, MULTI*,
--             GEOMETRYCOLLECTION; com M aparece o sufixo M, ex. POINTM)
--   srid      ST_SRID da geometria (graphy normalmente 4326)
--   wkt       ST_AsText no SRID nativo
--   geojson   texto GeoJSON (o backend converte em objeto JsonElement),
--             9 casas; WGS84 via ST_Transform(g, 4326) quando srid <> 0,
--             coordenadas nativas quando srid = 0 (ST_Transform falha com
--             SRID 0, por isso o CASE)
--   x / y     ST_X/ST_Y nativos, so para POINT; senao NULL
--   longitude / latitude  ST_X/ST_Y em 4326, so para POINT com srid <> 0;
--             senao NULL
--   Filtro IS NOT NULL, ORDER BY ctid, LIMIT 101 (101 linhas => truncado).
--
-- Observacoes (A3.2 / A3.3):
--   * public.mapas NAO tem PK nem UNIQUE em id (S3) e nao ha indice em id:
--     o mesmo id pode ter 1..N linhas e cada consulta faz seq scan.
--   * ctid e a unica ordem estavel disponivel; ela muda apos UPDATE ou
--     VACUUM FULL da linha, portanto a ordem e estavel por leitura, nao
--     um identificador permanente. Nao expor ctid na API.
--   * ST_Transform para 4326 depende de 4674/3857/31983 em spatial_ref_sys
--     (conferido em M3.3). SRID 0 nao passa por ST_Transform (CASE).
--   * CASE do PostgreSQL so avalia o ramo escolhido por linha, entao o
--     ST_Transform nunca roda para SRID 0.
-- =====================================================================


-- ---------------------------------------------------------------------
-- Q1 - GET /api/mapas/{id}/graphy   (constante na API, parametro @id)
-- ---------------------------------------------------------------------
SELECT GeometryType(t.g)                                        AS tipo,
       ST_SRID(t.g)                                             AS srid,
       ST_AsText(t.g)                                           AS wkt,
       CASE WHEN ST_SRID(t.g) <> 0
            THEN ST_AsGeoJSON(ST_Transform(t.g, 4326), 9)
            ELSE ST_AsGeoJSON(t.g, 9) END                       AS geojson,
       CASE WHEN GeometryType(t.g) = 'POINT'
            THEN ST_X(t.g) END                                  AS x,
       CASE WHEN GeometryType(t.g) = 'POINT'
            THEN ST_Y(t.g) END                                  AS y,
       CASE WHEN GeometryType(t.g) = 'POINT' AND ST_SRID(t.g) <> 0
            THEN ST_X(ST_Transform(t.g, 4326)) END              AS longitude,
       CASE WHEN GeometryType(t.g) = 'POINT' AND ST_SRID(t.g) <> 0
            THEN ST_Y(ST_Transform(t.g, 4326)) END              AS latitude
FROM (SELECT m.ctid AS linha, m.graphy::geometry AS g
      FROM public.mapas AS m
      WHERE m.id = @id
        AND m.graphy IS NOT NULL
      ORDER BY m.ctid
      LIMIT 101) AS t
ORDER BY t.linha;


-- ---------------------------------------------------------------------
-- Q2 - GET /api/mapas/{id}/metry   (constante na API, parametro @id)
-- ---------------------------------------------------------------------
SELECT GeometryType(t.g)                                        AS tipo,
       ST_SRID(t.g)                                             AS srid,
       ST_AsText(t.g)                                           AS wkt,
       CASE WHEN ST_SRID(t.g) <> 0
            THEN ST_AsGeoJSON(ST_Transform(t.g, 4326), 9)
            ELSE ST_AsGeoJSON(t.g, 9) END                       AS geojson,
       CASE WHEN GeometryType(t.g) = 'POINT'
            THEN ST_X(t.g) END                                  AS x,
       CASE WHEN GeometryType(t.g) = 'POINT'
            THEN ST_Y(t.g) END                                  AS y,
       CASE WHEN GeometryType(t.g) = 'POINT' AND ST_SRID(t.g) <> 0
            THEN ST_X(ST_Transform(t.g, 4326)) END              AS longitude,
       CASE WHEN GeometryType(t.g) = 'POINT' AND ST_SRID(t.g) <> 0
            THEN ST_Y(ST_Transform(t.g, 4326)) END              AS latitude
FROM (SELECT m.ctid AS linha, m.metry AS g
      FROM public.mapas AS m
      WHERE m.id = @id
        AND m.metry IS NOT NULL
      ORDER BY m.ctid
      LIMIT 101) AS t
ORDER BY t.linha;


-- ---------------------------------------------------------------------
-- Uso manual no psql: as mesmas consultas com $1 via PREPARE (descomentar).
-- Substituir "@id" por "$1" no texto de Q1/Q2:
--   PREPARE graphy_por_id(int4) AS <Q1 com $1>;
--   PREPARE metry_por_id(int4)  AS <Q2 com $1>;
--   EXECUTE graphy_por_id(1);
--   EXECUTE metry_por_id(1);
--   DEALLOCATE graphy_por_id; DEALLOCATE metry_por_id;
-- ---------------------------------------------------------------------


-- ---------------------------------------------------------------------
-- Verificacoes opcionais (somente leitura, NAO executadas pelo DataBrain)
-- ---------------------------------------------------------------------
-- A3.2 SRIDs necessarios para ST_Transform (esperado: 4 linhas EPSG)
-- SELECT srid, auth_name, auth_srid FROM spatial_ref_sys
-- WHERE srid IN (4326, 4674, 3857, 31983) ORDER BY srid;
--
-- A3.2 SRID 0 nao e transformado; demais SRIDs vao para 4326
-- SELECT s.srid,
--        CASE WHEN s.srid <> 0
--             THEN ST_AsText(ST_Transform(ST_SetSRID(ST_MakePoint(s.x, s.y), s.srid), 4326))
--             ELSE ST_AsText(ST_MakePoint(s.x, s.y)) END AS em_4326
-- FROM (VALUES (0, 1.0, 2.0), (4326, -46.6333, -23.5505),
--              (4674, -46.6333, -23.5505), (3857, -5191195.21, -2698729.46),
--              (31983, 333250, 7394600)) AS s(srid, x, y);
--
-- A3.3 Constraints e indices em public.mapas (esperado: nenhuma linha)
-- SELECT conname, contype FROM pg_constraint
-- WHERE conrelid = 'public.mapas'::regclass;
-- SELECT indexname, indexdef FROM pg_indexes
-- WHERE schemaname = 'public' AND tablename = 'mapas';


-- ---------------------------------------------------------------------
-- SUGESTAO (A3.3) - NAO EXECUTAR sem aprovacao do usuario/DBA.
-- Indice B-tree em id para evitar seq scan (id nao e unico, por isso nao
-- e UNIQUE/PK). CONCURRENTLY nao bloqueia escrita; nao roda em transacao.
-- ---------------------------------------------------------------------
-- CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_mapas_id
--     ON public.mapas USING btree (id);


-- ---------------------------------------------------------------------
-- SUBSTITUIDO: a fonte oficial da role api_mapas_leitura (e da
-- mapas_teste) agora e database/roles.sql, revisado pelo SecBrain
-- (C1-C5, R1-R8). Usar SOMENTE aquele script; ele ja inclui guardas,
-- idempotencia, idle_in_transaction_session_timeout e a conferencia.
-- Ainda NAO foi executado no banco: fica a cargo do DBA (acao 4).
-- O bloco abaixo fica apenas como REFERENCIA HISTORICA da A3.4 - NAO
-- EXECUTAR (esta desatualizado em relacao a database/roles.sql).
-- ---------------------------------------------------------------------
-- SUGESTAO (A3.4, ajustada por RA-1/RA-2 do SecBrain na A2)
-- NAO EXECUTAR sem aprovacao do usuario/DBA.
-- Role somente leitura para a API (D-A4 / CA-5). Ajustada por O1/O2/R6
-- do SecBrain na A6. Nunca gravar senha real neste arquivo nem no codigo
-- (vai em PG_PASSWORD, fora do repo).
-- ---------------------------------------------------------------------
-- R6: antes de tudo, conferir a versao do PostgreSQL:
-- SHOW server_version;
-- Se for < 15, o schema public ainda concede CREATE a PUBLIC. Avaliar
-- REVOKE CREATE ON SCHEMA public FROM PUBLIC; SOMENTE apos inventariar as
-- roles que criam objetos em public (CasaOS/outros apps podem depender).
--
-- RA-1: role dedicada, sem privilegios administrativos
-- O2: CONNECTION LIMIT alinhado ao pool da API (Maximum Pool Size = 20)
-- O1: sem clausula PASSWORD (evita senha em texto puro no historico/log)
-- CREATE ROLE api_mapas_leitura LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE
--     NOBYPASSRLS NOINHERIT CONNECTION LIMIT 20;
-- O1: definir a senha SOMENTE via psql (o cliente envia o hash, nao o texto):
-- \password api_mapas_leitura
-- ATENCAO: sem o \password a role fica sem senha e NAO autentica por senha.
--
-- RA-1: privilegio minimo (CONNECT na base, USAGE no schema, SELECT so em mapas)
-- ALERTA: o REVOKE abaixo e um no-op defensivo (a role nova nao tem grants
-- diretos na base). NUNCA trocar por "FROM PUBLIC": isso removeria CONNECT/
-- TEMP de todas as roles e derrubaria o CasaOS e os outros apps da base.
-- REVOKE ALL ON DATABASE casaos FROM api_mapas_leitura;
-- GRANT CONNECT ON DATABASE casaos TO api_mapas_leitura;
-- GRANT USAGE ON SCHEMA public TO api_mapas_leitura;
-- GRANT SELECT ON public.mapas TO api_mapas_leitura;
-- -- spatial_ref_sys ja e legivel por PUBLIC na instalacao padrao do PostGIS
-- -- (necessario para ST_Transform); conferir com:
-- -- SELECT has_table_privilege('api_mapas_leitura', 'public.spatial_ref_sys', 'SELECT');
--
-- RA-2: sessao somente leitura e limite de tempo por consulta
-- ALTER ROLE api_mapas_leitura SET default_transaction_read_only = on;
-- ALTER ROLE api_mapas_leitura SET statement_timeout = '15s';
--
-- Conferencia (somente leitura):
-- SELECT rolname, rolsuper, rolcreatedb, rolcreaterole, rolbypassrls, rolconfig
-- FROM pg_roles WHERE rolname = 'api_mapas_leitura';
-- SELECT has_table_privilege('api_mapas_leitura', 'public.mapas', 'SELECT')  AS pode_ler,
--        has_table_privilege('api_mapas_leitura', 'public.mapas', 'INSERT')  AS pode_inserir;
