-- =====================================================================
-- api_mapas/sql/consultas_referencia.sql  (A3.1 - A3.4, P3.3, P3.4, PO3.7)
-- Alvo: public.mapas (id int4 GENERATED ALWAYS AS IDENTITY, PRIMARY KEY
-- mapas_pkey; graphy geography; metry geometry; CHECK mapas_nao_vazia)
--
-- Revisao PO3 (2026-10-23, D-N5 a D-N7, RS-17 a RS-23): uma linha por id.
--   Q1/Q2  GET de UMA geometria por id, com LIMIT 2 (RS-23)   - leitura
--   W1/W2  PUT: UPDATE da coluna pela PK                       - escrita
--   W3/W4  POST: INSERT da coluna, id da identity, RETURNING id - escrita
--   W1.V/W2.V validacao no banco, antes de W1/W3 (graphy) e W2/W4 (metry)
--   V1     consulta do VerificadorRoleEscrita (RS-20; PO5.C1: 14 colunas) - escrita
-- PRE-REQUISITO (leitura e escrita): a migracao
-- database/migracao_mapas_pk.sql aplicada. Sem ela, o mesmo id pode ter
-- varias linhas: o GET responde 500 (2 linhas) e o verificador desliga a
-- escrita (sem PK exata).
--
-- Nenhum DDL/DML foi executado pelo DataBrain no servidor. Os textos
-- foram conferidos num container postgis/postgis:17-3.5 descartavel.
--
-- Uso na API: copiar os textos EXATOS abaixo como constantes, uma por
-- coluna, escolhida por switch (a coluna nunca vem do usuario e nunca e
-- concatenada). Parametros (Npgsql reescreve @nome para $n):
--   @id    NpgsqlDbType.Integer  (id ja validado, 1..2147483647)
--   @wkt   NpgsqlDbType.Text     (WKT ja validado, ate 10.000 caracteres)
--   @srid  NpgsqlDbType.Integer  (SRID ja validado pela politica da coluna)
--
-- Contrato do GET (Q2 da PU; AJ-1..AJ-3):
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
--   preenchida  (coluna 8, nova) true se a coluna da rota tem geometria
--
-- Observacoes:
--   * ST_Transform para 4326 depende de 4674/3857/31983 em spatial_ref_sys
--     (conferido em M3.3). SRID 0 nao passa por ST_Transform (CASE).
--   * CASE do PostgreSQL so avalia o ramo escolhido por linha, entao o
--     ST_Transform nunca roda para SRID 0 (nem para geometria NULL).
-- =====================================================================


-- ---------------------------------------------------------------------
-- Q1 - GET /api/mapas/{id}/graphy   (conexao api_mapas_leitura, @id)
-- Q2 - GET /api/mapas/{id}/metry
-- ---------------------------------------------------------------------
-- D-N7 / RS-23. A subconsulta busca as linhas do id SEM filtrar a coluna
-- (assim um id duplicado e detectado mesmo que so uma linha tenha a
-- coluna) e com LIMIT 2 (basta saber se ha mais de uma). Resultado, nesta
-- ordem:
--   0 linhas                   -> 404 "Mapa nao encontrado"
--   2 linhas                   -> 500 generico + log Error proprio
--                                 (migracao nao aplicada; RS-23)
--   1 linha, preenchida = f    -> 404 "Mapa nao encontrado" (coluna NULL)
--   1 linha, preenchida = t    -> 200 com o objeto unico (Q2 da PU)
-- Ler "preenchida" (indice 8) ANTES das demais colunas: com a coluna NULL
-- as colunas 0 a 7 vem NULL. O teto ApiMapas:LimiteBytesGeometrias vale
-- para o item (acima dele: 500 com log Warning).
-- Sem ORDER BY: com a PK ha no maximo 1 linha; com 2 linhas a resposta e
-- erro, entao a ordem nao importa. Com a tabela grande o planner usa o
-- indice de mapas_pkey; com poucas linhas pode preferir seq scan.

-- Q1 (graphy)
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
            THEN ST_Y(ST_Transform(t.g, 4326)) END              AS latitude,
       (t.g IS NOT NULL)                                        AS preenchida
