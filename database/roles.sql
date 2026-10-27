-- =====================================================================
-- database/roles.sql  (acoes 3, 4 e 5 do DBA - libera F6.6, M6.6 e A7.3)
-- Base: casaos | Alvo: public.mapas (id int4 identity, graphy, metry)
-- Servidor: 192.168.168.106 (container Docker do CasaOS), PG 17.x,
-- PostGIS 3.5.2.
-- Revisao 2: correcoes C1-C5 e recomendacoes R1-R8 do SecBrain.
-- Revisao 3: secao 7 (liga o SSL do servidor via ALTER SYSTEM).
-- Revisao 4 (P3, PUT da api_mapas): role api_mapas_escrita (RP-1, RP-3),
--   guarda de colunas de public.mapas (RP-2), conferencias novas (CP-14)
--   e variavel psql "pular_ssl" para nao reaplicar a secao 7.
-- Revisao 5 (PO3, POST + PK em id; D-N4, RS-9, RS-15, RS-16):
--   - api_mapas_escrita: SELECT (id, graphy, metry), UPDATE (graphy,
--     metry) e INSERT (graphy, metry). SEM INSERT em id e SEM DELETE
--     (Q4: o DELETE foi revogado; o U-2 esta ENCERRADO);
--   - guarda C3 exige id GENERATED ALWAYS (attidentity = 'a', Q5);
--   - guarda 0.2 exige a PK exata mapas_pkey (id). SEM a migracao
--     (database/migracao_mapas_pk.sql) este script ABORTA: a revisao 5
--     so roda DEPOIS da migracao;
--   - 4.5 com INSERT em id = f e DELETE = f; 5.12 (advisory lock) saiu;
--     conferencias novas da sequencia (5.12) e das constraints (5.13).
--
-- RODAR A REVISAO 5 (passo 8 do PU-DBA, DEPOIS da migracao e do
-- VALIDATE do CHECK; SSL ja ligado):
--   1. sudo docker exec -i <container> psql -v pular_ssl=1 \
--          -U casaos -d casaos < database/roles.sql
--      (reaplica as 3 roles de forma idempotente e pula a secao 7; o
--      REVOKE ALL da 4.3 retira o INSERT em id e o DELETE antigos)
--   2. Conferir a secao 5. So entao religar a escrita (LOGIN volta com o
--      proprio script; senha NOVA com \password api_mapas_escrita) e
--      definir PG_ESCRITA_* na versao nova da API.
--   Procedimento completo: database/README.md ("Migracao para PRIMARY KEY").
--
-- ORDEM DOS PASSOS NA PRIMEIRA INSTALACAO (DBA; os agentes NAO executam nada):
--   1. Backup de public.mapas (bloco "ANTES DE RODAR" abaixo).
--   2. database/gerar_certificado_ssl.sh no host do servidor: gera CA +
--      certificado e copia server.crt/server.key para o data_directory
--      do container. Sem isso a secao 7 deste script ABORTA.
--   3. Este script (roles + conferencia + secao 7 - SSL).
--   4. \password das duas roles (bloco "SENHAS" abaixo).
--   5. pg_hba.conf com "hostssl ... scram-sha-256" + "reject" IPv4/IPv6
--      e SELECT pg_reload_conf(); (bloco "LEMBRETE pg_hba.conf").
--   6. Clientes com PG_SSLMODE=Require (ou VerifyFull, com o ca.crt
--      gerado no passo 2 confiavel no cliente).
--
-- Cria/corrige as roles:
--   mapas_teste        testes de integracao (teste_graphy / teste_metry):
--                      SELECT e DELETE em public.mapas, INSERT so nas
--                      colunas graphy e metry
--   api_mapas_leitura  API api_mapas: somente SELECT em public.mapas,
--                      sessao read-only
--   api_mapas_escrita  API api_mapas, PUT /api/mapas/{id}/graphy|metry e
--                      POST /api/mapas/graphy|metry (conexao separada,
--                      PG_ESCRITA_*): SELECT (id, graphy, metry), UPDATE
--                      (graphy, metry) e INSERT (graphy, metry) em
--                      public.mapas. Sem INSERT em id, sem DELETE (Q4;
--                      U-2 encerrado), nada na sequencia, sem TRUNCATE.
--                      Controles: pg_hba hostssl /32, backup (U-4) e
--                      CONNECTION LIMIT 5.
--
-- NENHUMA SENHA NESTE ARQUIVO (O1/S-2). As roles sao criadas SEM senha.
-- R4: "sem senha nao autentica" vale so para regras scram-sha-256/md5/
-- password do pg_hba. A imagem oficial do Postgres traz
-- "local all all trust": por socket local (dentro do container) as roles
-- entram SEM senha. Risco aceito, pois exige acesso ao container (que ja
-- da acesso como casaos). Conferir as regras em uso:
--   SELECT line_number, type, database, user_name, address, auth_method
--   FROM pg_hba_file_rules ORDER BY line_number;
--
-- Execucao (DBA, como SUPERUSUARIO, socket local dentro do container):
--   sudo docker exec -i <container> psql -U casaos -d casaos < database/roles.sql
--
-- Idempotente: pode ser rodado de novo. Se a role ja existir, atributos,
-- configuracoes (RESET ALL + SET) e GRANTs sao reaplicados. Se a role
-- tiver membros/memberships, configuracao por base ou privilegios/posse
-- em objetos fora do esperado, o script ABORTA (RAISE EXCEPTION) para o
-- DBA analisar - nada e alterado nesse caso. As secoes 0 a 4 rodam em
-- uma unica transacao: qualquer erro desfaz tudo. A secao 7 (SSL) roda
-- DEPOIS do COMMIT, fora de transacao (ALTER SYSTEM nao roda em bloco
-- de transacao); se ela abortar, as roles ja estao gravadas e o SSL
-- fica como estava.
--
-- ---------------------------------------------------------------------
-- ANTES DE RODAR F6.6 / M6.6 (S-5, C4): backup de public.mapas, porque
-- os testes fazem DELETE (somente dos ids que inseriram). Gravar FORA do
-- repositorio e FORA do OneDrive, com permissao so do dono (umask 077):
--   umask 077; mkdir -p ~/backups_postgis
--   sudo docker exec <container> pg_dump -U casaos -d casaos -t public.mapas \
--       > ~/backups_postgis/mapas_backup_AAAAMMDD.sql
-- Conferir que o dump nao ficou vazio (esperado: 1):
--   grep -c '^COPY public.mapas' ~/backups_postgis/mapas_backup_AAAAMMDD.sql
-- Revisao 4 (U-4): o mesmo backup, com as conferencias acima embutidas,
-- esta pronto em database/backup_mapas.sh (Git Bash/Linux) e
-- database/backup_mapas.ps1 (PowerShell). Obrigatorio ANTES de definir
-- PG_ESCRITA_* na API.
-- ---------------------------------------------------------------------
--
-- ---------------------------------------------------------------------
-- SENHAS (obrigatorio, DEPOIS de rodar este script) - O1/S-2
-- ---------------------------------------------------------------------
-- Definir SOMENTE no psql interativo (o \password envia o hash SCRAM, a
-- senha nao fica em historico de shell, log do servidor nem no repo):
--   sudo docker exec -it <container> psql -U casaos -d casaos
--   SHOW password_encryption;      -- esperado: scram-sha-256
--   \password mapas_teste
--   \password api_mapas_leitura
--   \password api_mapas_escrita
--   \q
-- Regras:
--   - 16+ caracteres, geradas aleatoriamente (gerenciador de senhas);
--   - diferentes entre si e diferentes da senha de casaos (a API sai com
--     codigo 1 se PG_ESCRITA_PASSWORD for igual a PG_PASSWORD - RP-8);
--   - nunca em arquivo versionado (.env fica fora do repo / apagado, RQ-9);
--   - com PG_SSLMODE=Disable (excecao formal), trocar de novo apos o SSL.
-- ---------------------------------------------------------------------
--
-- ---------------------------------------------------------------------
-- LEMBRETE pg_hba.conf (acao 2, C1) - NAO executado por este script
-- ---------------------------------------------------------------------
-- O pg_hba usa a PRIMEIRA linha que casar (tipo, base, usuario, origem).
-- A imagem oficial do Postgres termina com "host all all all
-- scram-sha-256", que aceitaria estas roles de QUALQUER IP. Por isso,
-- as linhas abaixo devem ficar ANTES dela: libera so a maquina dos
-- testes/API e rejeita as roles vindas de qualquer outro lugar.
--   host     casaos  mapas_teste,api_mapas_leitura  <IP-da-maquina>/32  scram-sha-256
--   host     all     mapas_teste,api_mapas_leitura  0.0.0.0/0           reject
--   host     all     mapas_teste,api_mapas_leitura  ::/0                reject
-- Com SSL ligado (acao 5), a liberacao vira "hostssl"; as linhas "host
-- ... reject" continuam e passam a barrar as tentativas SEM SSL:
--   hostssl  casaos  mapas_teste,api_mapas_leitura  <IP-da-maquina>/32  scram-sha-256
--   host     all     mapas_teste,api_mapas_leitura  0.0.0.0/0           reject
--   host     all     mapas_teste,api_mapas_leitura  ::/0                reject
-- Revisao 4 (RP-17): api_mapas_escrita SO por hostssl, numa linha propria
-- (SEM fase "host"), e incluida nas linhas "reject". Bloco final:
--   hostssl  casaos  mapas_teste,api_mapas_leitura  <IP-da-maquina>/32  scram-sha-256
--   hostssl  casaos  api_mapas_escrita              <IP-da-maquina>/32  scram-sha-256
--   host     all     mapas_teste,api_mapas_leitura,api_mapas_escrita  0.0.0.0/0  reject
--   host     all     mapas_teste,api_mapas_leitura,api_mapas_escrita  ::/0       reject
-- IMPORTANTE: a secao 7 LIGA o SSL, mas NAO o torna obrigatorio. Sem a
-- troca para "hostssl" + os "reject" acima, um cliente com
-- sslmode=Disable/Prefer continua entrando SEM criptografia. Conexoes
-- por socket local (docker exec ... psql) nunca usam SSL.
-- ATENCAO (Docker): com a porta publicada pelo Docker, o client_addr visto
-- pelo Postgres pode ser o gateway da bridge (ex.: 172.17.0.1), e nao o
-- IP real da maquina. Antes de fixar o /32, conectar uma vez e conferir:
--   SELECT usename, client_addr, ssl
--   FROM pg_stat_activity a LEFT JOIN pg_stat_ssl s USING (pid)
--   WHERE usename IN ('mapas_teste', 'api_mapas_leitura', 'api_mapas_escrita');
-- Usar esse endereco no /32. NAO alargar a mascara (nada de /16, /24).
-- Depois de editar:
--   SELECT pg_reload_conf();
--   SELECT * FROM pg_hba_file_rules;   -- conferir (coluna error vazia)
-- ---------------------------------------------------------------------
-- =====================================================================

