# Feito

## Pedido: Programa de teste para inserir dados em `public.mapas.graphy` (PostGIS)

### F1 - Planejamento detalhado do programa de teste
- Responsável: analista (SubBrain)
- Concluído em: 2026-10-21
- O que foi gerado:
  - Decisões registradas (padrões da orquestração): qualquer WKT válido, validado antes do INSERT; entrada por argumento de linha de comando ou 3 exemplos fixos perto de São Paulo; SRID 4326; credenciais por variáveis de ambiente `PG_*`; console .NET `net10.0` + Npgsql isolado em `teste_graphy/`.
  - Estrutura de pastas e nomes de projeto (`TesteGraphy`, `TesteGraphy.Tests`, `TesteGraphy.sln` local).
  - Tarefas detalhadas por camada em `tarefas/afazer.md`: F2 (security, 7 itens), F3 (database, 5 itens, só leitura + SQL de referência), F4 (backend, 9 itens), F5 (security, 5 itens), F6 (qa, 6 itens), F7 (fechamento).
  - Observação para o DataBrain: a DDL usa `geography` sem typmod (SRID não restrito na coluna), por isso o programa força 4326 via `ST_GeomFromText(@wkt, 4326)::geography`.
  - Frontend fora do escopo; coleção Postman não aplicável (sem API HTTP).

### F2 - Revisão de segurança prévia (credenciais e conexão)
- Responsável: security (SecBrain)
- Concluído em: 2026-10-21 — plano APROVADO com requisitos obrigatórios.
- O que foi gerado (incorporado em F3 `fazendo.md` e F4/F6 `afazer.md`):
  - R1 Credenciais só por env; `PG_USER`/`PG_PASSWORD` sem padrão; nada de senha/connection string por argumento, `appsettings.json` ou `launchSettings.json`.
  - R2 Sem vazamento: `Descricao()` só host:porta/base e usuário; sem `ex.ToString()`/stack; `PostgresException` -> SqlState+MessageText; falhas de conexão -> mensagem genérica; sem Include Error Detail; Persist Security Info false; WKT ecoado truncado (~200) com controles trocados por `?`.
  - R3 SQL constante só com `@wkt`/`@id`, `NpgsqlDbType` explícito, SRID 4326 literal.
  - R4 `WktValidator`: InvariantCulture/NumberStyles.Float, rejeita NaN/Infinity, profundidade máx. 10, sem Regex com backtracking, só 2D (rejeita Z/M/EMPTY), máx. 100 WKT por execução, dupla validação local + ST_IsValid.
  - R5 `PG_SSLMODE` com lista permitida, padrão Prefer com aviso, sem TrustServerCertificate; Timeout=10, CommandTimeout=30, ApplicationName=TesteGraphy.
  - R6 `.gitignore` completo, `.env.example` só com placeholders, README com leitura segura de senha, aviso PSReadLine e OneDrive; programa não carrega `.env`.
  - R7 Integração: opt-in `PG_RUN_INTEGRATION=1`, DELETE só dos ids do RETURNING em finally com conferência de 1 linha.
  - Recomendações S1–S6 (privilégios/atributos da role, ssl/password_encryption, ausência de PK, Npgsql fixo + varredura de vulneráveis, `await using`, testes de segurança).

### F3 - Validação da tabela alvo (somente leitura) + SQL de referência
- Responsável: database (DataBrain)
- Concluído em: 2026-10-21 (scripts entregues; a execução no servidor depende das credenciais do usuário)
- O que foi gerado:
  - `teste_graphy/sql/validacao.sql` (só SELECT/SHOW): extensão PostGIS e versão, `geography_columns` e `information_schema.columns` (tipo, SRID, `is_identity`), `has_table_privilege` (INSERT/SELECT/DELETE) e `rolsuper`/`rolcreaterole`/`rolcreatedb` do `current_user` (S1), `SHOW ssl` e `SHOW password_encryption` (S2).
  - `teste_graphy/sql/insert_referencia.sql`: validação (`ST_IsValid`), INSERT `ST_GeomFromText(@wkt, 4326)::geography RETURNING id`, leitura (`ST_AsText`, `ST_SRID`) e DELETE por id (só para os testes).
  - Registrado que `public.mapas` não tem PK nem UNIQUE em `id` (S3).
  - [x] F3.1–F3.6
  - [x] F3.7: `validacao.sql` executado pelo usuário em 2026-10-21. Os resultados e as ressalvas estão no bloco "F3.7 e M3.7 - Validação do servidor", mais abaixo.

### F4 - Programa console de inserção em `graphy`
- Responsável: backend (BackBrain)
- Concluído em: 2026-10-21
- O que foi gerado (`teste_graphy/`):
  - `TesteGraphy.sln`, `src/TesteGraphy` (console net10.0, Npgsql `[10.0.3]` fixo), `tests/TesteGraphy.Tests` (xUnit 2.9.3, coverlet 6.0.4, versões fixas).
  - Configuração (`ConexaoConfig`, `ConfiguracaoInvalidaException`, `ConfiguracaoAplicacao`): leitura das variáveis `PG_*`, lista permitida de SSL, Timeout=10/CommandTimeout=30/ApplicationName/PersistSecurityInfo=false; `Descricao()` sem senha.
  - Validação (`WktValidator`, `ResultadoValidacao`): tokenizador manual sem Regex, todas as regras R4, além de anel de POLYGON fechado e LINESTRING com 2 ou mais pontos.
  - Dados (`IGraphyRepository`, `GraphyRepository`, `RegistroGraphy`, `ValidacaoBanco`, `FalhaBancoException`, `FalhaConexaoException`): SQL idêntico a `insert_referencia.sql`, `NpgsqlDbType` explícito, `await using`, `NpgsqlDataSource`.
  - Aplicação (`InsercaoService`, `ProgramaConsole`, `ResultadoInsercao`, `CodigoSaida` 0/1/2/3), `Program.cs`, `Exemplos/ExemplosFixos` (3 WKT em São Paulo), `Comum/TextoSeguro` (eco truncado em 200 caracteres, controles trocados por `?`).
  - Log verboso opcional (`Diagnostico/`, `appsettings.json` só com a chave `Verboso`).
  - `.gitignore`, `.env.example` (placeholders), `README.md`.
  - Correções da F5: SqlState `28*`/`08*`/`3D*`/`57P03`/`53300` tratados como falha de conexão (interrompem os itens seguintes); `Descricao()` sanitizada; try/catch em `ConfiguracaoAplicacao.Carregar`; `.gitignore` ampliado (`appsettings.*.json`, `*.pfx`, `*.key`, `*.pem`).
  - Correções da F6: D1 (`PG_SSLMODE` vazio é rejeitado, código 1) e O2 (`PG_PASSWORD` só com espaços é rejeitada).
  - Build: 0 avisos, 0 erros. `dotnet list package --vulnerable --include-transitive`: nenhum pacote vulnerável.
  - [x] F4.1–F4.9

