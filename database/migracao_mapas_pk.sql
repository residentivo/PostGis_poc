-- =====================================================================
-- database/migracao_mapas_pk.sql  (PO3.2 - D-N3; RS-10, RS-12, RS-13, RS-14)
-- Base: casaos | Alvo: public.mapas (id int4 identity, graphy, metry)
--
-- O QUE FAZ: public.mapas.id passa a ser PRIMARY KEY (mapas_pkey), uma
-- linha por id, com "GENERATED ALWAYS AS IDENTITY" (Q5), a sequencia
-- ajustada para nunca gerar um id ja usado e o CHECK mapas_nao_vazia
-- (graphy IS NOT NULL OR metry IS NOT NULL) criado NOT VALID (RS-13).
--
-- OS AGENTES NAO EXECUTAM ESTE SCRIPT. Quem executa e o usuario/DBA, no
-- passo 6 do procedimento PU-DBA (database/README.md), DEPOIS de:
--   1. desligar a escrita antiga (PG_ESCRITA_* removido e/ou
--      ALTER ROLE api_mapas_escrita NOLOGIN + pg_terminate_backend);
--   2. backup (database/backup_mapas.sh / .ps1);
--   3. relatorio (database/relatorio_duplicados_mapas.sql);
--   4. deduplicacao (database/mescla_duplicados_mapas.sql e escolha dos
--      conflitantes);
--   5. segundo backup, se houver intervalo grande desde o primeiro.
--
-- EXECUCAO (superusuario casaos, socket local dentro do container), numa
-- UNICA transacao (-1) que para no primeiro erro (ON_ERROR_STOP):
--   sudo docker exec -i <container> psql -X -v ON_ERROR_STOP=1 -1 \
--        -U casaos -d casaos -f - < database/migracao_mapas_pk.sql
-- O COMMIT (passo 12 do D-N3) e feito pelo proprio psql no fim do -1.
-- Qualquer erro (guarda, lock_timeout, duplicado, conferencia) faz o psql
-- dar ROLLBACK: NADA e alterado.
-- NAO coloque BEGIN/COMMIT neste arquivo (quebraria o -1). Se o script
-- for rodado SEM -1, o LOCK TABLE do passo 2 falha com "LOCK TABLE can
-- only be used in transaction blocks" e nada e alterado.
--
-- IDEMPOTENTE: rodar de novo nao muda nada. Cada passo ja aplicado e
-- pulado com NOTICE. A PK so conta como existente se for EXATA (ver
-- guarda 3.3); outra PK, UNIQUE, EXCLUSION ou indice unico aborta.
--
-- SAIDA: so contagens, ids (no maximo 20) e nomes de objetos. Nenhuma
-- geometria e mostrada.
-- Bloqueio: ACCESS EXCLUSIVE durante a transacao (leituras e escritas em
-- public.mapas esperam). Com poucas linhas, dura menos de 1 s.
-- =====================================================================

\set ON_ERROR_STOP on

-- ---------------------------------------------------------------------
-- 1. Limites da transacao (SET LOCAL: valem so ate o COMMIT/ROLLBACK)
-- ---------------------------------------------------------------------
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '5min';

-- ---------------------------------------------------------------------
-- 2. Lock exclusivo da tabela (ninguem le nem grava ate o fim)
--    Espera > 5 s: erro 55P03 e ROLLBACK. Ver quem segura o lock:
--    SELECT pid, usename, application_name, state FROM pg_stat_activity
--    WHERE pid = ANY (pg_blocking_pids(<pid desta sessao>));
-- ---------------------------------------------------------------------
LOCK TABLE public.mapas IN ACCESS EXCLUSIVE MODE;

-- ---------------------------------------------------------------------
-- 3. Guardas (abortam sem alterar nada)
-- ---------------------------------------------------------------------
DO $$
DECLARE
    v_identity "char";
    v_extras   text;
    v_faltando text;
    v_tipos    text;
    v_sessoes  int;
