-- =====================================================================
-- database/roles.sql  (acoes 3, 4 e 5 do DBA - libera F6.6, M6.6 e A7.3)
-- Base: casaos | Alvo: public.mapas (id int4 identity, graphy, metry)
-- Servidor: 192.168.168.106 (container Docker do CasaOS), PG 17.x,
-- PostGIS 3.5.2.
-- Revisao 2: correcoes C1-C5 e recomendacoes R1-R8 do SecBrain.
-- Revisao 3: secao 7 (liga o SSL do servidor via ALTER SYSTEM).
--
-- ORDEM DOS PASSOS (DBA; os agentes NAO executam nada):
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
--   \q
-- Regras:
--   - 16+ caracteres, geradas aleatoriamente (gerenciador de senhas);
--   - diferentes entre si e diferentes da senha de casaos;
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
-- IMPORTANTE: a secao 7 LIGA o SSL, mas NAO o torna obrigatorio. Sem a
-- troca para "hostssl" + os "reject" acima, um cliente com
-- sslmode=Disable/Prefer continua entrando SEM criptografia. Conexoes
-- por socket local (docker exec ... psql) nunca usam SSL.
-- ATENCAO (Docker): com a porta publicada pelo Docker, o client_addr visto
-- pelo Postgres pode ser o gateway da bridge (ex.: 172.17.0.1), e nao o
-- IP real da maquina. Antes de fixar o /32, conectar uma vez e conferir:
--   SELECT usename, client_addr, ssl
--   FROM pg_stat_activity a LEFT JOIN pg_stat_ssl s USING (pid)
--   WHERE usename IN ('mapas_teste', 'api_mapas_leitura');
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
--    e id identity
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

    -- C3: a nota sobre a sequencia (secao 3) so vale se id for identity.
    SELECT a.attidentity INTO v_identity
    FROM pg_attribute a
    WHERE a.attrelid = 'public.mapas'::regclass
      AND a.attname  = 'id'
      AND NOT a.attisdropped;

    IF v_identity IS NULL OR v_identity NOT IN ('a', 'd') THEN
        RAISE EXCEPTION 'public.mapas.id nao e identity (attidentity = %)',
            coalesce(nullif(v_identity::text, ''), '<vazio/inexistente>');
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
END
$$;

ALTER ROLE mapas_teste
    LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS NOREPLICATION
    NOINHERIT CONNECTION LIMIT 5;

-- O2: CONNECTION LIMIT alinhado ao pool da API (Maximum Pool Size = 20).
ALTER ROLE api_mapas_leitura
    LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS NOREPLICATION
    NOINHERIT CONNECTION LIMIT 20;

-- C2: limpa configuracoes antigas (globais e da base casaos).
ALTER ROLE mapas_teste RESET ALL;
ALTER ROLE mapas_teste IN DATABASE casaos RESET ALL;
ALTER ROLE api_mapas_leitura RESET ALL;
ALTER ROLE api_mapas_leitura IN DATABASE casaos RESET ALL;


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
    WHERE m.rolname IN ('mapas_teste', 'api_mapas_leitura')
       OR g.rolname IN ('mapas_teste', 'api_mapas_leitura');

    IF v_detalhe IS NOT NULL THEN
        RAISE EXCEPTION 'membership inesperada (analisar e revogar manualmente): %', v_detalhe;
    END IF;

    -- (b) apos os RESET acima, nenhuma linha deve sobrar (sobra = config
    -- em OUTRA base, via ALTER ROLE ... IN DATABASE <outra>).
    SELECT string_agg(format('%s em base oid %s: %s',
                             r.rolname, s.setdatabase, s.setconfig), '; ')
      INTO v_detalhe
    FROM pg_db_role_setting s
    JOIN pg_roles r ON r.oid = s.setrole
    WHERE r.rolname IN ('mapas_teste', 'api_mapas_leitura');

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
-- Sequencia do id: NAO ha GRANT nela. A coluna id e "GENERATED BY
-- DEFAULT AS IDENTITY" (conferido na guarda C3); o valor e gerado pela
-- expressao interna da identity, que o PostgreSQL avalia SEM checar
-- privilegio na sequencia - basta o INSERT. (USAGE na sequencia so seria
-- necessario para serial/DEFAULT nextval(), o que nao e o caso.)
-- A conferencia 5.5 apenas mostra que a role NAO tem esse privilegio;
-- quem comprova que o INSERT funciona sem ele sao F6.6 e M6.6.
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


-- ---------------------------------------------------------------------
-- 4.3 C2 (c): aborta se as roles tiverem posse ('o'), privilegio ('a') ou
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
      AND r.rolname IN ('mapas_teste', 'api_mapas_leitura')
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
-- rolpassword_definida: f ate o \password; t depois (pg_authid so e
-- legivel por superusuario, que e quem roda este script).
SELECT r.rolname, r.rolsuper, r.rolcreatedb, r.rolcreaterole, r.rolbypassrls,
       r.rolreplication, r.rolinherit, r.rolcanlogin, r.rolconnlimit,
       r.rolconfig,
       (a.rolpassword IS NOT NULL) AS rolpassword_definida