\set ON_ERROR_STOP on

BEGIN;

-- ---------------------------------------------------------------------
-- 0. Guardas (C3): base correta, executor superusuario, tabela existente
--    e id identity GENERATED ALWAYS (revisao 5, Q5/RS-9: attidentity
--    = 'a'; 'd' = BY DEFAULT = migracao ainda nao aplicada)
-- ---------------------------------------------------------------------
DO $$
DECLARE
    v_identity "char";
BEGIN
    IF current_database() <> 'casaos' THEN
        RAISE EXCEPTION 'roles.sql deve rodar na base casaos (atual: %)', current_database();
    END IF;

    -- C3: somente superusuario (CREATEROLE nao basta: no PG16+ o criador
    -- nao-superusuario vira membro ADMIN da role criada).
    IF NOT EXISTS (SELECT 1 FROM pg_roles
                   WHERE rolname = current_user AND rolsuper) THEN
        RAISE EXCEPTION 'roles.sql exige superusuario (atual: %)', current_user;
    END IF;

    IF to_regclass('public.mapas') IS NULL THEN
        RAISE EXCEPTION 'tabela public.mapas nao encontrada';
    END IF;

    -- C3: a nota sobre a sequencia (secao 4.1) so vale se id for
    -- identity. Revisao 5: so GENERATED ALWAYS ('a'), para que nenhum
    -- INSERT sem OVERRIDING SYSTEM VALUE informe id (RS-9).
    SELECT a.attidentity INTO v_identity
    FROM pg_attribute a
    WHERE a.attrelid = 'public.mapas'::regclass
      AND a.attname  = 'id'
      AND NOT a.attisdropped;

    IF v_identity IS DISTINCT FROM 'a' THEN
        RAISE EXCEPTION 'public.mapas.id nao e GENERATED ALWAYS AS IDENTITY (attidentity = %). Rode antes database/migracao_mapas_pk.sql (PU-DBA).',
            coalesce(nullif(v_identity::text, ''), '<vazio/inexistente>');
    END IF;
END
$$;

-- ---------------------------------------------------------------------
-- 0.1 Guarda de colunas (RP-2 / CP-15). Revisao 5: o PUT so faz UPDATE
-- de uma coluna pela PK (W1/W2) e o POST so faz INSERT de uma coluna
-- (W3/W4); os privilegios de api_mapas_escrita sao por coluna e so estao
-- corretos se public.mapas tiver EXATAMENTE as colunas id, graphy e
-- metry (uma coluna a mais, por exemplo NOT NULL sem default, faria o
-- POST falhar, e mudaria o sentido do CHECK mapas_nao_vazia). Por isso o
-- script ABORTA (nada e alterado) se:
--   - houver coluna alem de id, graphy e metry;
--   - faltar uma delas;
--   - graphy nao for geography ou metry nao for geometry.
-- Colunas removidas (attisdropped) e de sistema (attnum <= 0) nao contam.
-- Se a tabela mudar, W1 a W4 (api_mapas/sql/consultas_referencia.sql) e
-- esta secao precisam ser revistos antes de rodar o script de novo.
-- ---------------------------------------------------------------------
DO $$
DECLARE
    v_extras   text;
    v_faltando text;
    v_tipos    text;
BEGIN
    SELECT string_agg(quote_ident(a.attname), ', ' ORDER BY a.attnum)
      INTO v_extras
    FROM pg_attribute a
    WHERE a.attrelid = 'public.mapas'::regclass
      AND a.attnum > 0
      AND NOT a.attisdropped
      AND a.attname NOT IN ('id', 'graphy', 'metry');

    IF v_extras IS NOT NULL THEN
        RAISE EXCEPTION 'RP-2: public.mapas tem colunas alem de id, graphy e metry: %. Rever W1 a W4 e roles.sql antes de continuar.',
            v_extras;
    END IF;

    SELECT string_agg(c.nome, ', ')
      INTO v_faltando
    FROM (VALUES ('id'), ('graphy'), ('metry')) AS c(nome)
    WHERE NOT EXISTS (SELECT 1 FROM pg_attribute a
                      WHERE a.attrelid = 'public.mapas'::regclass
                        AND a.attname  = c.nome
                        AND a.attnum   > 0
                        AND NOT a.attisdropped);

    IF v_faltando IS NOT NULL THEN
        RAISE EXCEPTION 'RP-2: public.mapas sem a(s) coluna(s): %', v_faltando;
    END IF;

    SELECT string_agg(format('%s = %s', a.attname, t.typname), ', ')
      INTO v_tipos
    FROM pg_attribute a
    JOIN pg_type t ON t.oid = a.atttypid
    WHERE a.attrelid = 'public.mapas'::regclass
      AND NOT a.attisdropped
      AND ((a.attname = 'graphy' AND t.typname <> 'geography')
        OR (a.attname = 'metry'  AND t.typname <> 'geometry')
        OR (a.attname = 'id'     AND t.typname <> 'int4'));

    IF v_tipos IS NOT NULL THEN
        RAISE EXCEPTION 'RP-2: tipo inesperado em public.mapas: % (esperado id = int4, graphy = geography, metry = geometry)',
            v_tipos;
    END IF;
END
$$;