BEGIN
    -- 3.1 Base, executor e tabela
    IF current_database() <> 'casaos' THEN
        RAISE EXCEPTION 'migracao: deve rodar na base casaos (atual: %)', current_database();
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = current_user AND rolsuper) THEN
        RAISE EXCEPTION 'migracao: exige superusuario (atual: %)', current_user;
    END IF;
    IF to_regclass('public.mapas') IS NULL THEN
        RAISE EXCEPTION 'migracao: tabela public.mapas nao encontrada';
    END IF;

    -- 3.2 RP-2: exatamente as colunas id int4, graphy geography e metry geometry
    SELECT string_agg(quote_ident(a.attname), ', ' ORDER BY a.attnum)
      INTO v_extras
    FROM pg_attribute a
    WHERE a.attrelid = 'public.mapas'::regclass
      AND a.attnum > 0 AND NOT a.attisdropped
      AND a.attname NOT IN ('id', 'graphy', 'metry');
    IF v_extras IS NOT NULL THEN
        RAISE EXCEPTION 'RP-2: public.mapas tem colunas alem de id, graphy e metry: %', v_extras;
    END IF;

    SELECT string_agg(c.nome, ', ')
      INTO v_faltando
    FROM (VALUES ('id'), ('graphy'), ('metry')) AS c(nome)
    WHERE NOT EXISTS (SELECT 1 FROM pg_attribute a
                      WHERE a.attrelid = 'public.mapas'::regclass
                        AND a.attname = c.nome AND a.attnum > 0 AND NOT a.attisdropped);
    IF v_faltando IS NOT NULL THEN
        RAISE EXCEPTION 'RP-2: public.mapas sem a(s) coluna(s): %', v_faltando;
    END IF;

    SELECT string_agg(format('%s = %s', a.attname, t.typname), ', ')
      INTO v_tipos
    FROM pg_attribute a
    JOIN pg_type t ON t.oid = a.atttypid
    WHERE a.attrelid = 'public.mapas'::regclass
      AND a.attnum > 0 AND NOT a.attisdropped
      AND ((a.attname = 'graphy' AND t.typname <> 'geography')
        OR (a.attname = 'metry'  AND t.typname <> 'geometry')
        OR (a.attname = 'id'     AND t.typname <> 'int4'));
    IF v_tipos IS NOT NULL THEN
        RAISE EXCEPTION 'RP-2: tipo inesperado em public.mapas: % (esperado id = int4, graphy = geography, metry = geometry)', v_tipos;
    END IF;

    -- 3.3 id identity (BY DEFAULT 'd' antes da migracao, ALWAYS 'a' depois)
    SELECT a.attidentity INTO v_identity
    FROM pg_attribute a
    WHERE a.attrelid = 'public.mapas'::regclass AND a.attname = 'id' AND NOT a.attisdropped;
    IF v_identity IS NULL OR v_identity NOT IN ('a', 'd') THEN
        RAISE EXCEPTION 'migracao: public.mapas.id nao e identity (attidentity = %)',
            coalesce(nullif(v_identity::text, ''), '<vazio>');
    END IF;
    IF pg_get_serial_sequence('public.mapas', 'id') IS NULL THEN
        RAISE EXCEPTION 'migracao: sequencia da identity de public.mapas.id nao encontrada';
    END IF;

    -- 3.4 RS-14: nenhuma PK/UNIQUE/EXCLUSION fora da PK exata. PK exata =
    --     nome mapas_pkey, contype 'p', conkey = {attnum de id}, nao
    --     deferrable, indice valido, unico, primario, sem predicado e sem
    --     expressao. Qualquer outra forma aborta: o DBA analisa e remove
    --     manualmente (nada e alterado aqui).
    IF EXISTS (
        SELECT 1
        FROM pg_constraint k
        WHERE k.conrelid = 'public.mapas'::regclass
          AND k.contype IN ('p', 'u', 'x')
          AND NOT (k.contype = 'p'
                   AND k.conname = 'mapas_pkey'
                   AND k.conkey = ARRAY[(SELECT a.attnum FROM pg_attribute a
                                         WHERE a.attrelid = k.conrelid AND a.attname = 'id'
                                           AND NOT a.attisdropped)]
                   AND NOT k.condeferrable
                   AND EXISTS (SELECT 1 FROM pg_index i
                               WHERE i.indexrelid = k.conindid
                                 AND i.indisvalid AND i.indisready AND i.indisunique
                                 AND i.indisprimary AND i.indpred IS NULL
                                 AND i.indexprs IS NULL))) THEN
        RAISE EXCEPTION 'RS-14: public.mapas tem PK/UNIQUE/EXCLUSION diferente da PK exata mapas_pkey (id): %. Analisar e remover manualmente antes de migrar.',
            (SELECT string_agg(format('%s (%s)', k.conname, pg_get_constraintdef(k.oid)), '; ')
             FROM pg_constraint k
             WHERE k.conrelid = 'public.mapas'::regclass AND k.contype IN ('p', 'u', 'x'));
    END IF;

    -- Indice UNICO que nao seja o da PK exata (ex.: CREATE UNIQUE INDEX
    -- sem constraint) tambem aborta.
    IF EXISTS (
        SELECT 1
        FROM pg_index i
        WHERE i.indrelid = 'public.mapas'::regclass
          AND i.indisunique
          AND NOT EXISTS (SELECT 1 FROM pg_constraint k
                          WHERE k.conindid = i.indexrelid
                            AND k.conrelid = 'public.mapas'::regclass
                            AND k.contype = 'p' AND k.conname = 'mapas_pkey')) THEN
        RAISE EXCEPTION 'RS-14: public.mapas tem indice unico fora da PK mapas_pkey: %. Analisar e remover manualmente antes de migrar.',
            (SELECT string_agg(i.indexrelid::regclass::text, ', ')
             FROM pg_index i WHERE i.indrelid = 'public.mapas'::regclass AND i.indisunique);
    END IF;

    -- 3.5 CHECK mapas_nao_vazia: se ja existir, tem de ser exatamente o
    --     esperado (senao aborta).
    IF EXISTS (SELECT 1 FROM pg_constraint k
               WHERE k.conrelid = 'public.mapas'::regclass
                 AND k.conname = 'mapas_nao_vazia'
                 AND (k.contype <> 'c'
                      OR pg_get_expr(k.conbin, k.conrelid)
                         <> '((graphy IS NOT NULL) OR (metry IS NOT NULL))')) THEN
        RAISE EXCEPTION 'RS-13: constraint mapas_nao_vazia existe com definicao diferente da esperada (graphy IS NOT NULL OR metry IS NOT NULL)';
    END IF;

    -- 3.6 RS-11: a escrita antiga tem de estar desligada (nenhuma sessao
    --     de api_mapas_escrita). O PUT antigo inseria com id explicito.
    SELECT count(*) INTO v_sessoes
    FROM pg_stat_activity
    WHERE usename = 'api_mapas_escrita';
    IF v_sessoes > 0 THEN
        RAISE EXCEPTION 'RS-11: ha % sessao(oes) de api_mapas_escrita abertas. Desligue a escrita antiga (PG_ESCRITA_* removido e/ou ALTER ROLE api_mapas_escrita NOLOGIN + pg_terminate_backend) antes de migrar.',
            v_sessoes;
    END IF;

    RAISE NOTICE 'migracao 3: guardas ok (colunas, tipos, identity %, constraints)', v_identity;