### F5 - Revisão de segurança pós-implementação
- Responsável: security (SecBrain)
- Concluído em: 2026-10-21 — APROVADO na revisão final.
- O que foi gerado: varredura de segredos, parametrização, ausência de vazamento em exceções/logs e conferência de R1–R7/S1–S6. Uma correção obrigatória (A07: falha de autenticação/conexão vinda de `PostgresException`) e ajustes menores, todos aplicados pelo BackBrain (ver F4).
  - [x] F5.1–F5.5

### F6 - Testes do programa
- Responsável: qa (TestBrain)
- Concluído em: 2026-10-21 (exceto a execução real da integração)
- O que foi gerado (`teste_graphy/tests/TesteGraphy.Tests/`):
  - Testes unitários de `ConexaoConfig`, `ConfiguracaoAplicacao`, `WktValidator`, `InsercaoService`, `ProgramaConsole`, entry point, `TextoSeguro`, `GraphyRepository` (servidor Postgres falso), `ExemplosFixos` e log, com os fakes em `Fakes/`.
  - Testes de segurança S6: senha sentinela, eco truncado, injeção de SQL rejeitada, NaN/Infinity, `1,5`, Z/M/EMPTY, profundidade e 10.000 vs 10.001 caracteres.
  - `Dados/GraphyRepositoryIntegrationTests.cs` (R7): opt-in `PG_RUN_INTEGRATION=1` + `PG_USER`/`PG_PASSWORD`; apaga só os ids do RETURNING e confere 1 linha por DELETE.
  - Resultado final: 394 testes, 391 aprovados, 0 falhas, 3 ignorados (integração). Cobertura de linhas: 93,99% (meta de 80%).
  - Defeitos reportados e corrigidos: D1 e O2. Observação O1 (até 2 logins falhos por execução com senha errada) documentada no README.
  - [x] F6.1–F6.5, F6.7
  - [ ] **PENDENTE F6.6 (execução)**: rodar a integração contra 192.168.168.106/casaos com credenciais do usuário: `PG_RUN_INTEGRATION=1` + `dotnet test TesteGraphy.sln --filter "Category=Integration"`. Os testes já estão escritos.

### F7 - Fechamento
- Responsável: analista (SubBrain)
- Concluído em: 2026-10-21
- O que foi gerado:
  - F3–F6 movidas para `feito.md`, com F3.7 e F6.6 (execução) marcadas como pendentes.
  - `teste_graphy/README.md` atualizado e usado como manual único:
    - como executar, variáveis de ambiente e códigos de saída (já existentes);
    - novas seções: resultado dos testes, testes só unitários (`--filter "Category!=Integration"`, conferido: 391 aprovados) e testes de integração com opt-in;
    - manual da base de dados `public.mapas`: colunas, SRID, SQL usado e S3 (sem PK);
    - observação O1 (até 2 logins falhos por execução com senha errada);
    - Coleção Postman: não aplicável.
  - Manual de telas: não aplicável (programa console sem interface; o passo a passo do console está no README).

---

## Pedido: Programa de teste para inserir dados em `public.mapas.metry` (PostGIS, geometry)

### M1 - Planejamento detalhado do programa `teste_metry`
- Responsável: analista (SubBrain)
- Concluído em: 2026-10-21
- O que foi gerado:
  - Decisões finais D-M1 a D-M9 registradas em `afazer.md` (estrutura `teste_metry/`, nomes `Metry`, herança de R1–R7/S1–S6/A07/D1/O2, `PG_SRID` com parsing estrito e lista permitida 4326/4674/3857/31983, SQL `ST_GeomFromText(@wkt, @srid)` com `@srid` Integer e conferência de `ST_SRID` na leitura, faixas de coordenadas por SRID, rejeição de EWKT, exemplos fixos por SRID, escopo sem DDL).
  - Ajuste em relação ao pressuposto do líder: SRIDs projetados (3857 e 31983) também têm faixas de coordenadas, e não só finitude.
  - Tarefas detalhadas por camada em `afazer.md`: M3 (database, 7 itens), M4 (backend, 9 itens), M5 (security, 5 itens), M6 (qa, 7 itens), M7 (fechamento, 4 itens); checklist da M2 (security, 7 itens) em `fazendo.md`.
  - Frontend, coleção Postman e manual de telas: não aplicáveis.
