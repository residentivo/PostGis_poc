# A fazer

> Situação em 2026-10-23: o pedido do POST (com `public.mapas.id` como PRIMARY KEY, uma geometria por id) foi
> implementado, revisado e testado (PO1-R a PO7 em `feito.md`). A versão nova da API só grava depois da migração
> (PU-DBA) e da ação 5. Antes disso (2026-10-22), o usuário rotacionou a senha de `casaos`, testou a API, ligou o
> SSL e executou as inserções em `graphy` e `metry`. Restam as pendências abaixo, todas do usuário/DBA ou sob pedido.

## Pedido (2026-10-23): POST para `graphy` e `metry`, com `public.mapas.id` como PRIMARY KEY (`api_mapas`)

> Implementado, revisado e testado (PO1-R a PO7 em `feito.md`, com o contrato e as decisões de referência). Restam só as duas ações do usuário abaixo. Documentação: `api_mapas/README.md`, `database/README.md` e as coleções em `api_mapas/postman/`.

### PU-R - Aceite dos riscos residuais R-N1 a R-N5 (usuário; RS-26) - os agentes NÃO decidem
- Responsável: usuário. Status: pendente. Não bloqueia o uso local; o README da `api_mapas` já lista os riscos como "aceite pendente". Ao aceitar (ou recusar algum), o usuário avisa o analista, que registra a decisão e ajusta o README.
  - R-N1: o PUT não tem autenticação, então dá para enumerar ids e sobrescrever geometrias de qualquer id existente. Mitigado pelo U-1 (loopback, usuário único), pelos limitadores e pelo 403 de origem.
  - R-N2: o `Location` é sequencial, o que revela o volume de linhas e o próximo id.
  - R-N3: um INSERT manual com `OVERRIDING SYSTEM VALUE` à frente da sequência faz o POST responder 500 (`23505`) até o DBA rodar o `setval`.
  - R-N4: o POST não é idempotente. Um retry depois de 500 ou timeout pode criar um id a mais.
  - R-N5: a mescla dos duplicados é manual (modelo comentado), e os conflitantes dependem da escolha do usuário.

### PU-DBA - Executar a migração (usuário/DBA, depois do PO3) - os agentes NÃO executam
- Status: pendente (o PO3 foi concluído; comandos em `database/README.md`, "Migração para PRIMARY KEY"). Ordem (RS-11):
  1. desligar a escrita antiga: remover `PG_ESCRITA_*` e/ou `ALTER ROLE api_mapas_escrita NOLOGIN` com `pg_terminate_backend` das sessões;
  2. backup (`database/backup_mapas.*`);
  3. relatório;
  4. deduplicação (mescla e escolha dos conflitantes);
  5. segundo backup, se houver intervalo grande desde o primeiro;
  6. migração;
  7. `VALIDATE` do CHECK;
  8. `roles.sql` revisado e conferência da seção 5;
  9. só então religar a escrita, com a versão nova da API: `\password api_mapas_escrita` com senha **nova** e `PG_ESCRITA_*` na sessão; o log de início deve dizer "Escrita (PUT e POST) HABILITADA" (ver também a ação 5 abaixo).
- Sem a migração, a versão nova da API sobe com a escrita desligada (`SemChavePrimaria`) e o GET responde 500 para ids com 2 ou mais linhas.

---

## Pendências do usuário (parecer M2) — os agentes NÃO executam
- **REC-3 (sugestão)**: criar `.gitignore` na raiz do repositório e avaliar tirar o projeto do OneDrive.
- **REC-5 (sugestão)**: `tarefas/f3.tmp` não contém segredo; o usuário pode removê-lo.

---

## Ações do usuário/DBA, em ordem de prioridade (parecer SecBrain sobre F3.7/M3.7) — os agentes NÃO executam

1. **Restringir o `pg_hba` (S-4, Alta)**
   - Só `scram-sha-256`, só IPs `/32`, `casaos` só local, porta 5432 fechada fora da LAN.
   - Linhas `hostssl ... /32 scram-sha-256` mais as linhas `host ... reject` para `mapas_teste` e `api_mapas_leitura`, antes da linha genérica; depois `SELECT pg_reload_conf();`.
   - Verificar também `listen_addresses` e a exposição da porta no Docker.
   - Conferir com `SELECT * FROM pg_hba_file_rules;`.
   - Status: pendente
2. **Roles `mapas_teste` e `api_mapas_leitura` (S-1, Crítica)**
   - Script `database/roles.sql` (procedimento em `database/README.md`). Se ele já foi executado junto com a ligação do SSL, falta apenas confirmar a seção 5 (esperado: `CREATE` no schema public = f) e definir as senhas com `\password mapas_teste` e `\password api_mapas_leitura` (16+ caracteres, senhas distintas).
   - Daqui em diante, usar essas roles nos testes e na API em vez de `casaos`.
   - Pós-teste, opção preferida: `ALTER ROLE mapas_teste NOLOGIN PASSWORD NULL;` seguido de `pg_terminate_backend` das sessões de `mapas_teste`.
   - Avaliar também o índice `ix_mapas_id` (sugerido em `api_mapas/sql/consultas_referencia.sql`).
   - Status: pendente de confirmação pelo usuário