-- ---------------------------------------------------------------------
-- 0.2 Guarda de PK exata (revisao 5; D-N4, RS-14, RS-15). O PUT (UPDATE
-- WHERE id = @id) e o GET (LIMIT 2) so tratam UMA linha por id, e o POST
-- depende da PK para nunca misturar linhas. PK exata = constraint
-- mapas_pkey, contype 'p', conkey = {attnum de id}, nao deferrable,
-- indice valido, pronto, unico e primario, sem predicado nem expressao
-- (mesmos criterios de database/migracao_mapas_pk.sql e do
-- VerificadorRoleEscrita da API). Sem ela o script ABORTA (nada e
-- alterado) e aponta para a migracao: a revisao 5 NAO roda antes dela.
-- ---------------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint k
        JOIN pg_index i ON i.indexrelid = k.conindid
        JOIN pg_attribute a ON a.attrelid = k.conrelid
                           AND a.attname = 'id' AND NOT a.attisdropped
        WHERE k.conrelid = 'public.mapas'::regclass
          AND k.contype = 'p'
          AND k.conname = 'mapas_pkey'
          AND k.conkey = ARRAY[a.attnum]
          AND NOT k.condeferrable
          AND i.indisvalid AND i.indisready AND i.indisunique AND i.indisprimary
          AND i.indpred IS NULL AND i.indexprs IS NULL) THEN
        RAISE EXCEPTION 'public.mapas sem a PRIMARY KEY exata mapas_pkey (id). Rode antes database/migracao_mapas_pk.sql (PU-DBA, database/README.md). Nada foi alterado.';
    END IF;
END
$$;


-- ---------------------------------------------------------------------
-- 1. Criacao (somente se nao existir, sem PASSWORD) e atributos
--    (sempre reaplicados - corrige role pre-existente criada errada)
-- ---------------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'mapas_teste') THEN
        CREATE ROLE mapas_teste;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'api_mapas_leitura') THEN
        CREATE ROLE api_mapas_leitura;
    END IF;
    -- RP-1: sem PASSWORD (definida depois so com \password).
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'api_mapas_escrita') THEN
        CREATE ROLE api_mapas_escrita;
    END IF;
END
$$;

ALTER ROLE mapas_teste
    LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS NOREPLICATION
    NOINHERIT CONNECTION LIMIT 5;

-- O2: CONNECTION LIMIT alinhado ao pool da API (Maximum Pool Size = 20).
ALTER ROLE api_mapas_leitura
    LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS NOREPLICATION
    NOINHERIT CONNECTION LIMIT 20;

-- RP-1: CONNECTION LIMIT 5 alinhado ao pool de escrita da API
-- (Maximum Pool Size = 5, D-P9). Sem SUPERUSER/BYPASSRLS/CREATEROLE: a API
-- desliga o PUT se a role tiver qualquer um deles (R-1).
ALTER ROLE api_mapas_escrita
    LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS
    NOINHERIT CONNECTION LIMIT 5;

-- C2: limpa configuracoes antigas (globais e da base casaos).
ALTER ROLE mapas_teste RESET ALL;
ALTER ROLE mapas_teste IN DATABASE casaos RESET ALL;
ALTER ROLE api_mapas_leitura RESET ALL;
ALTER ROLE api_mapas_leitura IN DATABASE casaos RESET ALL;
ALTER ROLE api_mapas_escrita RESET ALL;
ALTER ROLE api_mapas_escrita IN DATABASE casaos RESET ALL;


-- ---------------------------------------------------------------------
-- 2. C2 (a)/(b): aborta se houver membership ou configuracao residual
-- ---------------------------------------------------------------------
DO $$
DECLARE
    v_detalhe text;
BEGIN
    -- (a) role membro de outra role, ou com membros.
    SELECT string_agg(format('%s -> membro de %s',
                             m.rolname, g.rolname), '; ')
      INTO v_detalhe
    FROM pg_auth_members am
    JOIN pg_roles m ON m.oid = am.member
    JOIN pg_roles g ON g.oid = am.roleid
    WHERE m.rolname IN ('mapas_teste', 'api_mapas_leitura', 'api_mapas_escrita')
       OR g.rolname IN ('mapas_teste', 'api_mapas_leitura', 'api_mapas_escrita');

    IF v_detalhe IS NOT NULL THEN
        RAISE EXCEPTION 'membership inesperada (analisar e revogar manualmente): %', v_detalhe;
    END IF;

    -- (b) apos os RESET acima, nenhuma linha deve sobrar (sobra = config
    -- em OUTRA base, via ALTER ROLE ... IN DATABASE <outra>).
    -- C9: a configuracao FINAL de api_mapas_escrita (secao 3, inclusive
    -- search_path = public) e conferida item a item na autoconferencia 4.5.
    SELECT string_agg(format('%s em base oid %s: %s',
                             r.rolname, s.setdatabase, s.setconfig), '; ')
      INTO v_detalhe
    FROM pg_db_role_setting s
    JOIN pg_roles r ON r.oid = s.setrole
    WHERE r.rolname IN ('mapas_teste', 'api_mapas_leitura', 'api_mapas_escrita');

    IF v_detalhe IS NOT NULL THEN
        RAISE EXCEPTION 'configuracao residual em pg_db_role_setting: %', v_detalhe;
    END IF;
END
$$;


-- ---------------------------------------------------------------------
-- 3. Configuracoes de sessao
-- R5: read_only e os timeouts sao apenas DEFESA EM PROFUNDIDADE: sao
-- defaults da sessao e o proprio cliente pode altera-los (SET ...,
-- SET TRANSACTION READ WRITE). A protecao real e a dos privilegios
-- (GRANT/REVOKE) da secao 4.
-- ---------------------------------------------------------------------
ALTER ROLE mapas_teste SET statement_timeout = '30s';
ALTER ROLE mapas_teste SET lock_timeout = '10s';
ALTER ROLE mapas_teste SET idle_in_transaction_session_timeout = '60s';

-- RA-2: sessao somente leitura e limite de tempo por consulta.
ALTER ROLE api_mapas_leitura SET default_transaction_read_only = on;
ALTER ROLE api_mapas_leitura SET statement_timeout = '15s';
ALTER ROLE api_mapas_leitura SET idle_in_transaction_session_timeout = '60s';

-- RP-3: api_mapas_escrita. A API ainda faz "SET LOCAL lock_timeout = '5s'"
-- logo depois do BEGIN (W1 a W4); o default da role cobre o caso de o
-- cliente esquecer. client_min_messages = warning evita que os NOTICE do
-- ST_IsValid (ex.: "Self-intersection at or near point ...", que trazem
-- coordenadas do WKT) cheguem ao cliente e acabem em log.
-- transaction_timeout so existe no PG 17+ (no PG < 17 o ALTER ROLE SET
-- falharia com "unrecognized configuration parameter"), por isso a guarda
-- de versao. 30s = prazo total do PUT e do POST na API (RP-4).
ALTER ROLE api_mapas_escrita SET statement_timeout = '15s';
ALTER ROLE api_mapas_escrita SET lock_timeout = '5s';
ALTER ROLE api_mapas_escrita SET idle_in_transaction_session_timeout = '30s';
ALTER ROLE api_mapas_escrita SET client_min_messages = 'warning';
-- C9 (SecBrain, P5): search_path fixo. Sem isso valeria o default
-- ("$user", public) e o ALTER DATABASE casaos SET search_path da imagem
-- PostGIS (inclui topology e tiger); um schema chamado api_mapas_escrita
-- ou funcoes homonimas em outro schema poderiam mudar a resolucao de
-- ST_GeomFromText/ST_IsValid/GeometryType usadas sem schema em W1 a W4.
-- pg_catalog continua implicito e e pesquisado primeiro.
ALTER ROLE api_mapas_escrita SET search_path = public;
DO $$
BEGIN
    IF current_setting('server_version_num')::int >= 170000 THEN
        EXECUTE 'ALTER ROLE api_mapas_escrita SET transaction_timeout = ''30s''';
    ELSE
        RAISE WARNING 'PG < 17 (%): transaction_timeout nao aplicado em api_mapas_escrita (recomendado atualizar, S-6)',
            current_setting('server_version');
    END IF;
END
$$;


-- ---------------------------------------------------------------------
-- 4. Privilegios
-- ALERTA: os REVOKE sao so destas roles. NUNCA trocar por "FROM PUBLIC":
-- isso removeria CONNECT/TEMP de todas as roles e derrubaria o CasaOS e
-- os outros apps da base. (TEMP continua vindo de PUBLIC; ver conferencia.)
-- REVOKE ALL na tabela, na base e no schema (so destas roles) zera
-- grants antigos antes de reaplicar; o da tabela tambem remove
-- privilegios de coluna antigos.
-- ---------------------------------------------------------------------