- Decisões finais (movidas de `afazer.md` na M7):
  - **D-M1 Estrutura**: pasta isolada `teste_metry/` com `TesteMetry.sln`, `src/TesteMetry` (console net10.0, Npgsql `[10.0.3]` fixo), `tests/TesteMetry.Tests` (xUnit 2.9.3, coverlet 6.0.4) e `sql/`. Nenhuma referência ou arquivo compartilhado com `teste_graphy/`.
  - **D-M2 Nomes**: mesmo desenho do `teste_graphy`, trocando `Graphy` por `Metry` (`Configuracao/`, `Validacao/` com `LimitesSrid`, `Dados/` com `RegistroMetry` id + wkt + srid, `Aplicacao/`, `Comum/TextoSeguro`, `Diagnostico/`, `Exemplos/ExemplosFixos`); `ApplicationName=TesteMetry`.
  - **D-M3 Herança**: R1–R7, S1–S6, A07 (F5), D1 e O2 (F6) do graphy.
  - **D-M4 `PG_SRID`**: ausente -> 4326; vazia/espaços -> código 1; só dígitos ASCII, máx. 6 caracteres, `NumberStyles.None`; lista 4326/4674/3857/31983; mensagem sem eco do valor; lido uma vez; `Descricao()` mostra `srid=`.
  - **D-M5 SQL**: `ST_GeomFromText(@wkt, @srid)` sem cast; `@wkt` Text, `@srid`/`@id` Integer; SQL constante; divergência de `ST_SRID` na leitura = código 2.
  - **D-M6 Faixas**: 4326/4674 lon [-180, 180], lat [-90, 90]; 3857 x/y [-20037508.3428, 20037508.3428]; 31983 E [0, 1000000], N [0, 10000000]; finitude obrigatória; tabela constante `LimitesSrid`.
  - **D-M7 WKT**: regras R4 do graphy; EWKT rejeitado; 7 tipos aceitos; dupla validação local + `ST_IsValid`.
  - **D-M8 Exemplos fixos por SRID**: 3 WKT em São Paulo por SRID (ex.: Praça da Sé `POINT(-46.6333 -23.5505)` em 4326/4674, `POINT(-5191195.21 -2698729.46)` em 3857, `POINT(333250 7394600)` em 31983).
  - **D-M9 Escopo**: sem alterar a tabela; DELETE por id só nos testes de integração; frontend, Postman e manual de telas fora do escopo.

### M2 - Revisão de segurança prévia
- Responsável: security (SecBrain)
- Concluído em: 2026-10-21 — plano APROVADO COM REQUISITOS.
- O que foi gerado (parecer; requisitos distribuídos em M3 `fazendo.md` e M4–M7 `afazer.md`):
  - [x] M2.1–M2.7 revisados.
  - Obrigatórios:
    - RQ-1 (M4.2) `PG_SRID` convertido uma única vez em int da lista constante; vai ao banco só como `@srid` `NpgsqlDbType.Integer`, nunca em string SQL (inclusive `ValidacaoBanco`, leitura e DELETE); `Descricao()`/log mostram só o int validado; erro cita nome da variável e valores permitidos, nunca o valor recebido (inclusive no log verboso).
    - RQ-2 (M4.2/M4.5) `PG_SRID` só do ambiente; nada por argumento, `appsettings.json` ou `launchSettings.json`; argumentos continuam só WKT.
    - RQ-3 (M4.3) validador por lista permitida dos 7 tipos (case-insensitive); demais palavras-chave (curvas, superfícies, BOX, TRIANGLE, TIN) rejeitadas localmente; tokenizador aceita só letras dos tipos, dígitos, `. - + E`, espaço, parênteses e vírgula (rejeita `;`, `=`, controles, HEX/WKB, espaços Unicode). EWKT rejeitado independentemente de detectar `SRID=`.
    - RQ-4 (M6.2) testes negativos obrigatórios (EWKT em variações, CIRCULARSTRING, TRIANGLE, BOX, hex de WKB, U+00A0).
    - RQ-5 (M4.3/M6.2) faixas D-M6 em todas as coordenadas e todos os níveis (MULTI*, GEOMETRYCOLLECTION), comparação inclusiva, SRID do validador = int do RQ-1; teste cruzado 3857/4326/31983.
    - RQ-6 (M3.5/M6.6) DELETE de limpeza em transação (rowcount ≠ 1 -> ROLLBACK e falha; = 1 -> COMMIT), `WHERE id = @id AND ST_SRID(metry) = @srid` tipados, no finally para cada id do RETURNING mesmo após falha anterior; DELETE continua proibido no programa.
    - RQ-7 (M4.7) `teste_metry/.gitignore` idêntico ao do graphy; `.env.example` só placeholders e `PG_SRID=4326`; NÃO criar `teste_metry/.env`.
    - RQ-8 (M4) entradas novas em `.vscode/launch.json`/`tasks.json` para o metry usam `${input:pgUser}`/`${input:pgPassword}` (promptString, password:true); `PG_SRID` por pickString com os 4 valores ou ausente.
    - RQ-9 (pendência do usuário) `teste_graphy/.env` contém credencial real numa pasta sincronizada pelo OneDrive. Ver "Pendências do usuário" em `afazer.md`. Os agentes NÃO apagam o arquivo.
  - Recomendações: REC-1 rejeitar zeros à esquerda em `PG_SRID`; REC-2 README documenta SRIDs misturados em `metry`, linhas permanentes e SQL manual de limpeza por id; REC-3 sugerir `.gitignore` na raiz e avaliar tirar o projeto do OneDrive (sugestão ao usuário); REC-4 `validacao.sql` com `pg_stat_ssl` e orientação `PG_SSLMODE=Require/VerifyFull`; REC-5 `tarefas/f3.tmp` sem segredo, o usuário pode remover; REC-6 versões fixas iguais ao graphy e varredura de vulneráveis na M5.

### M3 - Validação da coluna `metry` (somente leitura) + SQL de referência
- Responsável: database (DataBrain)
- Concluído em: 2026-10-21 (scripts entregues; a execução no servidor depende das credenciais do usuário)
- O que foi gerado (`teste_metry/sql/`):
  - `validacao.sql` (só SELECT/SHOW): PostGIS e versão; `geometry_columns` e `information_schema.columns` para `metry`/`id`; os 4 SRIDs em `spatial_ref_sys`; ausência de CHECK/trigger em `metry`; privilégios INSERT/SELECT/DELETE e `rolsuper`/`rolcreaterole`/`rolcreatedb` (S1, RQ-9); `SHOW ssl`, `SHOW password_encryption` e `pg_stat_ssl` (REC-4); contagem de linhas por `ST_SRID(metry)` (REC-2); listagem das constraints (S3); consulta opcional M3.6 com `ST_Transform` 4326 -> 3857/31983 para conferir os exemplos D-M8.
  - `insert_referencia.sql`: SQL oficial (validação `ST_IsValid`/`GeometryType`, INSERT `ST_GeomFromText(@wkt, @srid) RETURNING id`, leitura com `ST_SRID`), `@srid` sempre parâmetro (RQ-1); DELETE de limpeza transacional RQ-6 e SQL manual por id (REC-2). Correção C-2 (M5): comando DELETE comentado e aviso sobre a sintaxe `@` no cabeçalho; os comandos 1–3 podem ficar ativos (aprovado pelo SecBrain).
  - [x] M3.1–M3.6
  - [x] M3.7: `validacao.sql` executado pelo usuário em 2026-10-21. Os resultados e as ressalvas estão no bloco "F3.7 e M3.7 - Validação do servidor", mais abaixo.