FROM (SELECT m.graphy::geometry AS g
      FROM public.mapas AS m
      WHERE m.id = @id
      LIMIT 2) AS t;

-- Q2 (metry)
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
            THEN ST_Y(ST_Transform(t.g, 4326)) END              AS latitude,
       (t.g IS NOT NULL)                                        AS preenchida
FROM (SELECT m.metry AS g
      FROM public.mapas AS m
      WHERE m.id = @id
      LIMIT 2) AS t;


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
-- Verificacoes opcionais (somente leitura, NAO executadas no servidor)
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
-- Constraints e indices em public.mapas. Esperado depois da migracao:
-- mapas_pkey (p) e mapas_nao_vazia (c); indice so o mapas_pkey.
-- SELECT conname, contype, pg_get_constraintdef(oid) FROM pg_constraint
-- WHERE conrelid = 'public.mapas'::regclass;
-- SELECT indexname, indexdef FROM pg_indexes
-- WHERE schemaname = 'public' AND tablename = 'mapas';
--
-- (A sugestao antiga de indice ix_mapas_id ficou obsoleta: a PK ja cria o
-- indice em id, e a migracao remove o ix_mapas_id se ele existir.)


-- ---------------------------------------------------------------------
-- SUBSTITUIDO (P3.4): a FONTE OFICIAL das roles api_mapas_leitura,
-- api_mapas_escrita e mapas_teste e database/roles.sql (revisao 5 no
-- PO3). Usar SOMENTE aquele script. Procedimento do DBA:
-- database/README.md. O bloco historico da A3.4 que ficava aqui foi
-- removido por estar desatualizado.
-- ---------------------------------------------------------------------