-- 4.1 mapas_teste (S-1) - testes de integracao F6.6 / M6.6
--   SELECT  - LerAsync (SELECT ... WHERE id = @id); tambem exigido pelo
--             RETURNING id do INSERT e pelo WHERE do DELETE (ler coluna
--             em RETURNING/WHERE exige SELECT nela);
--   INSERT  - R3: so nas colunas graphy e metry. Confirmado nos
--             repositorios: "INSERT INTO public.mapas (graphy) ...
--             RETURNING id" e "INSERT INTO public.mapas (metry) ...
--             RETURNING id"; nenhum INSERT do programa ou dos testes
--             informa id. O valor default (identity) nao exige
--             privilegio de coluna em id;
--   DELETE  - limpeza no DisposeAsync (DELETE ... WHERE id = @id
--             [AND ST_SRID(metry) = @srid]) em transacao propria, com
--             COMMIT/ROLLBACK conforme o rowcount. Transacao nao exige
--             privilegio adicional.
-- UPDATE/TRUNCATE/REFERENCES/TRIGGER/MAINTAIN: nao concedidos.
--
-- Sequencia do id: NAO ha GRANT nela. A coluna id e "GENERATED ALWAYS
-- AS IDENTITY" (revisao 5, conferido na guarda C3); o valor e gerado pela
-- expressao interna da identity, que o PostgreSQL avalia SEM checar
-- privilegio na sequencia - basta o INSERT. (USAGE na sequencia so seria
-- necessario para serial/DEFAULT nextval(), o que nao e o caso.)
-- A conferencia 5.6 apenas mostra que a role NAO tem esse privilegio;
-- a prova de que o INSERT funciona sem ele, nos modos BY DEFAULT e
-- ALWAYS, e database/prova_identity_sem_usage.sql (PO3.1, RS-8), alem
-- de F6.6/M6.6 e do POST da API.
REVOKE ALL ON DATABASE casaos FROM mapas_teste;
GRANT CONNECT ON DATABASE casaos TO mapas_teste;
REVOKE ALL ON SCHEMA public FROM mapas_teste;
GRANT USAGE ON SCHEMA public TO mapas_teste;
REVOKE ALL ON TABLE public.mapas FROM mapas_teste;
GRANT SELECT, DELETE ON TABLE public.mapas TO mapas_teste;
GRANT INSERT (graphy, metry) ON TABLE public.mapas TO mapas_teste;

-- 4.2 api_mapas_leitura (RA-1) - API / A7.3
REVOKE ALL ON DATABASE casaos FROM api_mapas_leitura;
GRANT CONNECT ON DATABASE casaos TO api_mapas_leitura;
REVOKE ALL ON SCHEMA public FROM api_mapas_leitura;
GRANT USAGE ON SCHEMA public TO api_mapas_leitura;
REVOKE ALL ON TABLE public.mapas FROM api_mapas_leitura;
GRANT SELECT ON TABLE public.mapas TO api_mapas_leitura;
-- public.spatial_ref_sys (usada por ST_Transform): o PostGIS ja concede
-- SELECT a PUBLIC na instalacao padrao, por isso nao ha GRANT aqui.
-- Conferir na secao 5; se vier false, avaliar com o usuario:
--   GRANT SELECT ON public.spatial_ref_sys TO api_mapas_leitura;

-- 4.3 api_mapas_escrita (RP-1, revisao 5 / D-N4) -
--     PUT /api/mapas/{id}/graphy|metry e POST /api/mapas/graphy|metry
-- Privilegios POR COLUNA, exatamente os que W1 a W4
-- (api_mapas/sql/consultas_referencia.sql) usam:
--   SELECT (id, graphy, metry) - "WHERE id = @id" do UPDATE (W1/W2) e o
--             "RETURNING id" do INSERT (W3/W4) (ler coluna em WHERE ou
--             RETURNING exige SELECT nela);
--   UPDATE (graphy, metry)     - "SET <coluna> = ..." do PUT pela PK. id
--             NAO e atualizavel;
--   INSERT (graphy, metry)     - "INSERT INTO public.mapas (<coluna>)" do
--             POST, SEM id: o id vem da identity (GENERATED ALWAYS). Sem
--             INSERT em id, nem OVERRIDING SYSTEM VALUE deixa a role
--             informar id (RS-9; provado em prova_identity_sem_usage.sql).
-- DELETE: NAO concedido (Q4). O PUT so faz UPDATE e nenhum caminho da
--   API apaga linha. O U-2 (DELETE na tabela inteira) esta ENCERRADO.
--   Limpeza de ids de teste/Postman: mapas_teste religada temporariamente
--   ou casaos local (RS-16; database/README.md).
-- Por ser por coluna, uma coluna nova na tabela NAO fica acessivel a esta
-- role (alem disso, a guarda 0.1 aborta o script).
-- Sequencia: nada (R-3, RS-8). Ver nota em 4.1: o INSERT sem id usa a
-- identity sem checar privilegio na sequencia. O PUT nunca cria id
-- (UPDATE com 0 linhas = 404, D-P3).
-- TRUNCATE/REFERENCES/TRIGGER/MAINTAIN: nao concedidos.
-- As funcoes do PostGIS (ST_GeomFromText, ST_IsValid, GeometryType, cast
-- para geography) tem EXECUTE para PUBLIC na instalacao padrao;
-- conferido na 5.11. O advisory lock do PUT antigo saiu (D-N6).
-- O REVOKE ALL na tabela tambem retira os privilegios de coluna antigos
-- (INSERT em id) e o DELETE da revisao 4.
REVOKE ALL ON DATABASE casaos FROM api_mapas_escrita;
GRANT CONNECT ON DATABASE casaos TO api_mapas_escrita;
REVOKE ALL ON SCHEMA public FROM api_mapas_escrita;
GRANT USAGE ON SCHEMA public TO api_mapas_escrita;
REVOKE ALL ON TABLE public.mapas FROM api_mapas_escrita;
GRANT SELECT (id, graphy, metry) ON TABLE public.mapas TO api_mapas_escrita;
GRANT UPDATE (graphy, metry)     ON TABLE public.mapas TO api_mapas_escrita;
GRANT INSERT (graphy, metry)     ON TABLE public.mapas TO api_mapas_escrita;


-- ---------------------------------------------------------------------
-- 4.4 C2 (c): aborta se as roles tiverem posse ('o'), privilegio ('a') ou
-- forem alvo de politica RLS ('r', R8) em qualquer objeto alem dos
-- esperados. Linhas 'r' nunca estao nas excecoes, entao sempre abortam.
-- Excecoes (somente deptype 'a'):
--   - ACL de public.mapas (tabela e colunas) na base casaos;
--   - ACL do schema public na base casaos;
--   - ACL da base casaos (objeto compartilhado: dbid = 0).
-- pg_shdepend e catalogo compartilhado: enxerga dependencias em TODAS as
-- bases (ex.: GRANT em outra base, ALTER DEFAULT PRIVILEGES, objetos de
-- que a role e dona). Numa role recem-criada so existem as 3 esperadas.
-- ---------------------------------------------------------------------
DO $$
DECLARE
    v_db_casaos oid := (SELECT oid FROM pg_database WHERE datname = 'casaos');
    v_ns_public oid := (SELECT oid FROM pg_namespace WHERE nspname = 'public');
    v_detalhe   text;