### M4 - Programa console de inserção em `metry`
- Responsável: backend (BackBrain)
- Concluído em: 2026-10-21
- O que foi gerado (`teste_metry/`):
  - `TesteMetry.sln`, `src/TesteMetry` (net10.0, Npgsql `[10.0.3]`), `tests/TesteMetry.Tests` (xUnit 2.9.3, coverlet 6.0.4), `.gitignore` idêntico ao do graphy, `.env.example` só com placeholders, `README.md`; entradas `TesteMetry: ...` em `.vscode/launch.json` (inputs `pgUser`/`pgPassword` com `password: true`, `PG_SRID` por pickString) e `build-teste-metry` em `.vscode/tasks.json`. `teste_metry/.env` não criado.
  - Configuração com `PG_SRID` (D-M4, RQ-1, RQ-2, REC-1); `WktValidator` por lista permitida e faixas por SRID em todos os níveis (`LimitesSrid`, RQ-3, RQ-5, EWKT rejeitado); `MetryRepository` com SQL idêntico ao M3.5, `NpgsqlDbType` explícito e conferência de `ST_SRID` na leitura (novo `StatusItem.SridDivergente`, código 2); XX000/classe 22 na validação = `InvalidoBanco`; sem DELETE no programa; A07, R2, `TextoSeguro`, log verboso.
  - Correções da M5 (M4.11): C-1 `TextoSeguro` troca por `?` caracteres de controle, formato, separadores, surrogates soltos, uso privado, não atribuídos e espaços Unicode, sem cortar par surrogate; R-1 input `wktMetry` com `default` vazio; R-3 `ExemplosFixos.Para(srid)` dentro do `try`.
  - Build: 0 avisos, 0 erros. `dotnet list package --vulnerable --include-transitive`: nenhum pacote vulnerável.
  - [x] M4.1–M4.11

### M5 - Revisão de segurança pós-implementação
- Responsável: security (SecBrain)
- Concluído em: 2026-10-21 — APROVADO na revisão final.
- O que foi gerado: varredura de segredos (sem `teste_metry/.env`), parametrização com `@srid` Integer em todos os comandos, `PG_SRID` estrito e sem eco, vazamento em exceções/logs, A07, R1–R7/S1–S6, RQ-1 a RQ-8 e REC-6 (versões fixas, sem vulneráveis).
  - Correções: C-1 (`TextoSeguro`) e C-2 (DELETE comentado em `insert_referencia.sql`), R-1 (`launch.json`), R-3 (`ExemplosFixos` no `try`; aceito, código 3 inalcançável na prática), R-2 (comando `psql` do README com `sslmode=require`, aplicado na M7).
  - [x] M5.1–M5.6

### M6 - Testes do programa
- Responsável: qa (TestBrain)
- Concluído em: 2026-10-21 (exceto a execução real da integração)
- O que foi gerado (`teste_metry/tests/TesteMetry.Tests/`):
  - `Configuracao/ConexaoConfigTests.cs` e `ConfiguracaoAplicacaoTests.cs` (M6.1: `PG_SRID` ausente, 4 permitidos, 30 inválidos inclusive REC-1, mensagem e log sem o valor).
  - `Validacao/WktValidatorTests.cs` e `LimitesSridTests.cs` (M6.2: RQ-4 e RQ-5 completos, limites inclusivos, testes cruzados 3857/4326/31983).
  - `Exemplos/ExemplosFixosTests.cs` (M6.3), `Dados/MetryRepositoryTests.cs` com servidor Postgres falso (M6.4: SQL constante sem SRID literal, A07).
  - `Aplicacao/InsercaoServiceTests.cs`, `ProgramaConsoleTests.cs`, `ProgramEntryPointTests.cs`, `Comum/TextoSeguroTests.cs`, `Diagnostico/LogTests.cs` (M6.5: códigos 0/1/2/3, RQ-2, S6 senha sentinela, C-1).
  - `Dados/MetryRepositoryIntegrationTests.cs` (M6.6: opt-in, 4 SRIDs, limpeza transacional RQ-6).
  - Resultado: 715 testes, 712 aprovados, 0 falhas, 3 ignorados (integração). Cobertura: linhas 93,75% (945/1008), ramos 80,8%.
  - Sem defeitos bloqueantes; 4 observações registradas como melhorias futuras em `afazer.md`.
  - [x] M6.1–M6.5, M6.7
  - [ ] **PENDENTE M6.6 (execução)**: rodar a integração com credenciais do usuário (ver `afazer.md`). Os testes já estão escritos.

### M7 - Fechamento
- Responsável: analista (SubBrain)
- Concluído em: 2026-10-21
- O que foi gerado:
  - M3–M6 movidas para `feito.md`, com M3.7 e M6.6 (execução) marcadas como pendentes.
  - `teste_metry/README.md` atualizado como manual único:
    - R-2 (M5): comando `psql` da validação na forma `"host=192.168.168.106 dbname=casaos user=... sslmode=require"` (PowerShell e bash), com orientação caso o servidor não aceite SSL;
    - conferidos REC-2 (seção "Dados inseridos: permanentes e com SRIDs misturados", com SQL manual de limpeza por id) e REC-4 (seção SSL, `PG_SSLMODE=Require`/`VerifyFull`);
    - novas seções: resultado dos testes, só unitários (`--filter "Category!=Integration"`), cobertura e integração opt-in (com `PG_SSLMODE=Require`);
    - manual da base `public.mapas.metry` (já existente, conferido);
    - "Coleção Postman" e "Manual de telas": não aplicáveis (console, sem API HTTP e sem interface).
  - `afazer.md`: mantidas as pendências do usuário (M3.7, M6.6, RQ-9, REC-3, REC-5) e do graphy (F3.7, F6.6); registradas as melhorias futuras do TestBrain para o BackBrain.
  - [x] M7.1–M7.4