-- =====================================================================
-- ESCRITA - PUT /api/mapas/{id}/graphy|metry e POST /api/mapas/graphy|metry
-- (D-N5, D-N6; RS-17, RS-18, RS-19; CN-9, CN-12)
-- =====================================================================
-- Conexao: SOMENTE a de escrita (role api_mapas_escrita, PG_ESCRITA_*).
-- O GET (Q1/Q2) continua so na conexao de leitura.
-- Privilegios usados (database/roles.sql 4.3, revisao 5): SELECT (id,
-- graphy, metry), UPDATE (graphy, metry), INSERT (graphy, metry). Sem
-- DELETE, sem INSERT em id, nada na sequencia. Sem advisory lock (saiu
-- com a PK; o lock de linha do UPDATE pela PK basta).
--
-- Mensagens: a role tem client_min_messages = warning (roles.sql, RP-3),
-- entao os NOTICE do ST_IsValid (que citam coordenadas do WKT) nao chegam
-- ao cliente. Mesmo assim, a API nunca registra MessageText/Detail/Hint.
--
-- ---------------------------------------------------------------------
-- Etapa 1 - Validacao no banco, ANTES de abrir a transacao, na mesma
-- conexao de escrita (D-P6, RP-4). Uma geometria por requisicao: um
-- comando simples (sem lote). Erros esperados: XX000 (parse do WKT; SRID
-- que nao e lon/lat no cast para geography) e classe 22 (dados
-- invalidos) -> 400 "Geometria invalida" (sem indice). So o SqlState vai
-- para o log. Resultado: 1 linha (valida boolean, tipo text).
--   valida = false            -> 400 "geometria invalida segundo o PostGIS (ST_IsValid)"
--   tipo <> tipo do WKT local -> 400 "tipo devolvido pelo banco diverge do WKT"
-- ---------------------------------------------------------------------
-- W1.V (graphy; usada pelo PUT W1 e pelo POST W3) - o cast
-- ::geography::geometry faz o erro de conversao para geography aparecer
-- AQUI, e nao no UPDATE/INSERT. ATENCAO (PostGIS 3.5.2): o cast para
-- geography NAO falha com longitude/latitude fora de [-180,180]/[-90,90];
-- a faixa e responsabilidade do validador local (D-P5), que roda antes.
SELECT ST_IsValid(v.g) AS valida, GeometryType(v.g) AS tipo FROM (SELECT ST_GeomFromText(@wkt, @srid)::geography::geometry AS g) AS v;
--
-- W2.V (metry; usada pelo PUT W2 e pelo POST W4)
SELECT ST_IsValid(v.g) AS valida, GeometryType(v.g) AS tipo FROM (SELECT ST_GeomFromText(@wkt, @srid) AS g) AS v;
--
-- ---------------------------------------------------------------------
-- Etapa 2 - Transacao (READ COMMITTED), nesta ordem exata:
--   BEGIN (BeginTransaction(IsolationLevel.ReadCommitted))
--   -> W*.1 SET LOCAL lock_timeout = '5s'   (RP-3; logo depois do BEGIN)
--   -> W*.2 UPDATE (PUT) ou INSERT ... RETURNING id (POST)
--   -> COMMIT
-- Qualquer erro, cancelamento ou prazo de 30 s estourado -> ROLLBACK.
-- ---------------------------------------------------------------------
--
-- ---------------- W1 - PUT /api/mapas/{id}/graphy -------------------
-- W1.1
SET LOCAL lock_timeout = '5s';
-- W1.2
UPDATE public.mapas SET graphy = ST_GeomFromText(@wkt, @srid)::geography WHERE id = @id;
--
-- ---------------- W2 - PUT /api/mapas/{id}/metry --------------------
-- W2.1
SET LOCAL lock_timeout = '5s';
-- W2.2
UPDATE public.mapas SET metry = ST_GeomFromText(@wkt, @srid) WHERE id = @id;
--
-- Resultado de W1.2/W2.2 (rows affected) - D-N6, RS-19:
--   0  -> ROLLBACK, 404 "Mapa nao encontrado" (o PUT nunca cria id, D-P3)
--   1  -> COMMIT, 204
--   >1 -> ROLLBACK, 500 generico + log Error (so possivel sem a PK)
--   55P03 (lock de linha > 5 s) -> ROLLBACK, 409 "Atualizacao concorrente"
-- So muda a coluna da rota; a outra coluna da linha nunca e tocada. Pode
-- preencher uma coluna que estava NULL (Q3). Idempotente.
--
-- ---------------- W3 - POST /api/mapas/graphy -----------------------
-- W3.1
SET LOCAL lock_timeout = '5s';
-- W3.2
INSERT INTO public.mapas (graphy) VALUES (ST_GeomFromText(@wkt, @srid)::geography) RETURNING id;
--
-- ---------------- W4 - POST /api/mapas/metry ------------------------
-- W4.1
SET LOCAL lock_timeout = '5s';
-- W4.2
INSERT INTO public.mapas (metry) VALUES (ST_GeomFromText(@wkt, @srid)) RETURNING id;
--
-- Resultado de W3.2/W4.2 (D-N5; RS-17, RS-18):
--   1 linha (id int4) -> COMMIT, 201, Location /api/mapas/{id}/{coluna},
--                        montado a partir do int do RETURNING
--   23505 em mapas_pkey (PostgresException.ConstraintName) -> ROLLBACK,
--         500 + log Error so com SqlState e constraint, SEM retry (id da
--         sequencia ja usado: INSERT manual com OVERRIDING SYSTEM VALUE a
--         frente dela, R-N3; o DBA corrige com o passo 7 da migracao)
--   23505 em outra constraint, 23514 (CHECK, ex.: mapas_nao_vazia) -> 500
--   55P03 / 40P01 -> ROLLBACK, 409 com os textos do PUT, log Warning
--   57014, prazo de 30 s, pool esgotado -> 500
-- A outra coluna fica NULL. Nao e idempotente: cada POST gera id novo e
-- consome um valor da sequencia, mesmo com ROLLBACK (pode haver lacunas
-- nos ids; isso e normal).
--
-- Notas:
--   * O INSERT nao informa id: a coluna e GENERATED ALWAYS e o valor vem
--     da identity, que nao exige privilegio na sequencia (RS-8,
--     database/prova_identity_sem_usage.sql). Um id explicito falharia
--     com 428C9 (sem OVERRIDING) ou 42501 (sem INSERT em id).
--   * Teste manual no psql: os textos acima nao rodam direto no psql (o
--     @nome e do Npgsql). Para testar, trocar @id/@wkt/@srid por valores
--     literais, dentro de BEGIN ... ROLLBACK.
-- =====================================================================