BEGIN
    SELECT string_agg(format('%s: deptype=%s dbid=%s classid=%s objid=%s objsubid=%s',
                             r.rolname, d.deptype, d.dbid,
                             d.classid::regclass, d.objid, d.objsubid), '; ')
      INTO v_detalhe
    FROM pg_shdepend d
    JOIN pg_roles r ON r.oid = d.refobjid
    WHERE d.refclassid = 'pg_authid'::regclass
      AND r.rolname IN ('mapas_teste', 'api_mapas_leitura', 'api_mapas_escrita')
      AND d.deptype IN ('o', 'a', 'r')
      AND NOT (
            -- ACL de public.mapas (objsubid 0 = tabela; > 0 = coluna)
            (d.deptype = 'a' AND d.dbid = v_db_casaos
             AND d.classid = 'pg_class'::regclass
             AND d.objid = 'public.mapas'::regclass)
         OR -- ACL do schema public
            (d.deptype = 'a' AND d.dbid = v_db_casaos
             AND d.classid = 'pg_namespace'::regclass
             AND d.objid = v_ns_public)
         OR -- ACL da base casaos (compartilhado)
            (d.deptype = 'a' AND d.dbid = 0
             AND d.classid = 'pg_database'::regclass
             AND d.objid = v_db_casaos)
      );

    IF v_detalhe IS NOT NULL THEN
        RAISE EXCEPTION 'dependencias inesperadas em pg_shdepend (analisar e remover manualmente): %', v_detalhe;
    END IF;
END
$$;


-- ---------------------------------------------------------------------
-- 4.5 Autoconferencia (CP-14), ANTES do COMMIT: aborta (desfaz tudo) se
-- os privilegios EFETIVOS de api_mapas_leitura e api_mapas_escrita em
-- public.mapas, na sequencia e no schema nao forem exatamente os
-- esperados, ou se o rolconfig de api_mapas_escrita nao for exatamente o
-- da secao 3 (RP-3 + search_path = public, C9). has_*_privilege considera
-- tambem o que vem de PUBLIC: um GRANT ... TO PUBLIC em public.mapas
-- tambem faz o script abortar. CP-14 (revisao 5, D-N4): api_mapas_escrita
-- com SELECT (id, graphy, metry), UPDATE (graphy, metry) e INSERT
-- (graphy, metry), INSERT em id = f e DELETE = f; nada na sequencia.
-- ---------------------------------------------------------------------
DO $$
DECLARE
    v_seq     text := pg_get_serial_sequence('public.mapas', 'id');
    v_erros   text[] := '{}';
    v_col     text;
    v_priv    text;
    v_atual   boolean;
    v_esperado boolean;
    v_config     text[];
    v_config_esp text[];
BEGIN
    -- api_mapas_leitura: nenhuma escrita.
    IF has_any_column_privilege('api_mapas_leitura', 'public.mapas', 'INSERT')
       OR has_any_column_privilege('api_mapas_leitura', 'public.mapas', 'UPDATE')
       OR has_table_privilege('api_mapas_leitura', 'public.mapas', 'DELETE')
       OR has_table_privilege('api_mapas_leitura', 'public.mapas', 'TRUNCATE') THEN
        v_erros := v_erros || 'api_mapas_leitura tem escrita (INSERT/UPDATE/DELETE/TRUNCATE) em public.mapas'::text;
    END IF;

    -- api_mapas_escrita: matriz exata por coluna (revisao 5: INSERT em
    -- id = false, RS-9).
    FOREACH v_col IN ARRAY ARRAY['id', 'graphy', 'metry'] LOOP
        FOREACH v_priv IN ARRAY ARRAY['SELECT', 'INSERT', 'UPDATE', 'REFERENCES'] LOOP
            v_esperado := CASE v_priv
                              WHEN 'SELECT'     THEN true
                              WHEN 'INSERT'     THEN v_col <> 'id'
                              WHEN 'UPDATE'     THEN v_col <> 'id'
                              ELSE false
                          END;
            v_atual := has_column_privilege('api_mapas_escrita', 'public.mapas', v_col, v_priv);
            IF v_atual IS DISTINCT FROM v_esperado THEN
                v_erros := v_erros || format('api_mapas_escrita %s em %s = %s (esperado %s)',
                                             v_priv, v_col, v_atual, v_esperado);
            END IF;
        END LOOP;
    END LOOP;

    -- Revisao 5 (Q4): DELETE revogado.
    IF has_table_privilege('api_mapas_escrita', 'public.mapas', 'DELETE') THEN
        v_erros := v_erros || 'api_mapas_escrita com DELETE em public.mapas (revogado na revisao 5, Q4)'::text;
    END IF;
    IF has_table_privilege('api_mapas_escrita', 'public.mapas', 'TRUNCATE')
       OR has_table_privilege('api_mapas_escrita', 'public.mapas', 'REFERENCES')
       OR has_table_privilege('api_mapas_escrita', 'public.mapas', 'TRIGGER')
       OR (current_setting('server_version_num')::int >= 170000
           AND has_table_privilege('api_mapas_escrita', 'public.mapas', 'MAINTAIN')) THEN
        v_erros := v_erros || 'api_mapas_escrita com TRUNCATE/REFERENCES/TRIGGER/MAINTAIN em public.mapas'::text;
    END IF;

    -- Sequencia da identity: nada para as duas roles da API.
    IF has_sequence_privilege('api_mapas_escrita', v_seq, 'USAGE')
       OR has_sequence_privilege('api_mapas_escrita', v_seq, 'SELECT')
       OR has_sequence_privilege('api_mapas_escrita', v_seq, 'UPDATE')
       OR has_sequence_privilege('api_mapas_leitura', v_seq, 'USAGE')
       OR has_sequence_privilege('api_mapas_leitura', v_seq, 'UPDATE') THEN
        v_erros := v_erros || format('privilegio inesperado na sequencia %s', v_seq);
    END IF;

    -- Schema public: CREATE = f (no PG < 15 vem de PUBLIC; ver 5.9).
    IF current_setting('server_version_num')::int >= 150000
       AND (has_schema_privilege('api_mapas_escrita', 'public', 'CREATE')
            OR has_schema_privilege('api_mapas_leitura', 'public', 'CREATE')) THEN
        v_erros := v_erros || 'CREATE no schema public para role da API'::text;
    END IF;

    -- C9: configuracao de sessao de api_mapas_escrita exatamente a de RP-3
    -- + search_path (complementa a guarda de configuracao residual da
    -- secao 2, que roda antes dos SET): so a linha global (setdatabase =
    -- 0), sem nada a mais nem a menos.
    SELECT coalesce(array_agg(c ORDER BY c), '{}')
      INTO v_config
    FROM pg_db_role_setting s
    JOIN pg_roles r ON r.oid = s.setrole
    CROSS JOIN LATERAL unnest(s.setconfig) AS c
    WHERE r.rolname = 'api_mapas_escrita'
      AND s.setdatabase = 0;

    v_config_esp := ARRAY['client_min_messages=warning',
                          'idle_in_transaction_session_timeout=30s',
                          'lock_timeout=5s',
                          'search_path=public',
                          'statement_timeout=15s'];
    IF current_setting('server_version_num')::int >= 170000 THEN
        v_config_esp := v_config_esp || 'transaction_timeout=30s'::text;
    END IF;
    SELECT array_agg(c ORDER BY c) INTO v_config_esp FROM unnest(v_config_esp) AS c;

    IF v_config IS DISTINCT FROM v_config_esp THEN
        v_erros := v_erros || format('rolconfig de api_mapas_escrita = %s (esperado %s)',
                                     v_config, v_config_esp);
    END IF;

    IF EXISTS (SELECT 1 FROM pg_db_role_setting s
               JOIN pg_roles r ON r.oid = s.setrole
               WHERE r.rolname = 'api_mapas_escrita' AND s.setdatabase <> 0) THEN
        v_erros := v_erros || 'api_mapas_escrita com configuracao por base (pg_db_role_setting)'::text;
    END IF;

    IF cardinality(v_erros) > 0 THEN
        RAISE EXCEPTION 'CP-14: privilegios fora do esperado (nada foi gravado): %',
            array_to_string(v_erros, '; ');
    END IF;
END
$$;

COMMIT;


-- =====================================================================
-- 5. Conferencia (somente SELECT)
-- =====================================================================

