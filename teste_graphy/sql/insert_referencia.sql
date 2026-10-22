-- =====================================================================
-- teste_graphy/sql/insert_referencia.sql  (F3.5)
-- SQL oficial de referencia para o backend (public.mapas.graphy).
-- Os parametros @wkt e @id sao placeholders do Npgsql (NpgsqlParameter):
--   @wkt -> text (WKT, ex.: 'POINT(-46.63 -23.55)'), @id -> integer.
-- NUNCA concatenar valores na string SQL. SRID 4326 e literal (nao parametro).
-- Este arquivo nao e executavel diretamente no psql (usa sintaxe @param).
-- =====================================================================

-- 1) Validacao previa do WKT (rejeitar se ST_IsValid = false ou tipo inesperado)
SELECT ST_IsValid(ST_GeomFromText(@wkt, 4326)) AS valido,
       GeometryType(ST_GeomFromText(@wkt, 4326)) AS tipo;

-- 2) Insercao - retorna o id gerado pela identity
INSERT INTO public.mapas (graphy)
VALUES (ST_GeomFromText(@wkt, 4326)::geography)
RETURNING id;

-- 3) Leitura de conferencia (SRID esperado: 4326)
SELECT id,
       ST_AsText(graphy)       AS wkt,
       ST_SRID(graphy::geometry) AS srid
FROM public.mapas
WHERE id = @id;

-- 4) Limpeza - SOMENTE PARA TESTES DE INTEGRACAO.
-- public.mapas NAO tem PK nem UNIQUE em id (S3): o chamador deve conferir
-- rowcount = 1; se for diferente, tratar como falha (e usar transacao com
-- ROLLBACK quando possivel).
DELETE FROM public.mapas
WHERE id = @id;