### Melhorias MF-1..MF-4 (teste_metry)
- Origem: melhorias futuras do TestBrain na M6, autorizadas pelo usuário em 2026-10-21. Concluído em: 2026-10-21.
- Fluxo: plano do analista -> SecBrain (prévia: APROVADO, requisitos R-1 a R-18) -> BackBrain -> SecBrain (pós: APROVADO, sem correção obrigatória) -> TestBrain (nenhum defeito) -> fechamento.
- O que foi gerado (BackBrain, `teste_metry/src/TesteMetry/`; build sem avisos):
  - **MF-1** `Validacao/WktValidator.cs`: removido o ramo inalcançável que procurava NaN/Infinity por palavra (a lista de caracteres permitidos, RQ-3, já barra essas palavras). Foi mantida a checagem `double.IsFinite`, com comentário: ela cobre estouro de expoente (ex.: `1e999`). Código de saída inalterado (2).
  - **MF-2** `Dados/MetryRepository.cs`: `AbrirConexaoAsync` captura `PostgresException` só na abertura da conexão e lança `FalhaConexaoException("PostgresException/<SqlState>")`, sem mensagem, Detail, Hint nem InnerException. O resultado é código 3 e os itens seguintes marcados como "não processados". XX000 e classe 22 só dão código 2 quando ocorrem na execução da validação. Na falha de abertura o log usa `_log.Passo`, o que o SecBrain aceitou. `Aplicacao/InsercaoService.cs` teve só comentários atualizados.
  - **MF-3** `Aplicacao/ProgramaConsole.cs`: um try restrito, antes da fábrica do repositório, trata `ArgumentOutOfRangeException` de `ExemplosFixos.Para` e de `WktValidator` e retorna o código 1 (configuração), com mensagem que não ecoa o valor do SRID.
  - **MF-4** `Dados/MetryRepository.cs`: novas fábricas `internal static` `CriarComandoValidar`, `CriarComandoInserir` e `CriarComandoLer`, sem conexão, com SQL constante e parâmetros tipados (`wkt` Text, `srid` e `id` Integer).
  - `teste_metry/README.md`: atualizadas as linhas dos códigos 1 e 3, a observação operacional sobre falha na abertura da conexão, a seção "Build, testes" (resultado e cobertura; o achado Baixo do SecBrain sobre o texto do ramo não finito foi corrigido) e a descrição dos modos do servidor falso.
- Testes (TestBrain): 881 de 881 testes unitários passando, cobertura de linhas 98,64% e de ramos 95,22%, nenhum defeito.
  - Novos modos no `ServidorPostgresFalso`: `ComErroNaExecucao`, `SemLinhas`, `ComLinhas` e `ComandosRecebidos`. A `FalhaBancoException` na execução passou a ser coberta, sem lacuna para a M6.6.
  - O cenário da MF-3 não é alcançável pela configuração; foi coberto por reflexão em `ProgramaConsoleSridSemExemplosTests`.
  - O Npgsql remove o `;` final do SQL enviado.
  - Seguem descobertos: o caminho com credenciais de `Program.cs` e o `catch` de `SocketException`/`TimeoutException` do repositório (defesa em profundidade).
- Coleção Postman e manual de telas: não aplicáveis (console, sem API HTTP e sem interface).
- [x] MF-1, MF-2, MF-3, MF-4

### F3.7 e M3.7 - Validação do servidor (`teste_graphy/sql/validacao.sql` e `teste_metry/sql/validacao.sql`)
- Executado pelo usuário em 2026-10-21, com `docker exec` e `psql -U casaos`, por conexão local via socket (não pela rede). Interpretação: DataBrain. Parecer: SecBrain.
- **OK**
  - Versões: PostGIS 3.5.2, PostgreSQL 17, GEOS 3.9.0, PROJ 7.2.1.
  - `graphy`: `geography` sem typmod (srid 0).
  - `metry`: `geometry` sem typmod (coord_dimension 2, srid 0).
  - `id`: integer identity BY DEFAULT.
  - Sem constraints e sem triggers, como os scripts esperavam.
  - Os SRIDs 4326, 4674, 3857 e 31983 existem em `spatial_ref_sys`. `ST_Transform` funcionou e bate com `ExemplosFixos` (D-M8).
  - `metry` hoje tem 3 linhas, todas com SRID 4326.
  - `password_encryption` = scram-sha-256.
- **Diverge**
  - A role `casaos` é superusuária (rolsuper, createrole e createdb ativos). Por isso os privilégios INSERT/SELECT/DELETE = t não provam privilégio mínimo.
  - `ssl = off`. Com isso, `PG_SSLMODE=Require` falha no metry, na api_mapas e no `psql` sugerido, e `Prefer` cai para texto claro.
  - O `pg_hba` não foi verificado.
- **Ressalvas:** privilégio mínimo e TLS remoto não foram validados. Os blocos de privilégios (F3.3/M3.4) devem ser repetidos com a role de teste `mapas_teste`, conectando pela rede. Essa pendência foi registrada em `afazer.md`.
- **Parecer SecBrain**
  - S-1 (Crítica): usar `casaos` nos programas e testes é proibido.
  - S-2 (Crítica): a senha de `casaos` é trivial e está em 3 `.env`. **Achado novo:** `api_mapas/.env` também tem a credencial real; o RQ-9 foi ampliado.
  - S-3 (Alta): SSL desligado.
  - S-4 (Alta): `pg_hba`, `listen_addresses` e a exposição da porta 5432 não foram verificados.
  - S-5 (Média): `public.mapas` não tem PK e os testes fazem DELETE; fazer backup antes e avaliar PK/UNIQUE em `id`.
  - S-6 (Baixa): GEOS/PROJ antigos; atualizar para a última versão 17.x.
  - S-7 (Baixa): igualar a F6.6 à M6.6 (role não superusuária e `Require`).
