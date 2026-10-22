# A fazer

> Situação em 2026-10-22: o usuário rotacionou a senha de `casaos` (os 3 `.env` já não existem),
> testou a API com a senha nova, ligou o SSL com o script do banco e executou com sucesso as
> inserções em `graphy` e `metry`. F6.6, M6.6, A7.3, a ação 1, a ação 5 e o RQ-9 foram
> registrados em `feito.md`. Restam as pendências abaixo.

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
   - Para o DataBrain: atualizar o comentário do bloco de role em `api_mapas/sql/consultas_referencia.sql` para apontar `database/roles.sql` como fonte oficial.
3. **Backup e chave de `public.mapas` (S-5, Média)**
   - `pg_dump -t public.mapas` periódico (os testes de integração fazem DELETE dos ids que inserem).
   - Avaliar PK/UNIQUE em `id`, verificando antes se há duplicados.
   - Status: pendente
4. **Atualizar a versão (S-6, Baixa)**
   - Rodar `SHOW server_version`, atualizar para a última 17.x e para a imagem PostGIS mais recente (GEOS 3.9.0 e PROJ 7.2.1 estão antigos).
   - Status: pendente

**Opcional (DataBrain, sob pedido do usuário):** CHECK de SRID em `graphy` (4326) e em `metry` (4326, 4674, 3857, 31983), criado com `NOT VALID`. Se for criado, o analista atualiza o manual da API, que deixa de citar SRID 0.

### Pendência de BACKEND: CA nos clientes para `VerifyFull` (R4 do SecBrain na entrega do SSL)
- Responsável: BackBrain. **Ainda não planejada; só executar se o usuário pedir.** Qualquer mudança passa pelo SecBrain e pelo TestBrain.
- Motivo: `PG_SSLMODE=Require` cifra a conexão, mas não valida o servidor (sujeito a MITM).
- Escopo: `teste_graphy`, `teste_metry` e `api_mapas`.
  - Nova variável de ambiente com o caminho da CA (ex.: `PG_SSLROOTCERT`), para permitir `PG_SSLMODE=VerifyFull` com a CA gerada por `database/gerar_certificado_ssl.sh`.
  - Avaliar `Channel Binding=Require` no Npgsql.
  - Atualizar o `launch.json` (input do caminho da CA) e os READMEs dos três programas.
  - Nunca importar o `ca.crt` no repositório de certificados do Windows.
- Status: pendente (aguardando pedido do usuário)
