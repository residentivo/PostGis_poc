-- =====================================================================
-- database/relatorio_duplicados_mapas.sql  (PO3.4 - D-N3, Q6, RS-24)
-- Base: casaos | Alvo: public.mapas
--
-- SOMENTE LEITURA: roda numa transacao READ ONLY (REPEATABLE READ, uma
-- foto unica da tabela) terminada em ROLLBACK. Nao cria objeto, nao
-- grava nada e NAO MOSTRA GEOMETRIA: so ids, contagens, tipo, SRID,
-- numero de pontos, ctid e um hash (md5 do EWKB) para diferenciar linhas.
--
-- Passo 3 do PU-DBA (antes da mescla e da migracao) e, de novo, antes do
-- VALIDATE do CHECK (esperado: 0 linhas vazias).
--
-- Classificacao por id com mais de uma linha (Q6):
--   mesclavel   no maximo 1 linha com graphy E no maximo 1 com metry:
--               cabe numa linha so sem perder dado (modelo em
--               database/mescla_duplicados_mapas.sql).
--   conflitante 2+ linhas com graphy ou 2+ com metry: o USUARIO escolhe
--               qual linha manter (secao R4 abaixo + modelo de
--               conflitante no arquivo da mescla).
--
-- OS AGENTES NAO EXECUTAM ESTE SCRIPT. Execucao (casaos por socket local,
-- ou api_mapas_leitura, que ja e somente leitura):
--
-- ATENCAO (PO5.C1, achado 5): a secao R2 (sequencia) SO E CONFIAVEL
-- RODANDO COMO casaos. A api_mapas_leitura (e as outras roles da API)
-- nao tem SELECT/USAGE na sequencia do id; para elas o pg_sequences
-- devolve last_value NULL, que pareceria "sequencia nunca usada". Nesse
-- caso a R2 mostra "sem privilegio" em last_value e proximo_valor e
-- r2_confiavel = f. R1, R3, R4 e R5 valem com qualquer das duas roles.
--   sudo docker exec -i <container> psql -X -v ON_ERROR_STOP=1 \
--        -U casaos -d casaos -f - < database/relatorio_duplicados_mapas.sql
--
-- ONDE NAO SALVAR A SAIDA (RS-24): a saida tem ids e hashes dos dados.
-- NUNCA redirecionar para dentro do repositorio (PostGis_poc) nem para
-- pasta do OneDrive, e nunca colar em tarefas/*.md. Ler no terminal ou,
-- se precisar guardar, gravar em ~/backups_postgis (umask 077), junto
-- dos backups, e apagar depois da migracao.
-- =====================================================================

\set ON_ERROR_STOP on
\pset pager off

BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL statement_timeout = '5min';

-- ---------------------------------------------------------------------
-- R1 Resumo geral
--   linhas_vazias > 0 impede o VALIDATE do CHECK mapas_nao_vazia.
--   ids_duplicados = 0 e ids_nulos = 0: a migracao pode rodar.
-- ---------------------------------------------------------------------
WITH por_id AS (
    SELECT id,
           count(*)                                    AS linhas,
           count(*) FILTER (WHERE graphy IS NOT NULL)  AS com_graphy,
           count(*) FILTER (WHERE metry  IS NOT NULL)  AS com_metry
    FROM public.mapas
    WHERE id IS NOT NULL
    GROUP BY id
)
SELECT (SELECT count(*) FROM public.mapas)                                   AS total_linhas,
       (SELECT count(*) FROM public.mapas WHERE id IS NULL)                  AS linhas_id_nulo,
       (SELECT count(*) FROM public.mapas
         WHERE graphy IS NULL AND metry IS NULL)                             AS linhas_vazias,
       count(*)                                                              AS ids_distintos,
       count(*) FILTER (WHERE linhas > 1)                                    AS ids_duplicados,
       count(*) FILTER (WHERE linhas > 1 AND com_graphy <= 1 AND com_metry <= 1) AS ids_mesclaveis,
       count(*) FILTER (WHERE linhas > 1 AND (com_graphy > 1 OR com_metry > 1))  AS ids_conflitantes,
       max(id)                                                               AS max_id
FROM por_id;

-- ---------------------------------------------------------------------
-- R2 Sequencia da identity (para conferir se ha id a frente dela)
--   proximo_valor <= max_id: o proximo INSERT sem id colidiria. A
--   migracao corrige com setval (passo 7).
--   SO CONFIAVEL COMO casaos (achado 5). Sem SELECT/USAGE na sequencia
--   (ex.: api_mapas_leitura), last_value e proximo_valor saem como
--   'sem privilegio' e r2_confiavel = f: rodar de novo como casaos.
--   last_value 'nunca usada' = sequencia sem nenhum nextval (com
--   privilegio); proximo_valor e entao o start_value.
--   (pg_sequences usa o mesmo teste: has_sequence_privilege 'SELECT,USAGE'.)
-- ---------------------------------------------------------------------
SELECT s.schemaname || '.' || s.sequencename                          AS sequencia,
       CASE WHEN NOT p.tem_privilegio THEN 'sem privilegio'
            WHEN s.last_value IS NULL THEN 'nunca usada'
            ELSE s.last_value::text END                               AS last_value,
       CASE WHEN NOT p.tem_privilegio THEN 'sem privilegio'
            WHEN s.last_value IS NULL THEN s.start_value::text
            ELSE (s.last_value + s.increment_by)::text END            AS proximo_valor,
       (SELECT max(id) FROM public.mapas)                             AS max_id,
       s.max_value,
       p.tem_privilegio                                               AS r2_confiavel
FROM pg_sequences s
CROSS JOIN LATERAL (
    SELECT has_sequence_privilege(current_user,
               pg_get_serial_sequence('public.mapas', 'id'),
               'SELECT,USAGE') AS tem_privilegio
) AS p
WHERE (s.schemaname || '.' || s.sequencename) = pg_get_serial_sequence('public.mapas', 'id');

-- ---------------------------------------------------------------------
-- R3 Um registro por id duplicado (sem geometria)
--   linhas_vazias_no_id: linhas do id sem graphy e sem metry (a mescla as
--   apaga; sobram so se TODAS as linhas do id forem vazias - fica_vazio).
-- ---------------------------------------------------------------------
SELECT id,
       count(*)                                                   AS linhas,
       count(*) FILTER (WHERE graphy IS NOT NULL)                 AS com_graphy,
       count(*) FILTER (WHERE metry  IS NOT NULL)                 AS com_metry,
       count(*) FILTER (WHERE graphy IS NOT NULL AND metry IS NOT NULL) AS com_as_duas,
       count(*) FILTER (WHERE graphy IS NULL AND metry IS NULL)   AS linhas_vazias_no_id,
       CASE WHEN count(*) FILTER (WHERE graphy IS NOT NULL) <= 1
             AND count(*) FILTER (WHERE metry  IS NOT NULL) <= 1
            THEN 'mesclavel' ELSE 'conflitante' END               AS classificacao,
       (count(*) FILTER (WHERE graphy IS NOT NULL) = 0
        AND count(*) FILTER (WHERE metry IS NOT NULL) = 0)        AS fica_vazio
FROM public.mapas
WHERE id IS NOT NULL
GROUP BY id
HAVING count(*) > 1
ORDER BY classificacao DESC, id;

-- ---------------------------------------------------------------------
-- R4 Detalhe das linhas dos ids CONFLITANTES, para o usuario escolher
--   qual manter (sem coordenadas). Use o ctid + hash no modelo de
--   conflitante de database/mescla_duplicados_mapas.sql. ATENCAO: o ctid
--   muda depois de UPDATE/VACUUM FULL; o modelo confere o hash antes de
--   apagar. Para ver a geometria de uma linha, o usuario consulta
--   manualmente no terminal (nunca salvar).
-- ---------------------------------------------------------------------
WITH conflitantes AS (
    SELECT id
    FROM public.mapas
    WHERE id IS NOT NULL
    GROUP BY id
    HAVING count(*) > 1
       AND (count(*) FILTER (WHERE graphy IS NOT NULL) > 1
            OR count(*) FILTER (WHERE metry IS NOT NULL) > 1)
)
SELECT m.id,
       m.ctid                                                       AS ctid,
       GeometryType(m.graphy::geometry)                             AS graphy_tipo,
       ST_SRID(m.graphy::geometry)                                  AS graphy_srid,
       ST_NPoints(m.graphy::geometry)                               AS graphy_pontos,
       md5(ST_AsEWKB(m.graphy::geometry))                           AS graphy_md5,
       GeometryType(m.metry)                                        AS metry_tipo,
       ST_SRID(m.metry)                                             AS metry_srid,
       ST_NPoints(m.metry)                                          AS metry_pontos,
       md5(ST_AsEWKB(m.metry))                                      AS metry_md5
FROM public.mapas m
JOIN conflitantes c ON c.id = m.id
ORDER BY m.id, m.ctid;

-- ---------------------------------------------------------------------
-- R5 Linhas vazias fora dos ids duplicados (id com uma linha so, sem
--   graphy e sem metry). Impedem o VALIDATE; o usuario decide se apaga.
-- ---------------------------------------------------------------------
SELECT m.id
FROM public.mapas m
WHERE m.graphy IS NULL AND m.metry IS NULL
  AND NOT EXISTS (SELECT 1 FROM public.mapas o WHERE o.id = m.id AND o.ctid <> m.ctid)
ORDER BY m.id;

ROLLBACK;