END
$$;

-- ---------------------------------------------------------------------
-- 4. Contagem de linhas antes (guardada na transacao para a conferencia)
--    set_config(..., true) = valor local da transacao; nao cria objeto.
-- ---------------------------------------------------------------------
SELECT set_config('migracao_mapas.linhas_antes', count(*)::text, true) AS linhas_antes
FROM public.mapas;

-- ---------------------------------------------------------------------
-- 5. id NULL ou duplicado: RAISE com a quantidade e no maximo 20 ids,
--    nunca geometrias. Nada e alterado. Resolver com o relatorio e a
--    mescla (database/relatorio_duplicados_mapas.sql e
--    database/mescla_duplicados_mapas.sql) e rodar de novo.
-- ---------------------------------------------------------------------
DO $$
DECLARE
    v_nulos      bigint;
    v_dup_ids    bigint;
    v_dup_linhas bigint;
    v_amostra    text;
BEGIN
    SELECT count(*) INTO v_nulos FROM public.mapas WHERE id IS NULL;

    SELECT count(*), coalesce(sum(d.n), 0)
      INTO v_dup_ids, v_dup_linhas
    FROM (SELECT id, count(*) AS n FROM public.mapas
          WHERE id IS NOT NULL GROUP BY id HAVING count(*) > 1) AS d;

    IF v_nulos > 0 OR v_dup_ids > 0 THEN
        SELECT string_agg(x.id::text, ', ' ORDER BY x.id)
          INTO v_amostra
        FROM (SELECT id FROM public.mapas WHERE id IS NOT NULL
              GROUP BY id HAVING count(*) > 1 ORDER BY id LIMIT 20) AS x;

        RAISE EXCEPTION 'migracao 5: % linha(s) com id NULL e % id(s) duplicado(s) em % linha(s). Primeiros ids (max. 20): %. Nada foi alterado; rode o relatorio e a mescla.',
            v_nulos, v_dup_ids, v_dup_linhas, coalesce(v_amostra, '-');
    END IF;

    RAISE NOTICE 'migracao 5: nenhum id NULL ou duplicado';