-- 5.1 Atributos das roles
-- Esperado (ambas): rolsuper/rolcreatedb/rolcreaterole/rolbypassrls/
-- rolreplication = f, rolinherit = f, rolcanlogin = t
--   mapas_teste       rolconnlimit = 5,  rolconfig =
--                     {statement_timeout=30s,lock_timeout=10s,
--                      idle_in_transaction_session_timeout=60s}
--   api_mapas_leitura rolconnlimit = 20, rolconfig =
--                     {default_transaction_read_only=on,statement_timeout=15s,
--                      idle_in_transaction_session_timeout=60s}
--   api_mapas_escrita rolconnlimit = 5, rolconfig =
--                     {statement_timeout=15s,lock_timeout=5s,
--                      idle_in_transaction_session_timeout=30s,
--                      client_min_messages=warning,search_path=public,
--                      transaction_timeout=30s}
--                     (transaction_timeout so no PG 17+; a autoconferencia
--                      4.5 ja abortaria se fosse diferente - C9)
-- rolpassword_definida: f ate o \password; t depois (pg_authid so e
-- legivel por superusuario, que e quem roda este script).
SELECT r.rolname, r.rolsuper, r.rolcreatedb, r.rolcreaterole, r.rolbypassrls,
       r.rolreplication, r.rolinherit, r.rolcanlogin, r.rolconnlimit,
       r.rolconfig,
       (a.rolpassword IS NOT NULL) AS rolpassword_definida
FROM pg_roles r
JOIN pg_authid a ON a.oid = r.oid
WHERE r.rolname IN ('mapas_teste', 'api_mapas_leitura', 'api_mapas_escrita')
ORDER BY r.rolname;

-- 5.2 Configuracao por base (C2): esperado 0 linhas. As configuracoes
-- globais (setdatabase = 0) ja aparecem em rolconfig na 5.1.
SELECT r.rolname, s.setdatabase, s.setconfig
FROM pg_db_role_setting s
JOIN pg_roles r ON r.oid = s.setrole
WHERE r.rolname IN ('mapas_teste', 'api_mapas_leitura', 'api_mapas_escrita')
  AND s.setdatabase <> 0;

-- 5.2b (R8, so informativo) Configuracoes que valem para TODAS as roles
-- (ALTER ROLE ALL ... / ALTER DATABASE ... SET). Podem sobrepor ou somar
-- aos defaults das roles acima; analisar se houver linhas.
SELECT * FROM pg_db_role_setting WHERE setrole = 0;

-- 5.3 Membership/heranca: esperado 0 linhas.
SELECT m.rolname AS membro, g.rolname AS grupo
FROM pg_auth_members am
JOIN pg_roles m ON m.oid = am.member
JOIN pg_roles g ON g.oid = am.roleid
WHERE m.rolname IN ('mapas_teste', 'api_mapas_leitura', 'api_mapas_escrita')
   OR g.rolname IN ('mapas_teste', 'api_mapas_leitura', 'api_mapas_escrita');

-- 5.4 Privilegios de tabela em public.mapas (R2; revisao 5: DELETE de
-- api_mapas_escrita = f, Q4)
-- Esperado:
--               SELECT INSERT UPDATE DELETE TRUNCATE REFERENCES TRIGGER MAINTAIN
--   api_mapas_escrita f      f      f      f        f          f       f        f
--   api_mapas_leitura t      f      f      f        f          f       f        f
--   mapas_teste       t      f      f      t        f          f       f        f
-- (SELECT/INSERT/UPDATE de api_mapas_escrita e INSERT de mapas_teste sao f
-- no nivel de TABELA porque foram concedidos so por coluna; ver colunas
-- any_* abaixo e a 5.5.)
-- MAINTAIN so existe no PG17+; em versao anterior a coluna vem NULL.
--   any_select:  t para as tres
--   any_insert:  api_mapas_escrita t | api_mapas_leitura f | mapas_teste t
--   any_update:  api_mapas_escrita t | api_mapas_leitura f | mapas_teste f
SELECT r.rolname,
       has_table_privilege(r.rolname, 'public.mapas', 'SELECT')     AS pode_select,
       has_table_privilege(r.rolname, 'public.mapas', 'INSERT')     AS pode_insert_tabela,
       has_table_privilege(r.rolname, 'public.mapas', 'UPDATE')     AS pode_update,
       has_table_privilege(r.rolname, 'public.mapas', 'DELETE')     AS pode_delete,
       has_table_privilege(r.rolname, 'public.mapas', 'TRUNCATE')   AS pode_truncate,
       has_table_privilege(r.rolname, 'public.mapas', 'REFERENCES') AS pode_references,
       has_table_privilege(r.rolname, 'public.mapas', 'TRIGGER')    AS pode_trigger,
       CASE WHEN current_setting('server_version_num')::int >= 170000
            THEN has_table_privilege(r.rolname, 'public.mapas', 'MAINTAIN')
       END                                                          AS pode_maintain,
       has_any_column_privilege(r.rolname, 'public.mapas', 'SELECT') AS any_select,
       has_any_column_privilege(r.rolname, 'public.mapas', 'INSERT') AS any_insert,
       has_any_column_privilege(r.rolname, 'public.mapas', 'UPDATE') AS any_update
FROM (VALUES ('mapas_teste'), ('api_mapas_leitura'), ('api_mapas_escrita')) AS r(rolname)
ORDER BY r.rolname;

-- 5.5 Privilegios por coluna (R3, RP-1, CP-14; revisao 5: INSERT em id
-- de api_mapas_escrita = f, RS-9) - uma linha por role e coluna. Esperado:
--   role               coluna  SELECT INSERT UPDATE REFERENCES
--   api_mapas_escrita  id        t      f      f      f
--   api_mapas_escrita  graphy    t      t      t      f
--   api_mapas_escrita  metry     t      t      t      f
--   api_mapas_leitura  id        t      f      f      f
--   api_mapas_leitura  graphy    t      f      f      f
--   api_mapas_leitura  metry     t      f      f      f
--   mapas_teste        id        t      f      f      f
--   mapas_teste        graphy    t      t      f      f
--   mapas_teste        metry     t      t      f      f
SELECT r.rolname, c.coluna,
       has_column_privilege(r.rolname, 'public.mapas', c.coluna, 'SELECT')     AS pode_select,
       has_column_privilege(r.rolname, 'public.mapas', c.coluna, 'INSERT')     AS pode_insert,
       has_column_privilege(r.rolname, 'public.mapas', c.coluna, 'UPDATE')     AS pode_update,
       has_column_privilege(r.rolname, 'public.mapas', c.coluna, 'REFERENCES') AS pode_references
FROM (VALUES ('mapas_teste'), ('api_mapas_leitura'), ('api_mapas_escrita')) AS r(rolname)
CROSS JOIN (VALUES (1, 'id'), (2, 'graphy'), (3, 'metry')) AS c(ordem, coluna)
ORDER BY r.rolname, c.ordem;

-- 5.5b api_mapas_leitura sem NENHUMA escrita (CP-14).
-- Esperado: f em todas as colunas.
SELECT has_any_column_privilege('api_mapas_leitura', 'public.mapas', 'INSERT') AS leitura_insert,
       has_any_column_privilege('api_mapas_leitura', 'public.mapas', 'UPDATE') AS leitura_update,
       has_table_privilege('api_mapas_leitura', 'public.mapas', 'DELETE')      AS leitura_delete,
       has_table_privilege('api_mapas_leitura', 'public.mapas', 'TRUNCATE')    AS leitura_truncate;

-- 5.6 Sequencia da identity: esperado false em tudo, para as tres. Mostra
-- apenas que NAO foi concedido; a prova de que o INSERT sem id funciona
-- sem ele (BY DEFAULT e ALWAYS) e database/prova_identity_sem_usage.sql
-- (PO3.1, RS-8), alem de F6.6/M6.6 e do POST da API.
SELECT r.rolname,
       pg_get_serial_sequence('public.mapas', 'id') AS sequencia,
       has_sequence_privilege(r.rolname,
           pg_get_serial_sequence('public.mapas', 'id'), 'USAGE')  AS pode_usage_seq,
       has_sequence_privilege(r.rolname,
           pg_get_serial_sequence('public.mapas', 'id'), 'SELECT') AS pode_select_seq,
       has_sequence_privilege(r.rolname,
           pg_get_serial_sequence('public.mapas', 'id'), 'UPDATE') AS pode_update_seq
FROM (VALUES ('mapas_teste'), ('api_mapas_leitura'), ('api_mapas_escrita')) AS r(rolname)
ORDER BY r.rolname;

