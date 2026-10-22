-- =====================================================================
-- teste_metry/sql/insert_referencia.sql  (M3.5)
-- SQL oficial de referencia para o backend (public.mapas.metry).
-- Parametros Npgsql (NpgsqlParameter com NpgsqlDbType explicito):
--   @wkt  -> NpgsqlDbType.Text    (WKT 2D, ex.: 'POINT(-46.6333 -23.5505)')
--   @srid -> NpgsqlDbType.Integer (um de 4326, 4674, 3857, 31983; D-M4)
--   @id   -> NpgsqlDbType.Integer (id retornado pelo INSERT)
-- RQ-1: @srid e SEMPRE parametro, em TODOS os comandos abaixo. Nunca
-- literal, nunca concatenado/interpolado. Os textos SQL sao constantes.
-- Sem cast para geography (D-M5). Este arquivo nao e executavel
-- diretamente no psql (usa sintaxe @param).
-- AVISO (C-2): no psql/pgAdmin "@x" e o operador VALOR ABSOLUTO
-- (ex.: "id = @id" vira "id = abs(id)", verdadeiro para TODAS as linhas).
-- NUNCA execute estes comandos com parametros @ fora do Npgsql.
-- =====================================================================

-- 1) Validacao previa no banco (apos a validacao local D-M7).
-- Rejeitar o item se valido = false ou tipo fora da lista permitida.
SELECT ST_IsValid(ST_GeomFromText(@wkt, @srid)) AS valido,
       GeometryType(ST_GeomFromText(@wkt, @srid)) AS tipo;

-- 2) Insercao - retorna o id gerado pela identity (BY DEFAULT)
INSERT INTO public.mapas (metry)
VALUES (ST_GeomFromText(@wkt, @srid))
RETURNING id;

-- 3) Leitura de conferencia (D-M5): o chamador compara a coluna srid com
-- o SRID configurado; divergencia = falha do item (codigo 2).
-- Sem PK/UNIQUE em id (S3): esperar exatamente 1 linha.
SELECT id,
       ST_AsText(metry) AS wkt,
       ST_SRID(metry)   AS srid
FROM public.mapas
WHERE id = @id;

-- 4) Limpeza - SOMENTE NOS TESTES DE INTEGRACAO (RQ-6). PROIBIDO no
-- programa (o programa nunca executa DELETE).
-- Executar no finally, para CADA id devolvido pelo RETURNING, mesmo que
-- um passo anterior tenha falhado. Cada DELETE em sua propria transacao
-- (NpgsqlTransaction), com @id e @srid tipados (Integer):
--   BEGIN;
--   DELETE FROM public.mapas WHERE id = @id AND ST_SRID(metry) = @srid;
--   rowcount = 1  -> COMMIT;
--   rowcount <> 1 -> ROLLBACK; e falha do teste
-- (public.mapas nao tem PK/UNIQUE em id - S3 - por isso a conferencia
-- de rowcount e a condicao adicional por SRID).
-- C-2: bloco COMENTADO de proposito. No psql/pgAdmin "id = @id" vira
-- "id = abs(id)" (verdadeiro para todas as linhas) e apagaria a tabela.
-- Texto de referencia apenas para o NpgsqlCommand dos testes:
--   BEGIN;
--   DELETE FROM public.mapas
--   WHERE id = @id
--     AND ST_SRID(metry) = @srid;
--   -- COMMIT;   -- somente se rowcount = 1
--   -- ROLLBACK; -- se rowcount <> 1 (e reportar falha)

-- ---------------------------------------------------------------------
-- REC-2: limpeza MANUAL por id (para o README). As linhas inseridas pelo
-- programa sao permanentes; use isto apenas para remover uma linha
-- especifica, no psql, substituindo <ID> e <SRID> pelos valores conferidos
-- antes com a consulta 3. Mesma forma do RQ-6: confira que o DELETE
-- informou "DELETE 1" antes do COMMIT; caso contrario, ROLLBACK.
--
--   BEGIN;
--   SELECT id, ST_AsText(metry), ST_SRID(metry) FROM public.mapas WHERE id = <ID>;
--   DELETE FROM public.mapas WHERE id = <ID> AND ST_SRID(metry) = <SRID>;
--   -- saida esperada: DELETE 1
--   COMMIT;    -- se DELETE 1
--   -- ROLLBACK; -- se DELETE 0 ou maior que 1
-- ---------------------------------------------------------------------