- **Consequências:** F6.6, M6.6 e A7.3 ficam bloqueadas, e as ações do usuário/DBA foram priorizadas em `afazer.md`. A exceção `PG_SSLMODE=Disable` foi registrada lá como opção que depende de decisão do usuário.
- [x] F3.7, M3.7

### Script das roles `mapas_teste` e `api_mapas_leitura` (`database/roles.sql`) — ações 3 e 4 do DBA
- Responsáveis: DataBrain (script), SecBrain (revisão), sessão principal (teste local) e analista (documentação). Concluído em: 2026-10-21.
- **Revisão do SecBrain:** 3 rodadas (C1–C5, R1–R8), APROVADO depois da C5. O critério de aprovação era `CREATE` no schema public = f.
- **O que foi gerado**
  - `database/roles.sql`: transação única, idempotente, sem senhas, com guardas (base, superusuário, tabela, `id` identity).
  - O script aborta em caso de membership, configuração residual ou dependência inesperada em `pg_shdepend`.
  - Inclui conferências (seção 5) e pós-teste comentado (seção 6).
  - `mapas_teste`:
    - privilégios: `SELECT`, `DELETE`, e `INSERT` só nas colunas `graphy`/`metry`;
    - `CONNECTION LIMIT 5`;
    - `statement_timeout` 30s, `lock_timeout` 10s, `idle_in_transaction_session_timeout` 60s.
  - `api_mapas_leitura`:
    - só `SELECT`;
    - `default_transaction_read_only` on;
    - `statement_timeout` 15s, `idle_in_transaction_session_timeout` 60s;
    - `CONNECTION LIMIT 20`.
  - As duas roles têm `NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS NOREPLICATION NOINHERIT`, `CONNECT` em casaos e `USAGE` (sem `CREATE`) em public. Não há GRANT na sequência da identity.
- **Teste local** (PostGIS 17.5/3.5 descartável no Docker)
  - O script rodou 2 vezes sem erro.
  - `mapas_teste`: fez INSERT em graphy/metry com `RETURNING id`, SELECT e DELETE em transação. Foi negado ao informar `id` e ao fazer UPDATE.
  - `api_mapas_leitura`: fez SELECT e `ST_Transform`. INSERT e CREATE foram negados.
  - Um GRANT extra fez o script abortar pela checagem de `pg_shdepend`, e um `CREATE` residual no schema foi removido.
- **Documentação (analista):** criado o manual da base `database/README.md`, com a tabela `public.mapas`, as roles e seus privilégios, a nota sobre a sequência da identity, o procedimento do DBA e o pós-teste.
- Pendente (em `afazer.md`): a execução no servidor real pelo DBA. O comentário de `api_mapas/sql/consultas_referencia.sql` deve passar a apontar para `database/roles.sql`; é tarefa do DataBrain, porque o arquivo é da camada database.
- [x] Script das roles entregue e aprovado

### SSL do servidor (ação 5 / S-3): seção 7 de `database/roles.sql` e `database/gerar_certificado_ssl.sh`
- Responsáveis: DataBrain (scripts), SecBrain (revisão), sessão principal (teste local) e analista (registro). Concluído em: 2026-10-21.
- **O que foi gerado**
  - `database/roles.sql`, seção 7: liga o SSL com `ALTER SYSTEM`.
    - Guarda que confere a existência do certificado e da chave e recusa chave cifrada.
    - `ssl_min_protocol_version` = TLSv1.2.
    - `ssl_ciphers` = `HIGH:!aNULL:!eNULL:!MD5:!3DES:!RC4`.
    - Reload, seguido de teste obrigatório com rollback.
  - `database/gerar_certificado_ssl.sh`:
    - gera uma CA própria com nameConstraints (só o IP/DNS do servidor) e o certificado do servidor ECDSA P-256, com validade de 825 dias;
    - copia `server.crt` e `server.key` para o container (a chave com modo 600);
    - não sobrescreve sem `--force`; com `--force`, guarda os antigos como `.bak`;
    - recusa CA pela metade e recusa out-dir dentro do OneDrive ou de repositório git;
    - usa um `openssl.cnf` mínimo próprio.
  - `database/README.md`: nova seção "SSL do servidor".
- **Revisão do SecBrain**
  - Seção 7: aprovada.
  - Script `.sh`: C1 e C2 obrigatórias, mais R1–R6, todas aplicadas.
  - A R4 virou uma pendência de backend (CA nos clientes / `VerifyFull`), registrada em `afazer.md`.
- **Teste local** (PostGIS 17.5 descartável no Docker)
  - Sem certificado, a seção 7 aborta e o SSL continua off.
  - O script gerou e copiou o certificado; o defeito do `openssl.cnf` no Git for Windows foi corrigido.
  - O `roles.sql` ligou o SSL.
  - Conexão TCP com `verify-full` e a CA: TLSv1.3.
  - Sem `--force` o script recusa; com `--force` cria os `.bak`.
  - CA pela metade: recusada.
  - Depois de um restart do container, o SSL continuou on.
- Pendente (em `afazer.md`): a execução no servidor real pelo DBA (ação 5 e a ordem geral das ações).
- [x] Scripts de SSL entregues e aprovados

---

## Pedido: API REST `api_mapas` — pontos por id de `public.mapas` (graphy e metry) + coleção Postman

### A1 - Planejamento por camada
- Responsável: analista (SubBrain)
- Concluído em: 2026-10-21
- O que foi gerado (detalhado em `afazer.md`):
  - Contrato da API: `GET /api/mapas/{id}/graphy` e `/metry`, base local `http://localhost:5080`, resposta 200 com lista `geometrias` (`tipo`, `srid`, `wkt`, `geojson` objeto em 4326, `x`/`y` nativos, `longitude`/`latitude` em 4326), erros 400/404/500 em ProblemDetails sem vazamento.
  - Ajustes AJ-1 (id não é único: até 100 itens com `truncado`), AJ-2 (x/y só para POINT) e AJ-3 (SRIDs misturados no metry, `ST_Transform` para 4326).
  - Critérios de aceite CA-1 a CA-8; tarefas detalhadas A2 (security), A3 (database, só SELECT), A4 (backend), A5 (especificação da coleção Postman v2.1), A6 (security), A7 (qa), A8 (fechamento).
  - Frontend fora do escopo.