-- 5.7 spatial_ref_sys (ST_Transform na API; lookup de SRID no PostGIS,
-- inclusive no ST_GeomFromText(@wkt, @srid) do PUT e do POST)
-- Esperado: true para as tres (via PUBLIC). Se false, ver nota em 4.2
-- (para api_mapas_escrita: GRANT SELECT ON public.spatial_ref_sys TO
-- api_mapas_escrita, avaliado com o usuario).
SELECT r.rolname,
       has_table_privilege(r.rolname, 'public.spatial_ref_sys', 'SELECT') AS pode_ler_spatial_ref_sys
FROM (VALUES ('mapas_teste'), ('api_mapas_leitura'), ('api_mapas_escrita')) AS r(rolname)
ORDER BY r.rolname;

-- 5.8 Base casaos
-- Esperado: CONNECT t | CREATE f (para as tres).
-- TEMP normalmente t, herdado de PUBLIC (padrao do PostgreSQL); nao foi
-- removido para nao afetar as demais roles da base.
SELECT r.rolname,
       has_database_privilege(r.rolname, 'casaos', 'CONNECT') AS pode_connect,
       has_database_privilege(r.rolname, 'casaos', 'CREATE')  AS pode_create,
       has_database_privilege(r.rolname, 'casaos', 'TEMP')    AS pode_temp
FROM (VALUES ('mapas_teste'), ('api_mapas_leitura'), ('api_mapas_escrita')) AS r(rolname)
ORDER BY r.rolname;

-- 5.9 Schema public
-- Esperado: USAGE t | CREATE f.
-- ATENCAO (R6): no PostgreSQL < 15 o schema public concede CREATE a
-- PUBLIC, entao CREATE vira t. Nesse caso NAO revogar daqui: avaliar
-- "REVOKE CREATE ON SCHEMA public FROM PUBLIC" com o usuario, so apos
-- inventariar as roles que criam objetos em public (CasaOS/outros apps).
SELECT current_setting('server_version') AS versao_servidor;
SELECT r.rolname,
       has_schema_privilege(r.rolname, 'public', 'USAGE')  AS pode_usage,
       has_schema_privilege(r.rolname, 'public', 'CREATE') AS pode_create
FROM (VALUES ('mapas_teste'), ('api_mapas_leitura'), ('api_mapas_escrita')) AS r(rolname)
ORDER BY r.rolname;

-- 5.10 Guarda de colunas (RP-2), so informativo (a guarda 0.1 ja abortaria).
-- Esperado: 3 linhas - id int4 | graphy geography | metry geometry.
SELECT a.attnum, a.attname, t.typname
FROM pg_attribute a
JOIN pg_type t ON t.oid = a.atttypid
WHERE a.attrelid = 'public.mapas'::regclass
  AND a.attnum > 0
  AND NOT a.attisdropped
ORDER BY a.attnum;

-- 5.11 Funcoes usadas por W1 a W4 e pela validacao W1.V/W2.V (via PUBLIC
-- na instalacao padrao). Revisao 5: o advisory lock saiu (D-N6).
-- Esperado: t em todas as colunas.
SELECT has_function_privilege('api_mapas_escrita',
           'public.st_geomfromtext(text, integer)', 'EXECUTE')               AS st_geomfromtext,
       has_function_privilege('api_mapas_escrita',
           'public.st_isvalid(public.geometry)', 'EXECUTE')                  AS st_isvalid,
       has_function_privilege('api_mapas_escrita',
           'public.geometrytype(public.geometry)', 'EXECUTE')                AS geometrytype,
       has_function_privilege('api_mapas_escrita',
           'public.geography(public.geometry)', 'EXECUTE')                   AS cast_geography;

-- 5.12 Sequencia da identity (revisao 5, RS-12), so informativo.
-- Esperado: proximo_valor > max_id (senao o proximo POST falha com 23505
-- em mapas_pkey: alguem inseriu com OVERRIDING SYSTEM VALUE a frente da
-- sequencia, R-N3; corrigir com o passo 7 de migracao_mapas_pk.sql) e
-- ids_restantes bem acima de 1000000 (limite do RS-12).
SELECT s.schemaname || '.' || s.sequencename                       AS sequencia,
       s.last_value,
       CASE WHEN s.last_value IS NULL THEN s.start_value
            ELSE s.last_value + s.increment_by END                 AS proximo_valor,
       (SELECT max(id) FROM public.mapas)                          AS max_id,
       s.max_value - coalesce(s.last_value, s.start_value - 1)     AS ids_restantes
FROM pg_sequences s
WHERE (s.schemaname || '.' || s.sequencename) = pg_get_serial_sequence('public.mapas', 'id');

-- 5.13 Constraints e identity de public.mapas (revisao 5), so informativo
-- (a guarda 0.2 ja abortaria sem a PK). Esperado:
--   mapas_pkey      p  PRIMARY KEY (id)                                       convalidated t
--   mapas_nao_vazia c  CHECK (((graphy IS NOT NULL) OR (metry IS NOT NULL)))  convalidated t
--   (f enquanto o VALIDATE do passo 7 do PU-DBA nao rodar)
--   id: is_identity YES, identity_generation ALWAYS
SELECT conname, contype, pg_get_constraintdef(oid) AS definicao, convalidated
FROM pg_constraint
WHERE conrelid = 'public.mapas'::regclass
ORDER BY contype DESC, conname;
SELECT column_name, is_identity, identity_generation
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'mapas' AND column_name = 'id';


-- =====================================================================
-- 6. POS-TESTE (NAO EXECUTADO) - rodar depois que F6.6/M6.6 passarem
-- =====================================================================
-- Opcao preferida (R1): desativar o login, apagar a senha e derrubar
-- sessoes abertas da role de teste:
--   ALTER ROLE mapas_teste NOLOGIN PASSWORD NULL;
--   SELECT pg_terminate_backend(pid)
--   FROM pg_stat_activity
--   WHERE usename = 'mapas_teste';
-- Alternativa (manter a role ativa, mas sem poder apagar dados):
--   REVOKE DELETE ON public.mapas FROM mapas_teste;
-- Para uma rodada futura, basta rodar este script de novo (reaplica LOGIN
-- e os GRANTs) e definir uma senha NOVA com \password mapas_teste.
--
-- api_mapas_escrita - desligar a escrita no banco (ex.: suspeita de uso
-- indevido ou fim da POC), sem mexer na API:
--   ALTER ROLE api_mapas_escrita NOLOGIN PASSWORD NULL;
--   SELECT pg_terminate_backend(pid)
--   FROM pg_stat_activity
--   WHERE usename = 'api_mapas_escrita';
-- Rodar este script de novo reativa o LOGIN; a senha precisa ser NOVA.
-- Restauracao a partir do backup: ver database/backup_mapas.sh.
--
-- Limpeza de ids criados por POST de teste/Postman (revisao 5, RS-16):
-- api_mapas_escrita NAO tem DELETE (Q4; U-2 encerrado). Apagar com
-- mapas_teste religada temporariamente (rodar este script, \password
-- nova, DELETE e depois NOLOGIN de novo) ou com casaos por socket local:
--   BEGIN;
--   DELETE FROM public.mapas WHERE id = <id capturado do Location>;
--   -- conferir "DELETE 1"; se for outro numero: ROLLBACK;
--   COMMIT;
-- =====================================================================


-- =====================================================================
-- 7. SSL (acao 5 / S-3) - roda FORA de transacao, depois do COMMIT
-- =====================================================================
-- Revisao 4: com "psql -v pular_ssl=1" esta secao inteira e PULADA (o SSL
-- ja foi ligado em 2026-10-22; nao ha motivo para reaplicar ALTER SYSTEM
-- e pg_reload_conf so para atualizar as roles). Sem a variavel, a secao
-- roda como antes (idempotente: reaplica os mesmos valores).
-- Pre-requisito: database/gerar_certificado_ssl.sh ja copiou server.crt
-- e server.key para o data_directory do container (dono postgres, chave
-- 0600). Caminhos RELATIVOS ao data_directory (volume persistente).
--
-- RISCO: se ssl=on ficar gravado no postgresql.auto.conf e o certificado
-- ou a chave sumirem / ficarem com permissao errada, o servidor NAO SOBE
-- no proximo restart do container. Por isso a guarda 7.1 aborta antes
-- do ALTER SYSTEM se os arquivos nao estiverem la. Se o processo postgres
-- nao conseguir ler a chave (dono errado), o pg_read_file da guarda falha
-- e tambem aborta. Modo 0600 e dono exato NAO sao conferidos via SQL:
-- quem confere e o .sh (stat) - o PG recusa chave legivel por grupo/outros.
--
-- Desde o PG 10, ssl e os arquivos de certificado mudam com reload
-- (pg_reload_conf), sem restart. Se o reload falhar ao carregar o
-- certificado/chave, o PG registra o erro no log e NAO aceita SSL, mas o
-- valor ssl=on continua gravado para o proximo restart. Ver 7.4.
-- ---------------------------------------------------------------------