END
$$;

-- ---------------------------------------------------------------------
-- 6. PRIMARY KEY exata (pula se ja existir; a guarda 3.4 ja garantiu que
--    a unica PK aceita e a exata)
-- ---------------------------------------------------------------------
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint
               WHERE conrelid = 'public.mapas'::regclass
                 AND contype = 'p' AND conname = 'mapas_pkey') THEN
        RAISE NOTICE 'migracao 6: mapas_pkey ja existe (exata) - pulado';
    ELSE
        ALTER TABLE public.mapas ADD CONSTRAINT mapas_pkey PRIMARY KEY (id);
        RAISE NOTICE 'migracao 6: PRIMARY KEY mapas_pkey (id) criada';
    END IF;
END
$$;

-- ---------------------------------------------------------------------
-- 7. Sequencia (RS-12): o proximo valor gerado tem de ser > max(id).
--    Regra: setval(seq, GREATEST(max(id), last_value)), nunca para tras.
--    Na pratica so chama setval quando max(id) >= proximo valor; senao a
--    sequencia ja esta a frente e nao e tocada (re-execucao = no-op).
--    Tabela vazia: nao mexe na sequencia.
--    Limite: aborta se max(id) > 2147483647 - 1000000 (folga de 1 milhao
--    de ids ate o MAXVALUE do int4).
-- ---------------------------------------------------------------------
DO $$
DECLARE
    v_seq      regclass := pg_get_serial_sequence('public.mapas', 'id')::regclass;
    v_max      int4;
    v_last     bigint;
    v_called   boolean;
    v_proximo  bigint;
    v_alvo     bigint;
    c_limite   constant bigint := 2147483647 - 1000000;
BEGIN
    SELECT max(id) INTO v_max FROM public.mapas;
    EXECUTE format('SELECT last_value, is_called FROM %s', v_seq) INTO v_last, v_called;
    v_proximo := CASE WHEN v_called THEN v_last + 1 ELSE v_last END;

    IF v_max IS NULL THEN
        RAISE NOTICE 'migracao 7: tabela vazia - sequencia % nao alterada (proximo = %)', v_seq, v_proximo;
        RETURN;
    END IF;

    IF v_max > c_limite THEN
        RAISE EXCEPTION 'RS-12: max(id) = % acima do limite % (2147483647 - 1000000). Nada foi alterado; analisar com o usuario.', v_max, c_limite;
    END IF;
    IF v_proximo > c_limite THEN
        RAISE WARNING 'RS-12: proximo valor da sequencia % = % ja esta acima de %; restam poucos ids.', v_seq, v_proximo, c_limite;
    END IF;

    IF v_max >= v_proximo THEN
        v_alvo := GREATEST(v_max::bigint, v_last);
        PERFORM setval(v_seq, v_alvo, true);
        RAISE NOTICE 'migracao 7: setval(%, %) - proximo id = % (max(id) = %, antes o proximo era %)',
            v_seq, v_alvo, v_alvo + 1, v_max, v_proximo;
    ELSE
        RAISE NOTICE 'migracao 7: sequencia % ja a frente (proximo = %, max(id) = %) - pulado', v_seq, v_proximo, v_max;
    END IF;
