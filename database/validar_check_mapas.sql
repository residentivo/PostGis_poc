-- =====================================================================
-- database/validar_check_mapas.sql  (PO3.3 - RS-13)
-- Base: casaos | Alvo: CHECK mapas_nao_vazia de public.mapas
--
-- Passo 7 do PU-DBA, DEPOIS da migracao (database/migracao_mapas_pk.sql)
-- e de o relatorio (database/relatorio_duplicados_mapas.sql) mostrar 0
-- linhas vazias (graphy e metry NULL na mesma linha).
--
-- OS AGENTES NAO EXECUTAM ESTE SCRIPT. Execucao (superusuario casaos,
-- socket local), numa unica transacao:
--   sudo docker exec -i <container> psql -X -v ON_ERROR_STOP=1 -1 \
--        -U casaos -d casaos -f - < database/validar_check_mapas.sql
--
-- O VALIDATE CONSTRAINT usa lock SHARE UPDATE EXCLUSIVE: leituras e
-- escritas continuam. Se houver linha vazia, o script aborta ANTES do
-- VALIDATE, mostrando so a quantidade e no maximo 20 ids (sem geometria).
-- Idempotente: com o CHECK ja validado, nao faz nada.
-- =====================================================================

\set ON_ERROR_STOP on

SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '5min';

DO $$
DECLARE
    v_validado boolean;
    v_vazias   bigint;
    v_amostra  text;
BEGIN
    IF current_database() <> 'casaos' THEN
        RAISE EXCEPTION 'validar_check: deve rodar na base casaos (atual: %)', current_database();
    END IF;

    SELECT k.convalidated INTO v_validado
    FROM pg_constraint k
    WHERE k.conrelid = 'public.mapas'::regclass
      AND k.conname = 'mapas_nao_vazia'
      AND k.contype = 'c'
      AND pg_get_expr(k.conbin, k.conrelid) = '((graphy IS NOT NULL) OR (metry IS NOT NULL))';

    IF NOT FOUND THEN
        RAISE EXCEPTION 'validar_check: CHECK mapas_nao_vazia (graphy IS NOT NULL OR metry IS NOT NULL) nao encontrado. Rode antes database/migracao_mapas_pk.sql.';
    END IF;

    IF v_validado THEN
        RAISE NOTICE 'validar_check: mapas_nao_vazia ja esta validado - nada a fazer';
        RETURN;
    END IF;

    SELECT count(*) INTO v_vazias FROM public.mapas WHERE graphy IS NULL AND metry IS NULL;
    IF v_vazias > 0 THEN
        SELECT string_agg(x.id::text, ', ' ORDER BY x.id) INTO v_amostra
        FROM (SELECT id FROM public.mapas WHERE graphy IS NULL AND metry IS NULL
              ORDER BY id LIMIT 20) AS x;
        RAISE EXCEPTION 'validar_check: % linha(s) com graphy e metry NULL (primeiros ids, max. 20: %). Nada foi alterado. Decidir com o usuario (apagar ou preencher) e rodar de novo.',
            v_vazias, v_amostra;
    END IF;

    ALTER TABLE public.mapas VALIDATE CONSTRAINT mapas_nao_vazia;

    IF NOT (SELECT convalidated FROM pg_constraint
            WHERE conrelid = 'public.mapas'::regclass AND conname = 'mapas_nao_vazia') THEN
        RAISE EXCEPTION 'validar_check: VALIDATE nao marcou o CHECK como validado';
    END IF;

    RAISE NOTICE 'validar_check: mapas_nao_vazia validado';
END
$$;

-- Esperado: convalidated = t e a definicao sem "NOT VALID".
SELECT conname, pg_get_constraintdef(oid) AS definicao, convalidated
FROM pg_constraint
WHERE conrelid = 'public.mapas'::regclass AND conname = 'mapas_nao_vazia';