\if :{?pular_ssl}
\echo '>> Secao 7 (SSL) PULADA (-v pular_ssl). Roles aplicadas; conferir a secao 5 acima.'
\else

-- 7.1 Guarda: arquivos existem, nao estao vazios e tem o formato PEM
-- esperado. So le o cabecalho da chave (primeiros bytes), nunca o
-- conteudo secreto.
DO $$
DECLARE
    v_dir  text := current_setting('data_directory');
    v_crt  text := current_setting('data_directory') || '/server.crt';
    v_key  text := current_setting('data_directory') || '/server.key';
    v_st   record;
    v_cab  text;
BEGIN
    IF current_setting('server_version_num')::int < 120000 THEN
        RAISE EXCEPTION 'secao 7 exige PG 12+ (ssl_min_protocol_version); versao: %',
            current_setting('server_version');
    END IF;

    SELECT * INTO v_st FROM pg_stat_file(v_crt, true);
    IF v_st.size IS NULL OR v_st.isdir OR v_st.size = 0 THEN
        RAISE EXCEPTION 'SSL NAO LIGADO: % nao existe ou esta vazio. Rode antes database/gerar_certificado_ssl.sh (data_directory = %)',
            v_crt, v_dir;
    END IF;

    SELECT * INTO v_st FROM pg_stat_file(v_key, true);
    IF v_st.size IS NULL OR v_st.isdir OR v_st.size = 0 THEN
        RAISE EXCEPTION 'SSL NAO LIGADO: % nao existe ou esta vazio. Rode antes database/gerar_certificado_ssl.sh (data_directory = %)',
            v_key, v_dir;
    END IF;

    v_cab := pg_read_file(v_crt, 0, 27);
    IF v_cab <> '-----BEGIN CERTIFICATE-----' THEN
        RAISE EXCEPTION 'SSL NAO LIGADO: % nao comeca com "-----BEGIN CERTIFICATE-----"', v_crt;
    END IF;

    -- Chave sem senha (PKCS#8 "BEGIN PRIVATE KEY", ou "BEGIN EC/RSA
    -- PRIVATE KEY"). Chave cifrada exigiria ssl_passphrase_command e
    -- impediria o servidor de subir sozinho. R6: le 64 bytes para pegar
    -- tambem o PEM tradicional cifrado ("Proc-Type: 4,ENCRYPTED" logo
    -- apos o cabecalho).
    v_cab := pg_read_file(v_key, 0, 64);
    IF v_cab NOT LIKE '-----BEGIN %PRIVATE KEY-----%'
       OR v_cab LIKE '-----BEGIN ENCRYPTED PRIVATE KEY-----%'
       OR position('Proc-Type: 4,ENCRYPTED' IN v_cab) > 0 THEN
        RAISE EXCEPTION 'SSL NAO LIGADO: % nao e chave PEM sem senha', v_key;
    END IF;

    -- ssl_ca_file / ssl_crl_file apontando para arquivo inexistente tambem
    -- impedem o carregamento do SSL.
    IF current_setting('ssl_ca_file') <> ''
       AND (SELECT size FROM pg_stat_file(current_setting('ssl_ca_file'), true)) IS NULL THEN
        RAISE EXCEPTION 'SSL NAO LIGADO: ssl_ca_file = % nao existe', current_setting('ssl_ca_file');
    END IF;
    IF current_setting('ssl_crl_file') <> ''
       AND (SELECT size FROM pg_stat_file(current_setting('ssl_crl_file'), true)) IS NULL THEN
        RAISE EXCEPTION 'SSL NAO LIGADO: ssl_crl_file = % nao existe', current_setting('ssl_crl_file');
    END IF;

    RAISE NOTICE 'Guarda SSL ok: % e % presentes.', v_crt, v_key;
END
$$;

-- 7.2 Configuracao (grava em postgresql.auto.conf). ssl = on por ultimo.
ALTER SYSTEM SET ssl_cert_file = 'server.crt';
ALTER SYSTEM SET ssl_key_file = 'server.key';
-- TLSv1.2 como minimo: compatibilidade com o Npgsql no Windows.
ALTER SYSTEM SET ssl_min_protocol_version = 'TLSv1.2';
-- R3: so cifras fortes no TLS 1.2 (o TLS 1.3 usa as suites padrao do
-- OpenSSL e nao e afetado por ssl_ciphers).
ALTER SYSTEM SET ssl_ciphers = 'HIGH:!aNULL:!eNULL:!MD5:!3DES:!RC4';
ALTER SYSTEM SET ssl = on;

-- 7.3 Reload (sem restart). A pausa da tempo desta sessao processar o
-- SIGHUP antes do SHOW.
SELECT pg_reload_conf();
SELECT pg_sleep(1);

-- 7.4 Conferencia
-- Esperado: ssl = on; ssl_cert_file = server.crt; ssl_key_file =
-- server.key; ssl_min_protocol_version = TLSv1.2;
-- ssl_ciphers = HIGH:!aNULL:!eNULL:!MD5:!3DES:!RC4; pending_restart = f.
SHOW ssl;
SELECT name, setting, pending_restart
FROM pg_settings
WHERE name LIKE 'ssl%'
ORDER BY name;
-- Erros de sintaxe/valor nos arquivos de configuracao: esperado 0 linhas.
SELECT sourcefile, sourceline, name, setting, applied, error
FROM pg_file_settings
WHERE error IS NOT NULL OR (name LIKE 'ssl%' AND NOT applied);
\endif
--
-- ATENCAO: "SHOW ssl = on" mostra o valor configurado; NAO prova que o
-- certificado carregou. Os passos a) e b) abaixo sao OBRIGATORIOS, LOGO
-- DEPOIS desta secao, antes de qualquer outra acao no container:
--   a) log do container (procurar "could not load", "private key file",
--      "certificate"):
--        sudo docker logs --since 5m <container> 2>&1 | grep -i -E 'ssl|certif|private key'
--   b) uma conexao TCP real com sslmode=require. A senha e pedida pelo
--      prompt do psql (-it); NUNCA PGPASSWORD=... nem senha na linha de
--      comando. Dentro do container (TCP em localhost):
--        sudo docker exec -it <container> psql \
--             "host=localhost dbname=casaos user=casaos sslmode=require" \
--             -c "SELECT ssl, version, cipher FROM pg_stat_ssl WHERE pid = pg_backend_pid();"
--      Ou da maquina cliente, depois do \password das roles:
--        psql "host=192.168.168.106 dbname=casaos user=mapas_teste sslmode=require" \
--             -c "SELECT ssl, version, cipher FROM pg_stat_ssl WHERE pid = pg_backend_pid();"
--      Esperado: ssl = t, version TLSv1.2 ou TLSv1.3.
--      Com a CA: sslmode=verify-full sslrootcert=<caminho>/ca.crt.
--
-- SE a) OU b) FALHAR: rollback IMEDIATO, ANTES de qualquer restart do
-- container (senao o servidor pode nao subir):
--   ALTER SYSTEM RESET ssl;
--   ALTER SYSTEM RESET ssl_cert_file;
--   ALTER SYSTEM RESET ssl_key_file;
--   ALTER SYSTEM RESET ssl_min_protocol_version;
--   ALTER SYSTEM RESET ssl_ciphers;
--   SELECT pg_reload_conf();
--   SHOW ssl;   -- esperado: off
-- Depois corrigir os arquivos (rodar o .sh de novo com --force) e repetir
-- esta secao. Se o container ja nao sobe: editar postgresql.auto.conf no
-- volume (remover a linha ssl = 'on') a partir do host.
-- =====================================================================