### Contrato e critérios de aceite (referência, implementados)

Base local padrão: **`http://localhost:5080`** (Kestrel ouvindo só em `localhost`; HTTPS opcional em `https://localhost:7080`). JSON em camelCase, UTF-8. Sem CORS (sem frontend). Somente `GET`.

| Rota | Fonte | 200 | 400 | 404 | 500 |
|------|-------|-----|-----|-----|-----|
| `GET /api/mapas/{id}/graphy` | `public.mapas.graphy` (`geography`, cast `::geometry`) | lista de geometrias | id inválido | id inexistente ou só nulos na coluna | erro interno/banco |
| `GET /api/mapas/{id}/metry`  | `public.mapas.metry` (`geometry`) | idem | idem | idem | idem |

Regras do `{id}`:
- A rota usa `{id}` **sem** constraint `:int` (para devolver 400 em ProblemDetails, não 404 de roteamento). O id é validado na camada de aplicação/domínio.
- Válido: inteiro decimal `1..2147483647` (int4), só dígitos ASCII, sem sinal, sem espaços, até 10 caracteres. Qualquer outra coisa (`abc`, `0`, `-1`, `1.5`, `2147483648`, `+1`) -> 400, sem consultar o banco.

Resposta 200 (`application/json`):
```json
{
  "id": 1,
  "coluna": "graphy",
  "quantidade": 1,
  "truncado": false,
  "geometrias": [
    {
      "tipo": "POINT",
      "srid": 4326,
      "wkt": "POINT(-46.633309 -23.55052)",
      "geojson": { "type": "Point", "coordinates": [-46.633309, -23.55052] },
      "x": -46.633309,
      "y": -23.55052,
      "longitude": -46.633309,
      "latitude": -23.55052
    }
  ]
}
```
- `coluna`: `"graphy"` ou `"metry"`.
- `quantidade`: número de itens em `geometrias` (1..100). `truncado`: `true` se havia mais de 100 linhas não nulas (consulta com `LIMIT 101`).
- `tipo`: `GeometryType` em maiúsculas (`POINT`, `LINESTRING`, `POLYGON`, `MULTIPOINT`, ...).
- `srid`: `ST_SRID` da geometria (graphy normalmente 4326).
- `wkt`: `ST_AsText`, SRID nativo.
- `geojson`: **objeto** JSON (não string), `ST_AsGeoJSON(..., 9)`, em 4326 (AJ-3).
- `x`/`y`: `ST_X`/`ST_Y` no SRID nativo, só para POINT, senão `null`.
- `longitude`/`latitude`: `ST_X`/`ST_Y` de `ST_Transform(geom, 4326)`, só para POINT com SRID > 0, senão `null`.
- Ordenação estável: `ORDER BY ctid` (as linhas não têm outra chave).