3. **Backup e chave de `public.mapas` (S-5, Média)**
   - `pg_dump -t public.mapas` periódico, agora com `database/backup_mapas.sh`/`.ps1` (os testes de integração fazem DELETE dos ids que inserem e o PUT apaga e regrava linhas).
   - Avaliar PK/UNIQUE em `id`, verificando antes se há duplicados.
   - Status: pendente
4. **Atualizar a versão (S-6, Baixa)**
   - Rodar `SHOW server_version`, atualizar para a última 17.x e para a imagem PostGIS mais recente (GEOS 3.9.0 e PROJ 7.2.1 estão antigos).
   - Status: pendente
5. **Ligar a escrita (PUT e POST) da `api_mapas` (opcional, só se o usuário for usar a escrita; U-1, U-4, RP-16, RP-17)**
   - **Só depois do PU-DBA** (migração para PRIMARY KEY): o `roles.sql` revisão 5 aborta antes dela, e a API nova desliga a escrita sem a PK.
   - Passo a passo em `database/README.md`: "Migração para PRIMARY KEY (PU-DBA)", passos 8 e 9, e "Procedimento do DBA para ligar o PUT", passos 3 a 6. Resumo:
     1. **Backup** de `public.mapas` (`database/backup_mapas.sh --container <container>` ou `database/backup_mapas.ps1`), como `casaos`. Conferir `Backup OK`, a contagem e o SHA-256. Se der erro, parar.
     2. **`database/roles.sql`** (revisão 5) como superusuário, por socket local, com `-v pular_ssl=1`. Se abortar, trazer a mensagem para análise.
     3. **`\password api_mapas_escrita`**: senha nova, 16 ou mais caracteres, diferente das de `casaos`, `api_mapas_leitura` e `mapas_teste`.
     4. **`pg_hba.conf`**: `hostssl casaos api_mapas_escrita <IP da API>/32 scram-sha-256` e a role nas linhas `reject`, antes da linha genérica; `SELECT pg_reload_conf();` e conferir `pg_hba_file_rules`. Pode ser feito junto com a ação 1.
     5. **Conferência**: seção 5 do `roles.sql` (5.4 `DELETE` = f, 5.5 INSERT em `id` = f, 5.12 sequência, 5.13 constraints); conexão `sslmode=require` como `api_mapas_escrita` com `ssl = t` e `lock_timeout = 5s`.
     6. **Só então `PG_ESCRITA_USER`/`PG_ESCRITA_PASSWORD`** na sessão da API (nunca em arquivo). No log de início deve aparecer "Escrita (PUT e POST) HABILITADA".
   - Condições do U-1 enquanto a escrita estiver ligada: máquina de um único usuário, API só em loopback e nunca exposta por proxy, túnel ou port-forward.
   - Ids criados por POST de teste: limpeza com `mapas_teste` religada temporariamente ou `casaos` local (`database/README.md`, "Limpeza de ids de teste").
   - Para desligar depois: remover `PG_ESCRITA_*` ou rodar a seção 6 do `roles.sql` (`NOLOGIN PASSWORD NULL`).
   - Status: pendente (decisão do usuário)

**Opcional (DataBrain, sob pedido do usuário):** CHECK de SRID em `graphy` (4326) e em `metry` (4326, 4674, 3857, 31983), criado com `NOT VALID`. Se for criado, o analista atualiza o manual da API, que deixa de citar SRID 0.

### Pendência de BACKEND: CA nos clientes para `VerifyFull` (R4 do SecBrain na entrega do SSL)
- Responsável: BackBrain. **Ainda não planejada; só executar se o usuário pedir.** Qualquer mudança passa pelo SecBrain e pelo TestBrain.
- Motivo: `PG_SSLMODE=Require` cifra a conexão, mas não valida o servidor (sujeito a MITM).
- Escopo: `teste_graphy`, `teste_metry` e `api_mapas`.
  - Nova variável de ambiente com o caminho da CA (ex.: `PG_SSLROOTCERT`), para permitir `PG_SSLMODE=VerifyFull` com a CA gerada por `database/gerar_certificado_ssl.sh`.
  - Avaliar `Channel Binding=Require` no Npgsql.
  - Atualizar o `launch.json` (input do caminho da CA) e os READMEs dos três programas.
  - Nunca importar o `ca.crt` no repositório de certificados do Windows.
  - C8 do parecer P5 do PUT (DataBrain): `sslmode=verify-full` com a CA em `database/backup_mapas.sh`/`.ps1`.
- Status: pendente (aguardando pedido do usuário)