END
$$;

-- ---------------------------------------------------------------------
-- 8. GENERATED ALWAYS (Q5). Depois disso, INSERT com id so com
--    OVERRIDING SYSTEM VALUE (manual, fora da API).
-- ---------------------------------------------------------------------
DO $$
BEGIN
    IF (SELECT attidentity FROM pg_attribute
        WHERE attrelid = 'public.mapas'::regclass AND attname = 'id') = 'a' THEN
        RAISE NOTICE 'migracao 8: id ja e GENERATED ALWAYS - pulado';
    ELSE
        ALTER TABLE public.mapas ALTER COLUMN id SET GENERATED ALWAYS;
        RAISE NOTICE 'migracao 8: id passou a GENERATED ALWAYS';
    END IF;
END
$$;

-- ---------------------------------------------------------------------
-- 9. CHECK mapas_nao_vazia NOT VALID (RS-13): vale para as linhas novas
--    e alteradas; as existentes so sao conferidas no VALIDATE, que e um
--    passo separado (database/validar_check_mapas.sql), depois do
--    relatorio sem linhas vazias.
-- ---------------------------------------------------------------------
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint
               WHERE conrelid = 'public.mapas'::regclass AND conname = 'mapas_nao_vazia') THEN
        RAISE NOTICE 'migracao 9: CHECK mapas_nao_vazia ja existe - pulado';
    ELSE
        ALTER TABLE public.mapas
            ADD CONSTRAINT mapas_nao_vazia CHECK (graphy IS NOT NULL OR metry IS NOT NULL) NOT VALID;
        RAISE NOTICE 'migracao 9: CHECK mapas_nao_vazia criado NOT VALID';
    END IF;
END
$$;

-- ---------------------------------------------------------------------
-- 10. Indice redundante (RS-14): public.ix_mapas_id (sugerido antes em
--     api_mapas/sql/consultas_referencia.sql) so e removido se for
--     EXATAMENTE um indice btree, nao unico, de public.mapas(id), sem
--     predicado nem expressao. Qualquer outra forma fica e gera WARNING.
-- ---------------------------------------------------------------------
DO $$
DECLARE
    v_idx regclass := to_regclass('public.ix_mapas_id');
BEGIN
    IF v_idx IS NULL THEN
        RAISE NOTICE 'migracao 10: public.ix_mapas_id nao existe - pulado';
    ELSIF EXISTS (SELECT 1
                  FROM pg_index i
                  JOIN pg_class c ON c.oid = i.indexrelid
                  JOIN pg_am am ON am.oid = c.relam
                  WHERE i.indexrelid = v_idx
                    AND i.indrelid = 'public.mapas'::regclass
                    AND NOT i.indisunique
                    AND i.indnatts = 1
                    AND i.indkey[0] = (SELECT attnum FROM pg_attribute
                                       WHERE attrelid = 'public.mapas'::regclass AND attname = 'id')
                    AND i.indpred IS NULL AND i.indexprs IS NULL
                    AND am.amname = 'btree') THEN
        DROP INDEX IF EXISTS public.ix_mapas_id;
        RAISE NOTICE 'migracao 10: public.ix_mapas_id (redundante com mapas_pkey) removido';
    ELSE
        RAISE WARNING 'migracao 10: public.ix_mapas_id existe mas NAO e btree simples de public.mapas(id) - mantido; analisar';
    END IF;