Erros (`application/problem+json`, RFC 9457 / ProblemDetails do ASP.NET Core), mensagens em português, **sem** SQL, SqlState, stack, host, usuário ou texto do Npgsql:
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Geometria não encontrada",
  "status": 404,
  "detail": "Nenhuma geometria encontrada na coluna 'graphy' para o id 99.",
  "instance": "/api/mapas/99/graphy",
  "traceId": "00-...-...-00"
}
```
| Status | `title` | `detail` |
|--------|---------|----------|
| 400 | `Id inválido` | `O id deve ser um inteiro entre 1 e 2147483647.` (não ecoa o valor recebido) |
| 404 | `Geometria não encontrada` | `Nenhuma geometria encontrada na coluna '{coluna}' para o id {id}.` |
| 500 | `Erro interno` | `Não foi possível processar a requisição. Informe o traceId ao suporte.` |
- 404 também para rotas desconhecidas e 405 para métodos diferentes de GET (padrão ASP.NET Core, corpo ProblemDetails).
- Falha de conexão/timeout do banco -> 500 com o mesmo corpo genérico; o detalhe técnico vai só para o log (sem senha/connection string).

#### Critérios de aceite gerais
- CA-1: `GET /api/mapas/{id}/graphy` e `/metry` devolvem 200 com o JSON acima para um id com geometria não nula.
- CA-2: id inexistente, ou id cujas linhas têm a coluna nula -> 404 ProblemDetails.
- CA-3: `abc`, `0`, `-1`, `1.5`, `+1`, `2147483648` -> 400 ProblemDetails, sem consultar o banco.
- CA-4: erro de banco -> 500 ProblemDetails genérico com `traceId`; nada do banco no corpo.
- CA-5: SQL constante, parâmetro `@id` com `NpgsqlDbType.Integer`; nenhuma concatenação; role do banco só com `SELECT`.
- CA-6: credenciais só por `PG_*`; nenhuma credencial em `appsettings*.json`, `launchSettings.json`, `.env` ou coleção Postman.
- CA-7: coleção Postman importa no Postman e roda com `postman collection run` sem erros de script.
- CA-8: cobertura >= 80% de linhas no projeto de testes.


### A2 - Revisão de segurança do plano
- Responsável: security (SecBrain)
- Concluído em: 2026-10-21
- Plano aprovado com requisitos obrigatórios RA-1 a RA-18 (role somente leitura, SQL parametrizado, validação do id, ProblemDetails sem vazamento, só loopback, cabeçalhos nosniff/no-store, limites de itens e bytes, logs sem segredos, `PG_SSLMODE` em lista permitida, timeouts, rate limiting, coleção sem segredos, alerta RQ-9).
  - [x] A2

### A3 - Consulta e verificação da tabela
- Responsável: database (DataBrain)
- Concluído em: 2026-10-21 (nada executado no servidor)
- O que foi gerado: `api_mapas/sql/consultas_referencia.sql`
  - Q1 (graphy, `graphy::geometry`) e Q2 (metry): `@id`/`$1`, `IS NOT NULL`, `ORDER BY ctid`, `LIMIT 101`, `geojson` em 4326 (SRID 0 sem `ST_Transform`), x/y e longitude/latitude só para POINT.
  - Sugestões comentadas, não executadas: índice `ix_mapas_id` (`CREATE INDEX CONCURRENTLY`) e role `api_mapas_leitura` (`LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS`, CONNECT/USAGE, só `SELECT ON public.mapas`, `default_transaction_read_only = on`, `statement_timeout = '15s'`, `CONNECTION LIMIT 20`, sem cláusula `PASSWORD` — senha só via `\password`).
  - Pós-A6: O1 (sem `PASSWORD`), O2 (`CONNECTION LIMIT 20`), alerta no `REVOKE ALL ON DATABASE` (nunca `FROM PUBLIC`) e R6 comentado para o DBA (`SHOW server_version`; se < 15 avaliar `REVOKE CREATE ON SCHEMA public FROM PUBLIC`).
  - [x] A3.1–A3.4

### A4 - Implementação da API
- Responsável: backend (BackBrain)
- Concluído em: 2026-10-21
- O que foi gerado: `api_mapas/ApiMapas.sln` com `src/ApiMapas.Dominio` (`ColunaMapa`, `IdMapa`, `GeometriaMapa`, `IMapasRepository`), `src/ApiMapas.Aplicacao` (`ConsultaGeometriasService`, DTOs, limites 100 itens/5 MB), `src/ApiMapas.Infraestrutura` (`ConexaoConfig` PG_*, `MapasRepository` com SQL constante Q1/Q2), `src/ApiMapas.Api` (endpoints, ProblemDetails, cabeçalhos, rate limiting 60/min, só loopback, `AllowedHosts`), `.gitignore`, `.env.example` (placeholders), `README.md`.
  - `dotnet build` 0 erros/0 avisos; sem pacotes vulneráveis.
  - RA-3..RA-15 e REC-A1 aplicados; pós-A6 aplicados R1, R3 e R5.
  - Desvios registrados: `IMapasRepository` no Domínio; sem paginação (máx. 100 + `truncado`); 1º item acima do teto de bytes -> 200 com `geometrias: []` e `truncado: true`.
  - [x] A4.1–A4.8

### A5 - Coleção Postman
- Responsável: backend (BackBrain), revisada pelo analista
- Concluído em: 2026-10-21
- O que foi gerado: `api_mapas/postman/api_mapas.postman_collection.json` (v2.1, `info.name = api_mapas`), variáveis `baseUrl`, `id`, `idInexistente`, `idInvalido`; pastas `graphy` e `metry` com 6 requisições cada (200, 404, 400 texto, 400 zero, 400 longo, 400 `1%27`); testes de status, Content-Type, corpo, não vazamento e cabeçalhos nosniff/no-store; exemplos fictícios salvos; sem auth/segredos.
  - [x] A5

### A6 - Revisão de segurança da implementação
- Responsável: security (SecBrain)
- Concluído em: 2026-10-21 — **APROVADO COM RESSALVAS**
  - O1/O2 (SQL da role): corrigidos pelo DataBrain.
  - R1, R3, R5: aplicados pelo BackBrain.
  - R2 (teto de bytes verificado só após materializar as linhas) e R4 (IP interno no código/README): aceitos como risco de POC.
  - R6: comentado no SQL para o DBA.
  - [x] A6

### A7 - Testes
- Responsável: qa (TestBrain)
- Concluído em: 2026-10-21 (exceto a execução real da integração)
- O que foi gerado: `api_mapas/tests/ApiMapas.Tests/` (`Dominio/`, `Aplicacao/`, `Infraestrutura/`, `Api/`, `Integracao/`, `Suporte/` com `RepositorioFalso`, `LoggerMemoria`, `ApiFactory`, `ServidorPostgresFalso`).
  - 287 testes: 284 aprovados, 0 falhas, 3 ignorados (integração opt-in). Cobertura: linhas 99,61%, branches 98,72%.
  - Postman CLI sem banco: 12 requisições, 0 erros de script; falhas só nos casos 200/404 (recebem 500 sem banco; esperado).
  - Observações: O1 (unix socket no Windows: usar `http://unix:///caminho`) documentada no README; O2 (stack duplicado) reduzido — 3 logs Error por falha de banco, stack só no `ManipuladorExcecoes`.
  - [x] A7.1, A7.2, A7.4, A7.5
  - [x] A7.3: executada pelo usuário em 2026-10-22 (ver bloco "Execuções do usuário em 2026-10-22", ao final).

### A8 - Fechamento
- Responsável: analista (SubBrain)
- Concluído em: 2026-10-21
- O que foi gerado:
  - A1–A8 movidas para `feito.md`; `fazendo.md` limpo; pendências do usuário em `afazer.md`.
  - `api_mapas/README.md` revisado como manual da API: exemplos de corpo 400/404/500 e rastreio por `traceId`; importação no Postman (File > Import, variáveis `baseUrl`/`id`/`idInexistente`/`idInvalido`, Run collection); comandos de teste, cobertura e integração opt-in (`PG_TEST_ID`); manual da base (`public.mapas`: `id`, `graphy`, `metry`, índice e role sugeridos); riscos aceitos da POC.
  - Coleção Postman conferida contra o contrato. Manual de telas: não aplicável (sem frontend).
  - [x] A8

---

## Execuções do usuário em 2026-10-22

Informadas pelo usuário; os agentes não executaram nada no servidor.

- **Ação 1 / RQ-9 - Rotacionar a senha de `casaos` (S-2)**: senha resetada. Os 3 `.env` (`teste_graphy`, `teste_metry`, `api_mapas`) já não existem.
- **A7.3 - API com o banco real**: o usuário testou a `api_mapas` com a senha nova e funcionou.
- **Ação 5 - Habilitar SSL (S-3)**: o usuário executou o script do banco que liga o SSL. Com isso, a exceção `PG_SSLMODE=Disable` deixou de ser necessária e foi retirada do Kanban.
- **F6.6 e M6.6 - Inserção em `graphy` e `metry`**: os programas inseriram os dados com sucesso no banco real. A revalidação de privilégios ligada a essas execuções também saiu de `afazer.md`.