-- =====================================================================
-- V1 - VerificadorRoleEscrita (RS-20, D-N10), na conexao de ESCRITA,
-- uma vez na subida. SQL constante, sem parametros, so catalogo.
-- =====================================================================
-- Substitui SqlAtributosRole: as colunas 0 a 5 sao as mesmas de hoje
-- (R-1/C4); 6 a 9 vieram na PO3; 10 a 13 vieram na PO5.C1 (achado 3 do
-- SecBrain), depois das antigas, sem mudar os indices 0 a 9.
-- A escrita fica HABILITADA so se:
--   colunas 0 a 7 = false  E  colunas 8 e 9 = true  E  colunas 10 a 13 = false.
-- Qualquer outro resultado (ou nenhuma linha, ou erro) -> escrita
-- DESLIGADA com log Warning e DataSource.Clear(); o GET continua.
--   0 rolsuper        1 rolbypassrls     2 rolcreaterole
--   3 pode_truncate   4 escreve_tudo (pg_write_all_data)
--   5 dona_tabela
--   6 pode_delete     DELETE em public.mapas (revogado, Q4)
--   7 insere_id       INSERT na coluna id (RS-9)
--   8 pk_exata        PK mapas_pkey exata em id (mesmos criterios da
--                     migracao e da guarda 0.2 do roles.sql)
--   9 id_always       id GENERATED ALWAYS (attidentity = 'a'; Q5).
--                     Coluna recomendada pelo DataBrain alem do RS-20:
--                     confirma que a migracao terminou (passo 8).
--  10 atualiza_id    UPDATE na coluna id (direto na coluna ou herdado de
--                    UPDATE na tabela inteira); o roles.sql so da
--                    UPDATE (graphy, metry)
--  11 rolcreatedb    CREATEDB
--  12 rolreplication REPLICATION
--  13 membro_de_role a role e membro de QUALQUER outra role
--                    (pg_auth_members.member = oid da role; o roles.sql
--                    garante 0 memberships, C2(a) e 5.3)
-- Todas as colunas sao boolean NOT NULL (has_*_privilege e EXISTS nunca
-- devolvem NULL aqui; "= 'a'" sobre attidentity tambem nao).
SELECT r.rolsuper, r.rolbypassrls, r.rolcreaterole, has_table_privilege(current_user, 'public.mapas', 'TRUNCATE') AS pode_truncate, pg_has_role(current_user, 'pg_write_all_data', 'USAGE') AS escreve_tudo, EXISTS (SELECT 1 FROM pg_catalog.pg_class AS c WHERE c.oid = 'public.mapas'::regclass AND c.relowner = r.oid) AS dona_tabela, has_table_privilege(current_user, 'public.mapas', 'DELETE') AS pode_delete, has_column_privilege(current_user, 'public.mapas', 'id', 'INSERT') AS insere_id, EXISTS (SELECT 1 FROM pg_catalog.pg_constraint AS k JOIN pg_catalog.pg_index AS i ON i.indexrelid = k.conindid JOIN pg_catalog.pg_attribute AS a ON a.attrelid = k.conrelid AND a.attname = 'id' AND NOT a.attisdropped WHERE k.conrelid = 'public.mapas'::regclass AND k.contype = 'p' AND k.conname = 'mapas_pkey' AND k.conkey = ARRAY[a.attnum] AND NOT k.condeferrable AND i.indisvalid AND i.indisready AND i.indisunique AND i.indisprimary AND i.indpred IS NULL AND i.indexprs IS NULL) AS pk_exata, EXISTS (SELECT 1 FROM pg_catalog.pg_attribute AS a WHERE a.attrelid = 'public.mapas'::regclass AND a.attname = 'id' AND NOT a.attisdropped AND a.attidentity = 'a') AS id_always, has_column_privilege(current_user, 'public.mapas', 'id', 'UPDATE') AS atualiza_id, r.rolcreatedb, r.rolreplication, EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members AS m WHERE m.member = r.oid) AS membro_de_role FROM pg_catalog.pg_roles AS r WHERE r.rolname = current_user;
-- =====================================================================