END
$$;

-- ---------------------------------------------------------------------
-- 11. Conferencia (RAISE = ROLLBACK de tudo): PK exata, sequencia a
--     frente de max(id), contagem igual a de antes, attidentity = 'a' e
--     CHECK presente.
-- ---------------------------------------------------------------------
DO $$
DECLARE
    v_erros   text[] := '{}';
    v_antes   bigint := current_setting('migracao_mapas.linhas_antes')::bigint;
    v_depois  bigint;
    v_seq     regclass := pg_get_serial_sequence('public.mapas', 'id')::regclass;
    v_max     int4;
    v_last    bigint;
    v_called  boolean;
    v_proximo bigint;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint k
        JOIN pg_index i ON i.indexrelid = k.conindid
        JOIN pg_attribute a ON a.attrelid = k.conrelid AND a.attname = 'id' AND NOT a.attisdropped
        WHERE k.conrelid = 'public.mapas'::regclass
          AND k.contype = 'p' AND k.conname = 'mapas_pkey'
          AND k.conkey = ARRAY[a.attnum]
          AND NOT k.condeferrable
          AND i.indisvalid AND i.indisready AND i.indisunique AND i.indisprimary
          AND i.indpred IS NULL AND i.indexprs IS NULL) THEN
        v_erros := v_erros || 'PK exata mapas_pkey (id) ausente'::text;
    END IF;

    SELECT count(*), max(id) INTO v_depois, v_max FROM public.mapas;
    IF v_depois <> v_antes THEN
        v_erros := v_erros || format('contagem de linhas mudou (antes %s, depois %s)', v_antes, v_depois);
    END IF;

    EXECUTE format('SELECT last_value, is_called FROM %s', v_seq) INTO v_last, v_called;
    v_proximo := CASE WHEN v_called THEN v_last + 1 ELSE v_last END;
    IF v_max IS NOT NULL AND v_proximo <= v_max THEN
        v_erros := v_erros || format('sequencia atras de max(id) (proximo %s, max %s)', v_proximo, v_max);
    END IF;

    IF (SELECT attidentity FROM pg_attribute
        WHERE attrelid = 'public.mapas'::regclass AND attname = 'id') IS DISTINCT FROM 'a' THEN
        v_erros := v_erros || 'id nao e GENERATED ALWAYS (attidentity <> a)'::text;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conrelid = 'public.mapas'::regclass AND conname = 'mapas_nao_vazia'
                     AND contype = 'c') THEN
        v_erros := v_erros || 'CHECK mapas_nao_vazia ausente'::text;
    END IF;

    IF cardinality(v_erros) > 0 THEN
        RAISE EXCEPTION 'migracao 11: conferencia falhou (ROLLBACK, nada gravado): %', array_to_string(v_erros, '; ');
    END IF;

    RAISE NOTICE 'migracao 11: conferencia ok - % linha(s), max(id) = %, proximo id = %, PK exata, GENERATED ALWAYS, CHECK presente',
        v_depois, coalesce(v_max::text, '-'), v_proximo;
END
$$;

-- Resumo (somente catalogo; sem geometria). Esperado:
--   mapas_pkey      | p | PRIMARY KEY (id)                                       | t
--   mapas_nao_vazia | c | CHECK (((graphy IS NOT NULL) OR (metry IS NOT NULL))) NOT VALID | f
--   (convalidated = t depois do VALIDATE)
SELECT conname, contype, pg_get_constraintdef(oid) AS definicao, convalidated
FROM pg_constraint
WHERE conrelid = 'public.mapas'::regclass
ORDER BY contype DESC, conname;

SELECT column_name, is_identity, identity_generation
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'mapas' AND column_name = 'id';

-- 12. COMMIT: feito pelo psql ao final do -1.
-- Proximos passos (PU-DBA): relatorio de novo (esperado: 0 linhas vazias)
-- -> database/validar_check_mapas.sql -> database/roles.sql revisado.