FROM pg_roles r
JOIN pg_authid a ON a.oid = r.oid
WHERE r.rolname IN ('mapas_teste', 'api_mapas_leitura')
ORDER BY r.rolname;

-- 5.2 Configuracao por base (C2): esperado 0 linhas. As configuracoes
-- globais (setdatabase = 0) ja aparecem em rolconfig na 5.1.
SELECT r.rolname, s.setdatabase, s.setconfig
FROM pg_db_role_setting s
JOIN pg_roles r ON r.oid = s.setrole
WHERE r.rolname IN ('mapas_teste', 'api_mapas_leitura')
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
WHERE m.rolname IN ('mapas_teste', 'api_mapas_leitura')
   OR g.rolname IN ('mapas_teste', 'api_mapas_leitura');

-- 5.4 Privilegios de tabela em public.mapas (R2)
-- Esperado:
--               SELECT INSERT UPDATE DELETE TRUNCATE REFERENCES TRIGGER MAINTAIN
--   mapas_teste       t      f      f      t        f          f       f        f
--   api_mapas_leitura t      f      f      f        f          f       f        f
-- (INSERT de mapas_teste e f no nivel de TABELA porque foi concedido so
-- por coluna; ver colunas any_* abaixo e a 5.5.)
-- MAINTAIN so existe no PG17+; em versao anterior a coluna vem NULL.
--   any_insert:  mapas_teste t | api_mapas_leitura f
--   any_update:  f para as duas
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
       has_any_column_privilege(r.rolname, 'public.mapas', 'INSERT') AS any_insert,
       has_any_column_privilege(r.rolname, 'public.mapas', 'UPDATE') AS any_update
FROM (VALUES ('mapas_teste'), ('api_mapas_leitura')) AS r(rolname)
ORDER BY r.rolname;

-- 5.5 Privilegio de INSERT por coluna (R3)
-- Esperado:
--   mapas_teste       id f | graphy t | metry t
--   api_mapas_leitura id f | graphy f | metry f
SELECT r.rolname,
       has_column_privilege(r.rolname, 'public.mapas', 'id',     'INSERT') AS insert_id,
       has_column_privilege(r.rolname, 'public.mapas', 'graphy', 'INSERT') AS insert_graphy,
       has_column_privilege(r.rolname, 'public.mapas', 'metry',  'INSERT') AS insert_metry
FROM (VALUES ('mapas_teste'), ('api_mapas_leitura')) AS r(rolname)
ORDER BY r.rolname;

-- 5.6 Sequencia da identity: esperado false para as duas. Mostra apenas
-- que NAO foi concedido; a prova de que o INSERT funciona sem ele vem da
-- execucao de F6.6 e M6.6.
SELECT r.rolname,
       pg_get_serial_sequence('public.mapas', 'id') AS sequencia,
       has_sequence_privilege(r.rolname,
           pg_get_serial_sequence('public.mapas', 'id'), 'USAGE') AS pode_usage_seq
FROM (VALUES ('mapas_teste'), ('api_mapas_leitura')) AS r(rolname)
ORDER BY r.rolname;

-- 5.7 spatial_ref_sys (ST_Transform na API; lookup de SRID no PostGIS)
-- Esperado: true para as duas (via PUBLIC). Se false, ver nota em 4.2.
SELECT r.rolname,
       has_table_privilege(r.rolname, 'public.spatial_ref_sys', 'SELECT') AS pode_ler_spatial_ref_sys
FROM (VALUES ('mapas_teste'), ('api_mapas_leitura')) AS r(rolname)
ORDER BY r.rolname;

-- 5.8 Base casaos
-- Esperado: CONNECT t | CREATE f (para as duas).
-- TEMP normalmente t, herdado de PUBLIC (padrao do PostgreSQL); nao foi
-- removido para nao afetar as demais roles da base.
SELECT r.rolname,
       has_database_privilege(r.rolname, 'casaos', 'CONNECT') AS pode_connect,
       has_database_privilege(r.rolname, 'casaos', 'CREATE')  AS pode_create,
       has_database_privilege(r.rolname, 'casaos', 'TEMP')    AS pode_temp
FROM (VALUES ('mapas_teste'), ('api_mapas_leitura')) AS r(rolname)
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
FROM (VALUES ('mapas_teste'), ('api_mapas_leitura')) AS r(rolname)
ORDER BY r.rolname;


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
-- =====================================================================


-- =====================================================================
-- 7. SSL (acao 5 / S-3) - roda FORA de transacao, depois do COMMIT
-- =====================================================================
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
