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

---

## Pedido (2026-10-22): PUT para `/api/mapas/{id}/graphy` e `/api/mapas/{id}/metry` (`api_mapas`)

### P1 - Planejamento por camada
- Responsável: analista (SubBrain)
- Concluído em: 2026-10-22
- O que foi gerado (detalhado em `afazer.md`, seção do pedido):
  - Contrato do PUT, no mesmo padrão do A1:
    - Rotas `PUT /api/mapas/{id}/graphy|metry` e corpo `{"geometrias":[{"wkt","srid"}]}`.
    - Respostas 204, 400 (`Id inválido` / `Corpo inválido` / `Geometria inválida` com índice), 404, 405, 409, 413, 415, 429 e 500 em ProblemDetails com textos fixos.
    - Ordem das verificações e regra do `instance`: modelo da rota no 400/413/415; id validado no 404/409.
  - Decisões D-P1 a D-P10:
    - UPDATE NULL quando a outra coluna está preenchida e DELETE quando ela está nula; INSERT de linhas novas com o id explícito.
    - Lista vazia aceita (limpa a coluna); id sem linhas responde 404.
    - Limites: 100 itens, 10.000 caracteres por WKT, 1 MiB de corpo, `MaxDepth` 8, 10 PUT/min.
    - SRIDs por coluna e tipos 2D aceitos.
    - Concorrência por `pg_advisory_xact_lock` por id, com `lock_timeout` de 5 s (estouro responde 409).
    - `WktValidator` no Domínio da API (port do `teste_metry`).
    - Sucesso responde 204.
    - Conexão `PG_ESCRITA_*` opcional: sem ela o PUT fica desligado e responde 405.
    - Nenhuma autenticação nova; a decisão fica para o SecBrain avaliar.
  - Critérios de aceite CP-1 a CP-12. Subtarefas P2.1–P2.10 (security), P3.1–P3.5 (database), P4.1–P4.7 (backend), P6.1–P6.7 (qa) e P7.1–P7.5 (fechamento).
  - Frontend fora do escopo.
  - [x] P1

### P2 - Revisão de segurança do plano
- Responsável: security (SecBrain)
- Concluído em: 2026-10-22, **APROVADO COM REQUISITOS**
- O que foi gerado:
  - Requisitos obrigatórios RP-1 a RP-17, distribuídos pelo SubBrain nas subtarefas P3.x, P4.x, P6.x e P7.x de `afazer.md`.
  - Recomendações R-1 a R-5. R-1 (desligar a escrita se a role for superusuário, tiver `BYPASSRLS` ou `CREATEROLE`), R-2 (no máximo 3 PUT simultâneos, com 429), R-3 e R-5 foram incluídas. R-4 (CHECK de SRID) continua opcional e fora do escopo.
  - Decisões do usuário registradas como fechadas:
    - U-1: sem autenticação, com as condições do P2.5 no README.
    - U-2: risco do DELETE na tabela inteira aceito, com controles compensatórios e sem `SECURITY DEFINER`.
    - U-3: lista vazia responde 204, com log Warning.
    - U-4: script de backup em `database/`, executado pelo usuário antes de definir `PG_ESCRITA_*`.
  - Decisão do SubBrain D-P11: o estouro do prazo de 30 s (RP-4) responde 500 genérico, igual ao CA-4, ao `57014` e ao pool esgotado.
  - R-3 (P6.5: ids de teste criados com `mapas_teste`; `api_mapas_escrita` sem TRUNCATE, sem sequência e sem INSERT/UPDATE fora das colunas concedidas) e R-5 (P6.3: 499/cancelamento no meio da transação com ROLLBACK confirmado) foram transcritas e encaixadas no P6.
  - [x] P2

### P3 - Role de escrita, backup e SQL de referência
- Responsável: database (DataBrain)
- Concluído em: 2026-10-22. Nada foi executado no servidor; os passos do servidor ficam com o usuário/DBA (P3.5).
- O que foi gerado:
  - [x] P3.1 `database/roles.sql`, revisão 4: role `api_mapas_escrita` (RP-1, RP-3).
    - Criada sem senha, com `LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT CONNECTION LIMIT 5`.
    - Sessão: `RESET ALL`, `statement_timeout` 15s, `lock_timeout` 5s, `idle_in_transaction_session_timeout` 30s, `client_min_messages` warning; `transaction_timeout` 30s só no PG 17+.
    - Privilégios: `CONNECT`, `USAGE` no schema; em `public.mapas`, `SELECT (id, graphy, metry)`, `UPDATE (graphy, metry)`, `INSERT (id, graphy, metry)` e `DELETE`. Nada na sequência.
    - A role entrou nas guardas C2 (membership, configuração residual, `pg_shdepend`).
    - Nova autoconferência 4.5, antes do COMMIT: aborta se os privilégios efetivos de `api_mapas_leitura` e `api_mapas_escrita` (inclusive via PUBLIC) não forem exatamente os esperados.
    - Conferências: 5.4 e 5.5 com a matriz por coluna, 5.5b (leitura sem escrita), 5.6 (sequência USAGE/SELECT/UPDATE), 5.7 a 5.9 com a role nova, 5.10 (colunas), 5.11 (EXECUTE das funções de W1/W2) e 5.12 (advisory locks da classe do PUT).
    - Variável `psql -v pular_ssl=1` para pular a seção 7 (SSL já ligado).
  - [x] P3.2 Guarda 0.1 (RP-2/CP-15): aborta se `public.mapas` tiver colunas além de `id`, `graphy` e `metry`, se faltar alguma ou se os tipos não forem `int4`/`geography`/`geometry`.
  - [x] P3.3 `api_mapas/sql/consultas_referencia.sql`, blocos W1.V/W2.V (validação em lote) e W1.1–W1.6/W2.1–W2.6 (transação), com os predicados exatos e o INSERT via `unnest ... WITH ORDINALITY`.
    - Classe do advisory lock: **1296125011** (0x4D414C53, "MALS" em ASCII; corrigido no D-1 do P6), por id, a mesma para W1 e W2.
  - [x] P3.4 O comentário do bloco histórico de role aponta `database/roles.sql` como fonte oficial das três roles.
  - [x] P3.5 `database/README.md`: seção "Procedimento do DBA para ligar o PUT" (backup → `roles.sql` → `\password` → `pg_hba` `hostssl` `/32` + `reject` → conferência → `PG_ESCRITA_*`). Manual atualizado com a role nova, o risco U-2, a guarda RP-2 e os scripts de backup.
  - [x] P3.6 `database/backup_mapas.sh` (Git Bash/Linux; modo TCP com `sslmode=require` ou modo `--container`) e `database/backup_mapas.ps1` (PowerShell 5.1/7, modo TCP).
    - Nenhuma credencial; a senha vem de `PGPASSWORD`, `.pgpass` ou do prompt.
    - Saída datada fora do OneDrive e do git. Código diferente de zero se o dump falhar, vier vazio ou sem `COPY`/rodapé, e o parcial é removido.
    - O cabeçalho traz a restauração.
- Validação local (container `postgis/postgis:17-3.5` descartável, nunca o servidor):
  - `roles.sql` rodou 2x sem erro e abortou com coluna extra e com GRANT a PUBLIC.
  - W1/W2 com o texto exato deram os contadores esperados de D-P1; id inexistente teve EXISTS = f; o lock ocupado deu `55P03` em cerca de 5 s.
  - Os dois scripts de backup e a restauração funcionaram.
- Pontos levados ao SubBrain: ordem por `ctid` (CP-1/CP-2) e `casaos` como role do backup (detalhes na entrega).

### P4 - Implementação dos PUT na API
- Responsável: backend (BackBrain)
- Concluído em: 2026-10-22. `dotnet build` com 0 erros e 0 avisos; nenhum pacote vulnerável (diretos e transitivos); os 284 testes existentes continuam verdes (3 de integração opt-in ignorados). Testes novos ficam com o QA (P6).
- O que foi gerado:
  - [x] P4.1 Domínio (`ApiMapas.Dominio/Geometrias/`): `WktValidator` (port estático do `teste_metry`, sem log, mensagens fixas só com posição/`U+XXXX`/constantes), `LimitesSrid`/`FaixaSrid`, `PoliticaSridColuna` (graphy 4326; metry 4326/4674/3857/31983), `GeometriaEntrada.TentarCriar` (SRID antes das faixas, RP-14), `MotivosGeometria`, `LimitesEscrita.LimiteGeometriasPut = 100`. Na raiz: `IMapasEscritaRepository` e `ResultadoSubstituicao`.
  - [x] P4.2 Aplicação: `IAtualizacaoGeometriasService`/`AtualizacaoGeometriasService`, `ResultadoAtualizacao` (Sucesso, IdInvalido, TipoMidiaNaoSuportado, CorpoMuitoGrande, CorpoInvalido, GeometriaInvalida, NaoEncontrado, Conflito), `ILeitorCorpo`/`LeituraCorpo`, `OpcoesAtualizacao`, DTOs com `required` e `JsonSerializerOptions` de RP-11. Prazo de 30 s ligado ao `RequestAborted` (`PrazoAtualizacaoEstouradoException` → 500). Logs 4101–4114: auditoria Information, lista vazia Warning, sem WKT/corpo/`JsonException`.
  - [x] P4.3 Infraestrutura: `ConexaoEscritaConfig` (`PG_ESCRITA_*`, pool 5, `ApiMapas.Escrita`, RP-7/RP-8), `FonteDadosEscrita` (pool separado), `MapasEscritaRepository` (W1/W2 copiados literalmente, conferidos por script; `NpgsqlBatch` de validação com índice pelo `BatchCommand`; transação READ COMMITTED; `55P03` → Conflito; contagem do INSERT conferida; ROLLBACK em qualquer falha/cancelamento; `FalhaBancoEscritaException` sem a exceção original), `VerificadorRoleEscrita` (R-1).
  - [x] P4.4 API: `MapPut` nas duas rotas só com a escrita habilitada; `LeitorCorpoHttp` (415 RP-12, 413 por `Content-Length`, contador próprio e `BadHttpRequestException`); ProblemDetails novos em `ProblemasApi`; limitador encadeado (global 60/min + 10 PUT/min por IP + 3 PUT simultâneos), todos 429.
  - [x] P4.5 Configuração: `ApiMapas:LimiteBytesCorpoPut` (1..1048576) e `ApiMapas:LimiteEscritasPorMinuto` (1..60) com código 1 fora da faixa; logs 4006–4010 (habilitada/desligada com motivo/escrita sem autenticação).
  - [x] P4.6 RP-9: `.vscode/launch.json` (configuração nova, `PG_ESCRITA_PASSWORD` como input `password` sem `default`), `.env.example` com placeholders, README da API (seção de configuração da escrita).
  - [x] P4.7 Build 0/0 e auditoria de pacotes.
- Verificação local (container `postgis/postgis:17-3.5` descartável em `127.0.0.1`, removido ao final; teste de fumaça temporário apagado): contagens D-P1 corretas, idempotência, outra coluna preservada, 404 sem gravar, `ST_IsValid` falso e XX000 com o índice certo, `55P03` → Conflito em ~5 s, cancelamento no meio da transação com ROLLBACK, R-1 (role comum = habilitada; superusuário = desligada; banco fora = desligada). Na API em memória: 204/400/404/409/413 (com e sem `Content-Length`)/415/429 (4º PUT simultâneo)/500/405 conforme o contrato.

### P5 - Revisão de segurança da implementação
- Responsável: security (SecBrain)
- Concluído em: 2026-10-22, **APROVADO COM RESSALVAS** (nenhum achado Crítico ou Alto)
- O que foi gerado:
  - Correções C1 a C7 e C9, registradas como P5.C em `afazer.md`:
    - BackBrain: C1 a C6.
    - DataBrain: C7 e C9.
  - C8 (verify-full no backup) foi para a pendência "CA nos clientes para `VerifyFull`".
  - Decisões (7), (8) e (9) vão para o README no P7.3; a cobertura nova vai para o P6.8.
  - [x] P5

### P6 - Testes do PUT (qa / TestBrain)
- Responsável: qa (TestBrain)
- Concluído em: 2026-10-22. `dotnet build` com 0 erros e 0 avisos. `dotnet test`: 1146 aprovados e 19 ignorados (integração opt-in) no modo padrão; 1179 aprovados e 0 ignorados com `PG_RUN_INTEGRATION=1` contra um `postgis/postgis:17-3.5` descartável em `127.0.0.1:55432` (SSL ligado, `roles.sql` aplicado, container e volumes removidos ao final). Nada rodou contra o servidor do usuário.
- Cobertura de linhas (coverlet): 99,43% no total de `src` (1751/1761; ramos 97,64%) e 99,25% no código novo ou alterado do PUT (1327/1337; ramos 96,95%). Com o código gerado pelo `LoggerMessage`: 94,51%. As linhas sem cobertura são guardas defensivas que não têm como ser alcançadas (`MapasEscritaRepository` 167-168, 260-261, 300-303; fechamentos de `await using` no `VerificadorRoleEscrita`).
- O que foi gerado (`api_mapas/tests/ApiMapas.Tests`):
  - [x] P6.1 `Dominio/WktValidatorTests.cs` e `Dominio/GeometriaEntradaTests.cs`: casos do `teste_metry` portados para os 4 SRIDs; faixas inclusivas em todos os níveis de aninhamento; SRID verificado antes do WKT (RP-14); 100/101 itens e 10.000/10.001 caracteres; mensagens sem eco do WKT.
  - [x] P6.2 `Aplicacao/AtualizacaoGeometriasServiceTests.cs`: todos os ramos de `ResultadoAtualizacao`, 44 corpos inválidos de RP-11, ordem do contrato (id antes do corpo e do banco), auditoria 4110 e Warning 4108 sem WKT, cancelamento (4113) e prazo (4114 → `PrazoAtualizacaoEstouradoException`).
  - [x] P6.3 `Api/ApiEscritaTests.cs` (fábrica nova `Suporte/ApiEscritaFactory.cs`): 204, 400 (id, corpo, geometria), 404, 409, 413 (com e sem `Content-Length`, limite exato e 1 MiB), 18 variações de 415, 405 por cada motivo de D-P9, 429 de taxa e de concorrência (sem `Retry-After`), 500 genérico e por prazo, `instance`/`traceId`/`type`, nosniff/no-store, RP-12 (Host, OPTIONS com Origin, POST, PATCH, DELETE e method override). Pilha completa com o repositório real sobre o protocolo falso, incluindo o R-5 (cliente desconecta no meio da transação → ROLLBACK, sem COMMIT).
  - [x] P6.4 `Infraestrutura/MapasEscritaRepositoryTests.cs`, `Infraestrutura/ConexaoEscritaConfigTests.cs` e o mock `Suporte/ServidorPostgresEscritaFalso.cs` (protocolo v3 com lote, transação I/T/E, CancelRequest e queda de conexão): W1/W2 idênticos a `consultas_referencia.sql`, ordem exata da transação, OIDs dos parâmetros, `55P03` → Conflito, `57014`/permissão/pool esgotado/banco fora → 500 com ROLLBACK e log só com SqlState, checagem R-1 com os 6 critérios (C4) e falhas genéricas (C2).
  - [x] P6.5 `Integracao/IntegracaoEscritaTests.cs` (opt-in; `PG_ESCRITA_*` e `PG_TESTE_*` novos em `AmbienteOriginal`): CP-1 a CP-5, CP-7, CP-9 (409 em ~5 s; dois PUTs simultâneos nunca se misturam), CP-2 comparado como conjunto, faixa do graphy (o cast para geography não falha com longitude 200; a API recusa antes), CP-14/R-3 (matriz por coluna, sem TRUNCATE, sem privilégio na sequência, sem UPDATE de `id`, leitura sem escrita, tentativas sempre dentro de BEGIN/ROLLBACK) e RP-3/C9 (`SHOW` das configurações). Ids criados e apagados com `mapas_teste`. CP-15 conferido no container: o `roles.sql` aborta com coluna extra, com tipo trocado e com coluna faltando, sem criar role.
  - [x] P6.6 Postman CLI 1.75.0 contra a API local: a coleção oficial (GET) teve 126/126 asserções aprovadas. Como a coleção ainda não tem PUT (entra no P7.2), rodei uma coleção de verificação só no scratchpad (16 requisições, 42/42 asserções, 0 erros de script).
  - [x] P6.7 Cobertura acima de 80% (ver acima).
  - [x] P6.8 `Api/LeitorCorpoHttpTests.cs`: C1 (`Application/JSON`, `APPLICATION/JSON; CHARSET=UTF-8`), C2/C4 (seção acima), C3 (pool de escrita limpo quando a R-1 desliga), C5 em Kestrel real (chunked malformado → 400 "Corpo inválido", corpo lento → 400 com StatusCode 408 no log, cliente que fecha → nunca 500), C6 (bloco do `ArrayPool` devolvido limpo), COMMIT com `CancellationToken.None` e cancelamento antes do COMMIT → ROLLBACK.
- Ajustes na camada de testes: `Suporte/ApiFactory.cs` (trava `TravaAmbiente` na montagem do host, porque as variáveis `PG_*` são do processo) e correção de um falso positivo intermitente em `ApiEmMemoriaTests` (o `traceId` hexadecimal podia conter "abc").
- Encaminhado ao SubBrain:
  - D-1 (Baixa, documentação; backend + database): `MapasEscritaRepository.cs:22`, `consultas_referencia.sql:212` e `roles.sql:813-814` dizem "1296125011 = 0x4D415053 = MAPS". O correto é 1296125011 = 0x4D414C53 ("MALS"); "MAPS" = 0x4D415053 = 1296126035. O valor usado é o mesmo em todos os lugares, então não há impacto funcional.
  - O-1 (Baixa, defesa em profundidade; backend): `Program.cs:200-205` não protege a chamada da R-1. Se a resolução ou a implementação de `IVerificadorRoleEscrita` lançar exceção, a API não sobe. A implementação atual captura tudo (C2 cumprido), então hoje esse caminho não é alcançável.
  - O-2 (Informativo): no .NET 10 o `ExceptionHandlerMiddleware` responde 499 sozinho quando a requisição é abortada e não chama o `ManipuladorExcecoes`, por isso o log 3001 não aparece. O cancelamento fica registrado pelos logs 4113 e 2112. O resultado final está correto.
  - O-3 (Informativo): um erro XX000 dentro da transação faz o Npgsql descartar a conexão. O ROLLBACK não é enviado (fica o log 2114) e o servidor desfaz ao fechar a sessão. É o comportamento previsto pelo código.
  - Notas para o P7.2: com mais de 10 PUT numa execução, o 11º recebe 429 (limite de 10 PUT/min por IP). Use no máximo 10 PUT por execução ou `--delay-request 7000`. Deixe a lista vazia por último e use um `idEscrita` separado do `id` do GET.

### P5.C - Correções do parecer P5
- Responsáveis: backend (BackBrain) e database (DataBrain)
- Concluído em: 2026-10-22
- O que foi gerado:
  - [x] C1 (BackBrain): Content-Type comparado com `OrdinalIgnoreCase` em `LeitorCorpoHttp`; o resto do RP-12 continua estrito.
  - [x] C2 (BackBrain): `VerificadorRoleEscrita` captura qualquer exceção, registra só o tipo e o SqlState e devolve `VerificacaoFalhou`.
  - [x] C3 (BackBrain): com a escrita desligada pela R-1, o pool de escrita é limpo.
  - [x] C4 (BackBrain): a R-1 também desliga a escrita com `TRUNCATE` em `public.mapas`, membro de `pg_write_all_data` ou role dona da tabela (`relowner`).
  - [x] C5 (BackBrain): `BadHttpRequestException` que não é de tamanho responde 400 `Corpo inválido`, sem logar a exceção.
  - [x] C6 (BackBrain): `ArrayPool.Return` com `clearArray: true`.
  - [x] C7 (DataBrain): `backup_mapas.sh`/`.ps1` só restringem a permissão de diretório criado pelo próprio script, ou vazio e chamado `backups_postgis`; nos outros casos, abortam.
  - [x] C9 (DataBrain): `ALTER ROLE api_mapas_escrita SET search_path = public`, com guarda (4.5) e conferência (5.1).
  - C8 (verify-full no backup): não executado; está na pendência "CA nos clientes para `VerifyFull`" em `afazer.md`.

### P5.1 - Revisão das correções P5.C (opcional)
- Responsável: security (SecBrain)
- **Dispensado** em 2026-10-22 pelo coordenador e não executado: todos os achados do P5 eram de severidade Baixa e foram verificados pelos testes do P6 (P6.8).

### Correções dos defeitos do P6
- Concluído em: 2026-10-22
  - [x] D-1 (BackBrain e DataBrain, documentação): o comentário da classe do lock agora diz `1296125011 = 0x4D414C53 = "MALS"` em `MapasEscritaRepository.cs`, `consultas_referencia.sql` e `roles.sql`, sem mudança de valor. O SubBrain corrigiu a menção no registro do P3 deste arquivo.
  - [x] O-1 (BackBrain): a chamada da R-1 em `Program.cs` ganhou try/catch e o log 4011 (Warning, escrita desligada). O TestBrain acrescentou o teste em `tests/`.
  - O-2 e O-3 (informativas, sem ação): no .NET 10 o 499 é tratado pelo `ExceptionHandlerMiddleware`; um XX000 descarta a conexão e o servidor faz o rollback.

### P7 - Fechamento
- Responsável: analista (SubBrain)
- Concluído em: 2026-10-23
- O que foi gerado:
  - [x] P7.1 P3, P4, P5, P5.C e P6 em `feito.md`; P5.1 registrado como dispensado; a seção do pedido saiu de `afazer.md` e `fazendo.md` está vazio. O contrato e as decisões ficaram abaixo, como referência.
  - [x] P7.2 `api_mapas/postman/api_mapas.postman_collection.json`:
    - Pasta nova `escrita (PUT)` com 15 requisições, sendo 10 PUT (o limite por minuto):
      - 204 em graphy e em metry, este com `APPLICATION/JSON; CHARSET=UTF-8`;
      - GETs de conferência comparados como conjunto;
      - 400 de id, de corpo e de geometria (faixa local e `ST_IsValid`), 404, 413, 415 e POST 405 com method override;
      - a lista vazia por último, seguida dos GETs que provam a coluna limpa e a outra coluna preservada.
    - Variável nova `idEscrita`, vazia por padrão. A pasta é pulada (`pm.execution.skipRequest`) se `idEscrita` estiver vazio ou igual a `id`.
    - Teste da pasta: `problem+json` com `traceId` e sem vazamento.
    - Exemplos fictícios salvos; sem credenciais.
    - Validada com o Postman CLI 1.75.0 contra uma API local ligada a um `postgis/postgis:17-3.5` descartável em `127.0.0.1` (SSL, `roles.sql` aplicado, senhas aleatórias descartadas, container removido ao final): 27 requisições e 195 asserções, 0 falhas. Com `idEscrita` vazio, só os 12 GET rodaram e nada foi gravado. Nada rodou contra o servidor do usuário.
  - [x] P7.3 `api_mapas/README.md`:
    - Tabela de rotas com o PUT e um aviso no início com as condições do U-1.
    - Backup como pré-requisito, com a pasta datada `~/backups_postgis/AAAAMMDD_HHMMSS` e a regra de destino dedicado (C7).
    - Critérios da R-1, incluindo os três da C4, e a decisão (7) sobre a subida de até 15 s.
    - Seção "Contrato do PUT": requisição, regra UPDATE/DELETE/INSERT, lista vazia, 404 de PUT repetido com lista vazia, ordem por `ctid` não garantida depois de um PUT, concorrência, ordem das verificações, tabela de respostas, decisões (8) e (9), prazo e logs.
    - Também atualizados: `curl` do PUT, Postman (`idEscrita`, 10 PUT, `--delay-request 7000`), resultados de testes, manual da base e riscos aceitos (U-1, U-2). O aviso antigo de "SSL desligado" foi trocado pela situação de 2026-10-22.
  - [x] P7.4 `database/README.md` revisado depois da atualização do DataBrain:
    - linha do `id`: o PUT insere com o id explícito, só para ids existentes;
    - critérios da R-1 com a C4.
    - O resto (role nova, U-2, RP-2, scripts de backup, procedimento do DBA) estava de acordo com o entregue.
  - [x] P7.5 Passos de servidor para ligar o PUT registrados em "Ações do usuário/DBA" de `afazer.md`, na ordem backup → `roles.sql` → `\password` → `pg_hba` → conferência → `PG_ESCRITA_*`.
  - Manual de telas: não aplicável (sem frontend).

### Contrato do PUT e decisões (referência, implementados)

> Pedido do usuário: "Crie os métodos PUT para os endpoints graphy e metry". Features abertas pelo MegaBrain.

**Decisões do usuário (fechadas, não reabrir):**
1. **Semântica**: o PUT substitui tudo do id na coluna. Numa única transação, tira a coluna (`graphy` ou `metry`) das linhas existentes do id e grava a lista nova. O PUT é idempotente.
2. **Corpo**: WKT + SRID por geometria, ex.: `{"geometrias":[{"wkt":"POINT(-46.63 -23.55)","srid":4326}]}`. As regras de validação de `teste_graphy`/`teste_metry` são reaproveitadas.
3. **Banco**: nova role `api_mapas_escrita` com o mínimo de privilégios em `public.mapas`; conexão separada para o PUT; `api_mapas_leitura` continua só leitura.

**Decisões do usuário sobre o parecer P2 (2026-10-22, fechadas, não reabrir):**
- **U-1 - Sem autenticação no PUT.** Valem as condições do P2.5, que precisam constar no README:
  - a máquina tem um único usuário;
  - a API nunca é exposta por proxy, túnel ou port-forward;
  - RP-12, RP-13 e RP-16 estão cumpridos.
- **U-2 - Opção (a): o risco do `DELETE` valer para a tabela inteira foi aceito.** Os controles compensatórios são o `pg_hba` `hostssl` `/32`, o backup (U-4) e o `CONNECTION LIMIT`. Não haverá função `SECURITY DEFINER`.
- **U-3 - Lista vazia aceita:** responde 204 e limpa a coluna (D-P2 mantido), com log **Warning** (RP-13).
- **U-4 - Backup antes de ligar o PUT.** O DataBrain escreve um script de backup pronto em `database/` (`pg_dump -t public.mapas`), sem nenhuma credencial no script; a senha vem de `PGPASSWORD`/`.pgpass` ou do prompt. Quem executa é o usuário, **antes** de definir `PG_ESCRITA_*`. Vira pré-requisito no P3 e no README.

**Ordem executada:** P1 analista → P2 security → P3 database → P4 backend → P5 security → P5.C correções → P6 qa → P7 analista. Frontend: fora do escopo (a `api_mapas` não tem frontend).

#### Decisões do SubBrain (D-P1 a D-P11)

- **D-P1 - Como tirar a coluna antiga: UPDATE para NULL ou DELETE, conforme a outra coluna.** Para o PUT de `graphy` (o de `metry` é simétrico):
  - Linhas do id com `graphy` e `metry` preenchidas: `UPDATE ... SET graphy = NULL` (a `metry` dessa linha é preservada).
  - Linhas do id com `graphy` preenchida e `metry` nula: `DELETE` (se fossem só zeradas, virariam linhas vazias que se acumulam a cada PUT).
  - Linhas do id com `graphy` nula não são tocadas, inclusive as que já estavam vazias antes.
  - As geometrias novas entram como linhas novas: `INSERT (id, graphy)` com o id explícito. A coluna é `GENERATED BY DEFAULT`, então o id explícito é aceito e a sequência não é usada.
  - A tabela só pode ter `id`, `graphy` e `metry`. Isso agora é uma guarda que aborta o script (RP-2).
- **D-P2 - Lista vazia (`"geometrias": []`) é aceita e responde 204: limpa a coluna do id** (U-3, com log Warning). Já o corpo sem `geometrias`, com `geometrias: null` ou com raiz que não é objeto responde 400.
- **D-P3 - Id sem nenhuma linha em `public.mapas` responde 404; o PUT não cria ids.**
  - Motivo: se um id que a sequência ainda não gerou fosse gravado com valor explícito, um INSERT futuro dos programas de teste receberia o mesmo id e misturaria linhas de origens diferentes (a tabela não tem PK).
  - Vale qualquer linha do id, em qualquer coluna, inclusive linha vazia. A existência é verificada depois do lock (RP-15).
  - Idempotência: se um PUT com lista vazia apagar a última linha do id, o próximo PUT igual responde 404. O estado do servidor é o mesmo, que é o que a idempotência do RFC 9110 exige; isso fica documentado no manual.
- **D-P4 - Limites:**
  - Até **100** geometrias por PUT, igual ao `OpcoesConsulta.LimiteItens`. Depois do PUT, o GET devolve tudo sem truncar.
  - WKT com até **10.000** caracteres, como nos programas de teste.
  - Corpo com até **1 MiB** (1.048.576 bytes), configurável em `ApiMapas:LimiteBytesCorpoPut`. Fora de 1..1048576, a API sai com código 1 (RP-6).
  - JSON com as opções de RP-11.
  - Rate limit de escrita de **10 PUT/min por IP**, configurável em `ApiMapas:LimiteEscritasPorMinuto`; fora de 1..60, a API sai com código 1 (RP-6). Ele se soma ao limite global de 60/min.
  - No máximo **3 PUT simultâneos** na API toda. O excedente responde 429 (R-2).
  - Prazo total de **30 s** por PUT (RP-4, ver D-P11).
- **D-P5 - SRIDs e tipos por coluna:**
  - `graphy`: só **4326**, com longitude em [-180, 180] e latitude em [-90, 90].
  - `metry`: **4326, 4674, 3857 e 31983**, com as faixas de `teste_metry/.../LimitesSrid.cs`.
  - O SRID é verificado antes das faixas (RP-14).
  - O `srid` é obrigatório em todo item, sem valor padrão, e é um inteiro JSON (`NumberHandling Strict`); `"4326"` em texto responde 400.
  - Tipos aceitos: POINT, LINESTRING, POLYGON, MULTIPOINT, MULTILINESTRING, MULTIPOLYGON e GEOMETRYCOLLECTION. Só 2D, sem EMPTY, sem EWKT e sem HEX/WKB.
  - Nas duas colunas vale a lista permitida de caracteres do validador do `teste_metry`. Depois, no banco, `ST_IsValid` e `GeometryType` precisam bater com o tipo local.
- **D-P6 - Concorrência:**
  - Transação `READ COMMITTED`. Logo depois do `BEGIN` vem `SET LOCAL lock_timeout = '5s'` (RP-3) e em seguida `SELECT pg_advisory_xact_lock(<classe>::int4, @id::int4)`.
  - A classe é uma constante incomum e documentada (RP-15). O lock é por id, porque os PUTs de `graphy` e de `metry` mexem nas mesmas linhas.
  - `55P03` responde 409.
  - A ordem na transação é: `BEGIN` → `SET LOCAL lock_timeout` → lock → existência do id (404 → ROLLBACK) → UPDATE NULL → DELETE → INSERT (`unnest(@wkts::text[], @srids::int[]) WITH ORDINALITY`, na ordem do corpo) → COMMIT.
  - A validação no banco (`ST_IsValid`/`GeometryType`) roda antes de abrir a transação, na mesma conexão de escrita. O recomendado é um único `NpgsqlBatch` com um comando por item (RP-4). Um erro do PostGIS vira 400 com o índice do item.
  - O GET não usa o lock e enxerga o estado de antes ou de depois do COMMIT.
- **D-P7 - Onde fica o `WktValidator`:** em `ApiMapas.Dominio/Geometrias/`.
  - É uma cópia adaptada do validador do `teste_metry` mais `LimitesSrid`/`FaixaSrid`, sem log e com mensagens fixas (RP-14).
  - O projeto de domínio não referencia `teste_graphy`/`teste_metry`; o comentário registra a origem.
  - Junto com ele: o value object `GeometriaEntrada` (`TentarCriar`) e a política de SRID por coluna.
- **D-P8 - Sucesso responde `204 No Content`, sem corpo.** Para conferir o resultado, o cliente usa o GET.
- **D-P9 - Segunda conexão e quando a escrita fica desligada:**
  - Variáveis `PG_ESCRITA_USER` e `PG_ESCRITA_PASSWORD`. Host, porta, base e `PG_SSLMODE` são os mesmos da leitura.
  - Pool próprio: `Maximum Pool Size = 5`, `ApplicationName = ApiMapas.Escrita`, `CommandTimeout` de 15 s, `PersistSecurityInfo=false`, `IncludeErrorDetail=false`, sem `TrustServerCertificate` (RP-8).
  - A escrita fica **desligada** (PUT não mapeado, responde 405, com log Warning no início) quando:
    - as duas variáveis estão ausentes;
    - `PG_SSLMODE` é `Disable` ou `Prefer` (RP-7);
    - a role conectada é superusuário, tem `BYPASSRLS` ou tem `CREATEROLE` (R-1). A checagem é feita uma vez na subida, consultando `pg_roles` para o `current_user`. Se o banco não responder na subida, a escrita fica desligada.
  - A API **sai com código 1** (a mensagem cita só o nome da variável) quando:
    - só uma das duas variáveis está definida;
    - `PG_ESCRITA_USER` é igual a `PG_USER` (comparação depois do `Trim`, sem diferenciar maiúsculas; RP-8);
    - `PG_ESCRITA_PASSWORD` é igual a `PG_PASSWORD` (RP-8).
- **D-P10 - Sem autenticação** (U-1). Proteções:
  - a API só ouve em loopback (RA-5) e o `AllowedHosts` continua restrito;
  - o Content-Type JSON é obrigatório (RP-12);
  - não há CORS, method override nem POST nas rotas;
  - o log de início avisa que há escrita sem autenticação (RP-13).
- **D-P11 - Estouro do prazo de 30 s (RP-4) responde 500 genérico, e não 503.**
  - Fica igual ao tratamento já existente para timeout e falha de banco (CA-4) e ao `57014`/pool esgotado (RP-10), e não introduz `Retry-After` nem um novo texto.
  - O prazo usa um `CancellationTokenSource` ligado ao `RequestAborted`, com ROLLBACK.
  - Quando o `RequestAborted` também está cancelado, o caso é cliente desconectado e continua 499 sem corpo (`ManipuladorExcecoes`).

#### Requisitos do parecer P2 (RP-1 a RP-17, obrigatórios; R-1, R-2, R-3 e R-5 incluídos; R-4 fora do escopo)
- Distribuição por camada:
  - Database: RP-1, RP-2, RP-3 (role), RP-15 (SQL), RP-17, U-4.
  - Backend: RP-3 (`SET LOCAL`) e RP-4 a RP-15, R-1, R-2.
  - QA: RP-12, R-3 (P6.5), R-5 (P6.3) e os testes de cada RP.
  - Fechamento: RP-9 (Postman), U-1 e U-4 (README).
- **R-3** (QA, P6.5), texto do SecBrain: "os testes de integração criam os ids com mapas_teste, nunca com casaos. O P6.5 confirma que api_mapas_escrita não consegue TRUNCATE, não tem privilégio na sequência e não tem INSERT nem UPDATE em colunas além das concedidas."
- **R-5** (QA, P6.3), texto do SecBrain: "a cobertura do P6.3 inclui o 499/cancelamento no meio da transação, com o ROLLBACK confirmado (CP-7)."
- **R-4** (CHECK de SRID): continua opcional, fora deste pedido (ver "Opcional" nas ações do DBA).

#### Contrato do PUT

| Rota | Coluna gravada | SRIDs aceitos | Sucesso |
|------|----------------|---------------|---------|
| `PUT /api/mapas/{id}/graphy` | `public.mapas.graphy` (`ST_GeomFromText(@wkt, @srid)::geography`) | 4326 | 204 |
| `PUT /api/mapas/{id}/metry`  | `public.mapas.metry` (`ST_GeomFromText(@wkt, @srid)`) | 4326, 4674, 3857, 31983 | 204 |

- `{id}`: mesma regra do GET (sem constraint `:int`, validado por `IdMapa`, 1..2147483647). Constantes de rota reaproveitadas (`RotaGraphy`/`RotaMetry`), com `MapPut`. Sem POST nas rotas e sem method override (RP-12).
- `Content-Type` (RP-12): só `application/json` exato. O parâmetro `charset` é opcional e, se vier, só `utf-8`, sem diferenciar maiúsculas. Qualquer outro valor responde 415.
- Corpo:
```json
{
  "geometrias": [
    { "wkt": "POINT(-46.633309 -23.55052)", "srid": 4326 },
    { "wkt": "LINESTRING(-46.64 -23.56, -46.62 -23.54)", "srid": 4326 }
  ]
}
```
- Opções do JSON (RP-11):
  - os nomes de propriedade são exatamente `geometrias`, `wkt` e `srid`, sensíveis a maiúsculas;
  - todos os campos são `required`;
  - `MaxDepth` 8, `UnmappedMemberHandling Disallow`, `AllowDuplicateProperties false` e `NumberHandling Strict`.
- O índice dos itens começa em 0 e a ordem do corpo é a ordem do INSERT.
- Ordem das verificações, a primeira falha responde:
  1. Limitador de concorrência e de taxa (429).
  2. Id (400 `Id inválido`, sem ler o corpo e sem banco).
  3. Content-Type (415).
  4. Tamanho: `Content-Length` acima do limite, ou leitura com contador próprio que passa do limite (413). A `BadHttpRequestException` de tamanho também vira 413. Não desligar `MinRequestBodyDataRate` (RP-5).
  5. JSON (400 `Corpo inválido`).
  6. Itens (400 `Geometria inválida`).
  7. Banco: validação em lote (400 `Geometria inválida`), lock (409), existência (404), gravação (204/500).
- O corpo é lido pelo handler (não pelo binding automático do minimal API), para que todos os erros saiam no formato ProblemDetails abaixo.

Respostas (`application/problem+json`, mesmos `type`/`traceId`, nada ecoado do corpo, sem SQL/SqlState/stack/host/usuário/`JsonException`):

| Status | `title` | `detail` (texto fixo) | `instance` |
|--------|---------|-----------------------|------------|
| 204 | (sem corpo) | | |
| 400 | `Id inválido` | `O id deve ser um inteiro entre 1 e 2147483647.` | modelo da rota (`/api/mapas/{id}/graphy`) |
| 400 | `Corpo inválido` | `O corpo deve ser um JSON no formato {"geometrias":[{"wkt":"...","srid":4326}]}.` | modelo da rota |
| 400 | `Geometria inválida` | `geometrias[{i}]: {motivo}`, com `{motivo}` vindo da lista fixa do validador (posição e `U+XXXX`, nunca o WKT), ou `quantidade acima do máximo de 100 geometrias`, `srid não permitido para a coluna '{coluna}' (aceitos: ...)`, `geometria inválida segundo o PostGIS (ST_IsValid)`, `tipo devolvido pelo banco diverge do WKT` | modelo da rota |
| 404 | `Mapa não encontrado` | `Nenhuma linha encontrada em public.mapas para o id {id}.` (id validado) | `/api/mapas/{id}/{coluna}` com o id validado |
| 405 | (padrão ASP.NET) | escrita desligada (D-P9) ou método não mapeado | omitido |
| 409 | `Atualização concorrente` | `Outra atualização do id {id} está em andamento. Tente novamente.` | `/api/mapas/{id}/{coluna}` com o id validado |
| 413 | `Corpo muito grande` | `O corpo excede o limite de {limite} bytes.` | modelo da rota |
| 415 | `Tipo de mídia não suportado` | `Use Content-Type: application/json (charset utf-8 opcional).` | modelo da rota |
| 429 | `Muitas requisições` (atual) | texto atual; vale também para o limite de escrita (10/min) e para os 3 PUT simultâneos | omitido |
| 500 | `Erro interno` (atual) | texto atual; inclui o prazo de 30 s, `57014` e pool esgotado (D-P11/RP-10) | omitido |

- `type`: RFC 9110 15.5.1 (400), 15.5.5 (404), 15.5.10 (409), 15.5.14 (413), 15.5.16 (415).
- Logs (RP-10/RP-13):
  - **Auditoria Information por PUT** concluído: id, coluna, quantidade, linhas atualizadas, apagadas e inseridas, `traceId`.
  - Lista vazia: **Warning**.
  - Falhas: só SqlState, id, coluna e índice. Nunca `MessageText`, `Hint`, `Detail`, `Where`, `ToString` da exceção, WKT, corpo ou `JsonException`.

##### Critérios de aceite (CP-1 a CP-16)
- CP-1: um PUT válido em um id existente responde 204. O GET seguinte devolve exatamente as geometrias enviadas, na ordem, e as linhas antigas da coluna não aparecem mais.
- CP-2: a outra coluna do mesmo id não muda: linhas com as duas colunas ficam com a outra coluna intacta, e o GET da outra coluna devolve o mesmo resultado de antes.
- CP-3: repetir o mesmo PUT gera o mesmo estado (mesmas linhas e contagens por coluna para o id).
- CP-4: `geometrias: []` responde 204, o GET da coluna responde 404 e há um log Warning. Linhas que só tinham essa coluna são apagadas; as que também tinham a outra coluna são preservadas.
- CP-5: id sem linhas responde 404 e nada é inserido.
- CP-6: id inválido responde 400 sem ler o corpo. JSON malformado, raiz errada, campo faltando, campo extra, campo duplicado, nome com outra caixa ou `srid` em texto responde 400 `Corpo inválido`. WKT/SRID/tipo/limites inválidos respondem 400 `Geometria inválida` com o índice. Em todos esses casos nada é gravado.
- CP-7: falha em qualquer etapa do banco (item inválido no PostGIS, erro no INSERT, cancelamento, prazo de 30 s) faz ROLLBACK e deixa o estado anterior intacto.
- CP-8: 101 geometrias, WKT com 10.001 caracteres ou corpo acima de 1 MiB respondem 400/400/413, inclusive sem `Content-Length`. `text/plain`, `application/json; charset=latin1` e `application/x-www-form-urlencoded` respondem 415.
- CP-9: dois PUTs simultâneos no mesmo id terminam com um estado igual ao de um deles, nunca misturado. Espera acima de 5 s responde 409. Um 4º PUT simultâneo responde 429.
- CP-10: o SQL é constante por coluna (W1/W2, por `switch`), parametrizado (`@id` Integer, `@wkt` Text, `@srid` Integer, arrays `Text[]`/`Integer[]`) e com os predicados exatos do UPDATE/DELETE. O PUT usa só a conexão de escrita e o GET só a de leitura.
- CP-11: configuração:
  - sem `PG_ESCRITA_*`, com `PG_SSLMODE` `Disable`/`Prefer` ou com role superusuário/`BYPASSRLS`/`CREATEROLE`, a API sobe e o PUT responde 405 com log Warning;
  - só uma das variáveis definida, usuário igual ao de leitura (com caixa e espaços diferentes), senha igual à de leitura, ou `LimiteBytesCorpoPut`/`LimiteEscritasPorMinuto` fora da faixa, fazem a API sair com código 1.
- CP-12: `Host` malicioso responde 400. `OPTIONS` com `Origin` não devolve cabeçalhos CORS. `POST` e `X-HTTP-Method-Override` nas rotas não gravam nada (RP-12).
- CP-13: logs de auditoria e de falha conforme RP-10/RP-13, sem WKT, corpo, `MessageText` ou credenciais.
- CP-14: a role `api_mapas_escrita` tem exatamente os privilégios de RP-1; `api_mapas_leitura` não tem nenhuma escrita.
- CP-15: `roles.sql` aborta se `public.mapas` tiver colunas além de `id`, `graphy` e `metry` (RP-2).
- CP-16: cobertura de linhas de pelo menos 80% (meta: manter o patamar atual, cerca de 99%).

## Pedido (2026-10-23): POST para `/api/mapas/{id}/graphy` e `/api/mapas/{id}/metry` (`api_mapas`)

### PO1 - Planejamento por camada (contrato do POST, decisões, critérios de aceite)
- Responsável: analista (SubBrain)
- Concluído em: 2026-10-23
- O que foi gerado (plano SUPERADO pelo PO1-R abaixo, depois da mudança do modelo de dados pelo usuário):
  - Decisões D-PO1 a D-PO10: transação só com INSERT (W3/W4) sob o mesmo advisory lock 1296125011 do PUT; id inexistente aceito com log Warning não bloqueante quando o id não tinha linhas e é maior que o `max(id)` da tabela (sem privilégio na sequência; uso do `last_value` fica para decisão do SecBrain); 201 sem corpo com `Location` normalizado; lista vazia 400 `Geometria inválida` ("quantidade abaixo do mínimo de 1 geometria"); 409 com os textos do PUT; limitadores, limites, prazo e pool compartilhados com o PUT; nenhuma mudança em `roles.sql`; liga/desliga do D-P9 com o log "Escrita (PUT e POST)"; RP-12 revista (POST só nas duas rotas, method override ignorado, Content-Type estrito).
  - Contrato do POST (rotas, ordem das verificações, diferenças de resposta em relação ao PUT, logs) e critérios de aceite CPO-1 a CPO-14.
  - Subtarefas PO2.1–PO2.6 (security), PO3.1–PO3.3 (database), PO4.1–PO4.5 (backend), PO6.1–PO6.5 (qa) e PO7.1–PO7.4 (fechamento), incluindo a revisão de `ApiEscritaTests.PostPatchDeleteEMethodOverride_405_NaoGravam`, do CP-12 e da requisição Postman "graphy - POST nao mapeado (405, sem method override)".

### PO2 - Revisão de segurança do plano do PO1
- Responsável: security (SecBrain)
- Concluído em: 2026-10-23. Parecer: aprovado com ajustes. Os itens que continuam valendo com o modelo novo viraram RS-1 a RS-7 no plano do PO1-R (`afazer.md`, D-N8):
  - RS-1: Content-Type estrito com 415 pelo `LeitorCorpoHttp`, sem binding automático.
  - RS-2: middleware 403 antes do rate limiter quando vier `Origin` ou `Sec-Fetch-Site` diferente de `none`/`same-origin`, só nos endpoints de escrita.
  - RS-3: `UseHttpMethodOverride` proibido.
  - RS-4: logs com o método e EventIds próprios; 4006 a 4010 passam a dizer "PUT e POST"; 201 via `TypedResults.Created`, sem corpo, com `Location` relativo montado a partir do int.
  - RS-5: documentar o crescimento da tabela e do disco e o retry sem idempotência.
  - RS-6: testes de 415 (`text/plain`, form-urlencoded, multipart, sem Content-Type, lista com vírgula, Content-Type duplicado, latin1).
  - RS-7: o Postman e a integração não criam ids fora do controle.
- PO2.2 (aviso de colisão): o usuário escolheu `last_value`, depois superado pela PK (D-N2 do PO1-R).

### PO1-R - Replanejamento com `public.mapas.id` como PRIMARY KEY
- Responsável: analista (SubBrain)
- Concluído em: 2026-10-23
- O que foi gerado (plano em `tarefas/afazer.md`, "Plano do PO1-R", para o SecBrain revisar no PO2b):
  - Perguntas ao usuário Q1 a Q6, cada uma com recomendação: corpo de uma geometria, formato do GET, PUT preenchendo a outra coluna, PUT sem limpar nem apagar (revoga o `DELETE`), `GENERATED ALWAYS`, apoio à deduplicação.
  - Decisões D-N1 a D-N12:
    - o `last_value` perde o sentido; o risco residual é o `23505`, que responde 500;
    - migração com parada em duplicado e `setval` que nunca volta;
    - `roles.sql` sem `USAGE` na sequência (a identity não verifica privilégio), `INSERT` sem `id` e guarda de PK;
    - contratos novos de POST (`/api/mapas/graphy|metry`, 201 + `Location`), PUT (UPDATE pela PK, sem advisory lock) e GET (uma geometria);
    - RS-1 a RS-7; escrita desligada sem PK;
    - `teste_graphy`/`teste_metry` sem mudança de código;
    - Postman e integração com ids capturados do `Location`.
  - Critérios CN-1 a CN-15 e subtarefas PO2b, PO3, PU-DBA, PO4, PO5, PO6 e PO7.

### PU - Respostas do usuário às perguntas Q1 a Q6 do PO1-R
- Responsável: usuário
- Concluído em: 2026-10-23. O usuário aceitou todas as recomendações:
  - Q1: corpo de objeto único `{"wkt","srid"}`.
  - Q2: GET com objeto único.
  - Q3: o PUT pode preencher a outra coluna.
  - Q4: o PUT só faz UPDATE e o `DELETE` da `api_mapas_escrita` é revogado (U-2 encerrado).
  - Q5: `GENERATED ALWAYS`.
  - Q6: relatório somente leitura e modelo comentado de mescla.
- As respostas estão registradas como a decisão 6 do pedido em `afazer.md`, e as alternativas que não foram escolhidas saíram do plano.

### PO2b - Revisão de segurança do replanejamento (PO1-R)
- Responsável: security (SecBrain)
- Concluído em: 2026-10-23. Parecer: aprovado com requisitos. No plano em `afazer.md`, os achados viraram RS-8 a RS-26 e foram incorporados ao D-N3, D-N4, D-N5, D-N6, D-N7, D-N9, D-N10, D-N12, aos critérios CN-7 a CN-10 e CN-13, e às tarefas PO3, PU-DBA, PO4, PO6, PO7 e PU-R.
  1. Identity sem `USAGE`: confirmado; o PO3.1 prova isso nos modos `BY DEFAULT` e `ALWAYS` (RS-8).
  2. Q5: a guarda C3 passa a exigir `attidentity = 'a'`, e a matriz espera INSERT em `id` = false (RS-9).
  3. Migração numa única transação, nesta ordem: `lock_timeout` 5s e `statement_timeout` 5min → `LOCK ACCESS EXCLUSIVE` → guardas → contagem → NULL/duplicados com `RAISE` → PK → `setval` → `GENERATED ALWAYS` → `DROP INDEX` → conferência → `COMMIT`. Execução com `ON_ERROR_STOP` e `-1` (RS-10).
  4. PU-DBA: primeiro desligar a escrita antiga (`PG_ESCRITA_*` e/ou `NOLOGIN` + `pg_terminate_backend`); segundo backup se houver intervalo grande (RS-11).
  5. `setval` via `pg_get_serial_sequence` com `GREATEST(max(id), last_value)`, nunca para trás; `RAISE` se `max(id) > 2147483647 - 1000000`; conferência informativa da sequência na seção 5 (RS-12).
  6. Idempotência passo a passo: só vale a PK exata em `id` (`contype = 'p'`, `conkey`, `indisvalid`); outra PK ou UNIQUE aborta; `DROP INDEX IF EXISTS` só se o índice for de `mapas(id)` (RS-14).
  7. Relatório em `READ ONLY`, sem geometria por padrão e com a saída fora do repositório e do OneDrive. Mescla numa transação com `LOCK EXCLUSIVE`, reclassificação dentro da transação, UPDATE/DELETE por `ctid` com `rowcount = 1` senão `RAISE`, sem tocar nos conflitantes (RS-24).
  8. `roles.sql`: 4.3 com só `SELECT (id, graphy, metry)`, `UPDATE (graphy, metry)` e `INSERT (graphy, metry)`; 4.5 com INSERT `id` = false e DELETE = false; guarda de PK exata; textos do cabeçalho, da 0.1 e da 4.3; 5.4, 5.5 e CP-14; remoção da 5.12 e do advisory lock na 5.11; o script deixa de rodar antes da migração; U-2 encerrado (RS-15).
  9. Revogação do `DELETE` aprovada; o README explica a limpeza com `mapas_teste` religada temporariamente ou `casaos` local (RS-16).
  10. `23505` em `mapas_pkey` responde 500, com log Error só com SqlState e constraint, sem retry; `23514` e os outros `23505` também respondem 500 (RS-17).
  11. `55P03` e `40P01` no POST respondem 409 com os textos do PUT, log Warning e ROLLBACK (decisão do orquestrador); no PUT, o `55P03` continua 409 (RS-18).
  12. PUT com `rowcount > 1`: ROLLBACK, 500 e log Error (RS-19).
  13. `VerificadorRoleEscrita` confere a PK exata, o `DELETE` e o INSERT em `id`; se algo falhar, a escrita fica desligada com Warning e `DataSource.Clear()`; o GET continua (RS-20).
  14. Com a escrita desligada, o POST responde 404 e o PUT 405 (decisão: aceitar o 404; RS-21).
  15. O middleware 403 fica entre `UseStatusCodePages` e `UseRateLimiter`, nunca como filtro, e a metadata vai também nos POST. Ele responde 403 para `Origin` presente, `Sec-Fetch-Site` com mais de um valor, ou `Sec-Fetch-Site` diferente de `none`/`same-origin` (trim, sem diferenciar maiúsculas). Não lê o corpo, não ecoa nada e registra log Debug sem os valores (RS-22).
  16. CHECK `(graphy IS NOT NULL OR metry IS NOT NULL) NOT VALID` na migração e `VALIDATE` depois do relatório (decisão: adotar; RS-13).
  17. GET com `LIMIT 2`: 2 linhas respondem 500 com log Error; documentar que a migração é pré-requisito também da leitura (RS-23).
  18. RS-1, RS-3 e RS-6 nas rotas novas; `Location` a partir do `RETURNING`; corpo de 64 KiB aprovado; limitador global de escrita de 10/min sem partição (decisão: adotar); POST do Postman numa coleção separada (RS-25).
  19. Riscos residuais R-N1 a R-N5 para o usuário aceitar, sem bloquear (RS-26, tarefa PU-R em `afazer.md`).

### PO3 - Banco: migração, relatório, mescla, `roles.sql` e SQL de referência
- Responsável: database (DataBrain)
- Concluído em: 2026-10-23. Nada foi executado no servidor. Tudo foi validado num container `postgis/postgis:17-3.5` descartável, já removido; o registro está em `database/README.md`, "Validação local do PO3".
  - PO3.1 `database/prova_identity_sem_usage.sql` (RS-8): o INSERT sem id funciona sem privilégio na sequência nos modos `BY DEFAULT` e `ALWAYS`; o `nextval` direto é negado; em `ALWAYS`, id explícito falha com `428C9`; sem INSERT em `id`, `OVERRIDING` falha com `42501`. Termina em ROLLBACK.
  - PO3.2 `database/migracao_mapas_pk.sql` (D-N3; RS-10, RS-12, RS-14), executada com `psql -X -v ON_ERROR_STOP=1 -1 -f -`:
    - ordem: timeouts → `LOCK ACCESS EXCLUSIVE` → guardas (RP-2, identity, PK/UNIQUE/EXCLUSION/índice único fora da PK exata, CHECK divergente, sessão aberta da `api_mapas_escrita` (RS-11)) → contagem → NULL/duplicado (máx. 20 ids) → `mapas_pkey` → `setval` sem voltar, com limite `2147483647 - 1000000` → `GENERATED ALWAYS` → CHECK `mapas_nao_vazia` `NOT VALID` → `DROP` do `ix_mapas_id` só se for btree simples de `mapas(id)` → conferência;
    - idempotente; sem `-1`, para no `LOCK TABLE`.
  - PO3.3 `database/validar_check_mapas.sql` (RS-13): aborta se houver linha vazia e é idempotente.
  - PO3.4 `database/relatorio_duplicados_mapas.sql` (RS-24): `REPEATABLE READ READ ONLY` com ROLLBACK, sem geometria; seções R1 a R5, classificação mesclável/conflitante e aviso de onde não salvar a saída.
  - PO3.5 `database/mescla_duplicados_mapas.sql` (RS-24): modelo comentado (prefixos `--: ` e `--c: `, extraídos com `sed`):
    - parte A, mescláveis: `LOCK EXCLUSIVE`, reclassificação na transação, UPDATE/DELETE por ctid com `rowcount = 1` e conferência por md5;
    - parte B: um conflitante escolhido pelo usuário (ctid + md5 via `-v`);
    - o padrão é ensaio (ROLLBACK); só grava com `-v confirmar=1`.
  - PO3.6 `database/roles.sql` revisão 5 (D-N4; RS-9, RS-15, RS-16):
    - a `api_mapas_escrita` fica com `SELECT (id, graphy, metry)`, `UPDATE (graphy, metry)` e `INSERT (graphy, metry)`, sem `DELETE` e sem INSERT em `id`;
    - guardas: a C3 exige `attidentity = 'a'` e a nova 0.2 exige a PK exata (o script aborta antes da migração);
    - a 4.5 espera INSERT em `id` = f e `DELETE` = f;
    - 5.4/5.5/5.6 ajustadas; a 5.11 ficou sem advisory lock; a 5.12 virou a conferência da sequência e a 5.13 é nova (constraints);
    - textos do cabeçalho, da 0.1, da 4.1 e da 4.3 atualizados; U-2 encerrado; limpeza RS-16 na seção 6.
  - PO3.7 `api_mapas/sql/consultas_referencia.sql`:
    - Q1/Q2 com `LIMIT 2`, sem filtrar a coluna, e a coluna nova `preenchida` (índice 8);
    - W1.V/W2.V mantidas;
    - W1/W2: `SET LOCAL lock_timeout` + UPDATE pela PK;
    - W3/W4: `SET LOCAL lock_timeout` + `INSERT ... RETURNING id`;
    - V1 do `VerificadorRoleEscrita`: colunas 0 a 5 de hoje, mais `pode_delete`, `insere_id`, `pk_exata` e `id_always` (esta última é uma recomendação além do RS-20);
    - o bloco histórico da A3.4 e a sugestão do `ix_mapas_id` saíram.
  - PO3.8 `teste_graphy/sql/validacao.sql` e `teste_metry/sql/validacao.sql`: conferência informativa `pk_exata`/`id_always` e textos de "esperado" antes e depois da migração.
  - PO3.9 `database/README.md`:
    - tabela e sequência no estado pós-migração;
    - roles e `api_mapas_escrita` na revisão 5;
    - seção nova "Migração para PRIMARY KEY (PU-DBA)" com os 9 passos do RS-11 e os comandos;
    - migração como pré-requisito também da leitura (RS-23);
    - "Limpeza de ids de teste" (RS-16) e U-2 encerrado;
    - nota no procedimento antigo de ligar o PUT;
    - registro da validação local.
  - Extra: `database/mapas.sql` ganhou, em comentário, a forma da tabela depois da migração.

### PO4 - Backend: POST de `graphy`/`metry`, PUT e GET com uma geometria por id
- Responsável: backend (BackBrain)
- Concluído em: 2026-10-23. Nada foi executado no banco real; `dotnet build` sem avisos e `dotnet test` com 1117 aprovados, 0 falhas e 18 ignorados (integração opt-in).
  - PO4.1 Domínio e Aplicação:
    - `IMapasRepository.ObterPorIdAsync` devolve `LeituraMapa` (NaoEncontrada, ColunaVazia, Duplicada, Encontrada);
    - `IMapasEscritaRepository.AtualizarAsync` (PUT) e `CriarAsync` (POST) devolvem `ResultadoEscrita` (o `ResultadoSubstituicao` saiu);
    - corpo de objeto único `CorpoGeometriaDto` (Q1), lido e validado por `EntradaGeometria` (RP-11, RP-14), comum a PUT e POST;
    - `CriacaoGeometriasService` novo (POST, EventIds 4201 a 4212); `AtualizacaoGeometriasService` sem lista, sem limite de 100 e sem U-3;
    - GET devolve `RespostaGeometriaDto` (objeto único da Q2); 2 linhas ou teto de bytes estourado lançam `LeituraInconsistenteException` (500; log Error 1004 e Warning 1005);
    - `LimitesEscrita.LimiteGeometriasPut`, `MotivosGeometria.QuantidadeAcima` e `OpcoesConsulta.LimiteItens` saíram.
  - PO4.2 Infraestrutura:
    - SQL copiado exatamente do PO3.7: Q1/Q2 (`LIMIT 2`, `preenchida` lida antes), W1.V/W2.V, W1/W2 (UPDATE pela PK), W3/W4 (`INSERT ... RETURNING id`) e V1;
    - PUT: rowcount 0 = ROLLBACK e 404; 1 = COMMIT e 204; mais de 1 = ROLLBACK, log Error 2116 e 500 (RS-19); `55P03` = 409;
    - POST: `55P03`/`40P01` = ROLLBACK, Warning 2111 e 409 (RS-18); `23505` em `mapas_pkey` = Error 2132 ("id gerado já existe; conferir a sequência"); outros `23505` e `23514` = Error 2133; os dois com SqlState e constraint, sem retry e sem Detail/MessageText (RS-17); RETURNING inválido = Error 2134;
    - sem advisory lock, sem DELETE, sem lista e sem id explícito em INSERT;
    - `VerificadorRoleEscrita` com a V1: colunas 0 a 7 verdadeiras = `RolePrivilegiada`; 8 ou 9 falsas = `SemChavePrimaria` (motivo novo); Warning 2205/2206; o `Program` chama `DataSource.Clear()` (RS-20).
  - PO4.3 API:
    - `POST /api/mapas/graphy|metry` com `LeitorCorpoHttp` (sem binding automático), 201 via `TypedResults.Created` sem corpo e `Location: /api/mapas/{id}/{coluna}` a partir do int do RETURNING; com a escrita desligada, as rotas não existem (POST 404, PUT 405 - RS-21);
    - `OrigemEscritaMiddleware` entre `UseStatusCodePages` e `UseRateLimiter`, só nos endpoints com `MarcadorEscritaMapas` (PUT e POST): 403 com `Origin` presente, `Sec-Fetch-Site` com mais de um valor ou diferente de `none`/`same-origin` (trim, sem diferenciar maiúsculas); não lê o corpo, não ecoa cabeçalhos, log Debug 4012 sem valores (RS-22);
    - limitador global de escrita sem partição (`ApiMapas:LimiteEscritasPorMinuto`, padrão 10) encadeado a global/IP, escrita por IP e simultâneas; PUT e POST compartilham todos;
    - `LimiteBytesCorpoPut` com padrão 65.536 (appsettings e `OpcoesAtualizacao`);
    - eventos 4006 a 4010 com "PUT e POST"; `UseHttpMethodOverride` proibido com comentário (RP-12/RS-3).
  - Testes existentes ajustados só para o modelo novo (os testes novos e a cobertura são do PO6):
    - `ServidorPostgresFalso` com a coluna `preenchida` e a resposta `ColunaVazia`; `ServidorPostgresEscritaFalso` com o nome da constraint no ErrorResponse;
    - `RepositorioFalso`, `RepositorioEscritaFalso` e `CorposPut` no modelo de uma geometria;
    - removidos os testes de comportamento que deixou de existir: lista vazia/U-3, limite de 100 itens, truncado, advisory lock, DELETE e contagem divergente do INSERT;
    - `IntegracaoEscritaTests` portado só para compilar (a suíte do PO6.4 o substitui).
  - Verificado num teste descartável (removido; fica para o PO6): POST 201 com `Location`, 409/415/400 no POST, 403 de origem sem consumir o limite e sem eco, limitador global somando PUT e POST, POST com override `PUT` só insere.

### PO5 - Revisão de segurança da implementação (POST, PUT e GET com uma geometria por id)
- Responsável: security (SecBrain)
- Concluído em: 2026-10-23. Parecer: **aprovada com observações**, sem achado Crítico, Alto ou Médio. Achados de severidade Baixa e destino de cada um (decisão do orquestrador: corrigir tudo agora):
  1. O limitador global de escrita usa a mesma chave `LimiteEscritasPorMinuto` do limite por IP. Destino: README no PO7.2, com a conta de disco no teto de 60/min.
  2. Uma escrita recusada por simultaneidade gasta a cota por minuto. Destino: PO5.C2a (BackBrain), com `escritaSimultanea` primeiro no `CreateChained` (`Program.cs:294`).
  3. A V1 não confere UPDATE em `id`, `rolcreatedb`, `rolreplication` nem membership (`pg_auth_members`). Destino: PO5.C1a (DataBrain, V1 do `consultas_referencia.sql`) e depois PO5.C2b (BackBrain, `VerificadorRoleEscrita`, tratando como `RolePrivilegiada`).
  4. A mescla grava com `-v confirmar=0`. Destino: PO5.C1b (DataBrain, `mescla_duplicados_mapas.sql`, linhas 137 e 248: gravar só com `confirmar` exatamente `'1'`).
  5. A seção R2 do relatório engana quando roda como `api_mapas_leitura`. Destino: PO5.C1c (DataBrain: aviso no cabeçalho e "sem privilégio" quando `last_value` vier NULL).
- Pedidos ao PO6, além do que já estava previsto (registrados como PO6.6): 403 sem consumir limite em cada uma das 3 condições; `Origin` vazio e `null`; `Sec-Fetch-Site` vazio, com vírgula e repetido; ordem da cadeia de limitadores; V1 com privilégio sobrando ou sem PK (POST 404, PUT 405).

### PO5.C1 - Correções do parecer PO5 (DataBrain)
- Responsável: database (DataBrain)
- Concluído em: 2026-10-23. Nada foi executado no banco real e nenhum C# foi alterado. Validado num container `postgis/postgis:17-3.5` descartável, sem porta publicada, removido no fim.
  - a. Achado 3: a V1 de `api_mapas/sql/consultas_referencia.sql` ganhou 4 colunas no fim, sem mudar os índices 0 a 9: 10 `atualiza_id` (`has_column_privilege(current_user,'public.mapas','id','UPDATE')`), 11 `rolcreatedb`, 12 `rolreplication` e 13 `membro_de_role` (`EXISTS` em `pg_auth_members` com `member = r.oid`). Regra: habilita só com 0 a 7 = false, 8 e 9 = true e 10 a 13 = false. Cada privilégio extra concedido isoladamente ligou só a coluna esperada (UPDATE em `id`, na tabela e `TO PUBLIC`; CREATEDB; REPLICATION; membership comum, em role predefinida e com `INHERIT FALSE, SET FALSE`). Libera a PO5.C2b.
  - b. Achado 4: em `database/mescla_duplicados_mapas.sql`, partes A (`--: `) e B (`--c: `), o `confirmar` é comparado no servidor (`:'confirmar' = '1'`) e vira a variável `gravar`; sem a variável, `gravar = false`. Ensaio (ROLLBACK, nada alterado) com confirmar ausente, `0`, `nao`, `sim`, `01`, vazio, com aspas e só `-v gravar=1`; COMMIT só com `confirmar=1`.
  - c. Achado 5: `database/relatorio_duplicados_mapas.sql` avisa no cabeçalho e na R2 que a R2 só é confiável como `casaos`. Sem privilégio `SELECT,USAGE` na sequência, `last_value` e `proximo_valor` mostram `sem privilegio`, e a coluna nova `r2_confiavel` sai `f`; com privilégio e sequência sem uso, `last_value` = `nunca usada`. Conferido como `api_mapas_leitura` e como `casaos`.
  - Documentação: `database/README.md` atualizado (regra da V1 com 14 colunas, aviso da R2, `confirmar` exatamente 1, registro da validação). O `roles.sql` não cita a V1 e não mudou.

### PO5.C2 - Correções do parecer PO5 (BackBrain)
- Responsável: backend (BackBrain)
- Concluído em: 2026-10-23. Nada foi executado no banco real; `dotnet build` sem avisos e `dotnet test` com 1121 aprovados, 0 falhas e 18 ignorados (integração opt-in).
  - a. Achado 2: `escritaSimultanea` passou a ser o primeiro limitador do `CreateChained` (`api_mapas/src/ApiMapas.Api/Program.cs`), com comentário. O encadeamento para no primeiro que recusa, então o 429 por simultaneidade não gasta as cotas de janela fixa (global, escrita por IP e escrita global). Para o GET, `escritaSimultanea` é `NoLimiter`. Conferido num teste descartável (removido; fica para o PO6): com 4 escritas/min, 3 escritas presas e 3 recusas por simultaneidade, a 4ª escrita depois passa e a 5ª recebe 429; o GET responde 200 durante as recusas.
  - b. Achado 3: `VerificadorRoleEscrita` com a V1 nova de 14 colunas, copiada exatamente da linha 291 de `api_mapas/sql/consultas_referencia.sql` (o teste de texto exato passa). As colunas 10 a 13 (`atualiza_id`, `rolcreatedb`, `rolreplication`, `membro_de_role`) verdadeiras levam a `RolePrivilegiada`, com Warning 2205 e `DataSource.Clear()` no `Program`; log Debug 2208 novo com os 4 valores. Os textos do motivo (`ConexaoEscritaConfig`) foram atualizados.
  - Testes ajustados só para o build: o protocolo falso do verificador (`ConexaoEscritaConfigTests`) passou a ter 14 colunas, completando com false; em `ApiEscritaDesligadaTests.R1_Real_CadaCriterio_405`, o critério 10 a 13 desliga a escrita.

### PO6 - Testes do POST, do PUT e do GET com uma geometria por id (qa / TestBrain)
- Responsável: qa (TestBrain)
- Concluído em: 2026-10-23. Nada foi executado no banco real (`PG_RUN_INTEGRATION` removido do ambiente em todas as execuções). `dotnet test` da `api_mapas`: 1399 aprovados, 0 falhas, 25 ignorados (integração opt-in; 18 anteriores + 7 novos), estável em 5 execuções seguidas.
  - Cobertura (coverlet, `api_mapas`): 97,81% de linhas (2860/2924) e 95,53% de ramos (898/940). Só o código escrito à mão: 99,59% de linhas. Por pacote: Api 100%, Aplicacao 97,92%, Dominio 100%, Infraestrutura 95,41% (o que falta é código gerado pelo `LoggerMessage` e fechamentos de `await using`).
  - PO6.5: `teste_graphy` 391 aprovados/3 ignorados, 93,99% de linhas; `teste_metry` 881/3, 98,64%.
  - Testes novos: `Aplicacao/CriacaoGeometriasServiceTests.cs`, `Api/ApiPostTests.cs` (POST em memória e pilha completa no protocolo falso), `Api/OrigemEscritaTests.cs` (403 de origem e Kestrel cru), `Api/LimitesEscritaTests.cs`, `Api/ApiGetUmaGeometriaTests.cs`, `Infraestrutura/MapasEscritaRepositoryPostTests.cs` (ramos do POST e V1 de 14 colunas).
  - Ajustados: `Api/ApiEscritaTests.cs` (escrita desligada: POST metry 404, GET metry 200 e motivo `SemChavePrimaria`), `Api/LeitorCorpoHttpTests.cs` (coleção Kestrel serial), `Integracao/IntegracaoEscritaTests.cs` (limpeza no `finally` com `rowcount == 1` via `mapas_teste`, id do `Location`, CN-1/2/3/4/6/13/14) e suporte (`EscritaFalsos.Sequencial`, `ApiEscritaFactory.IpPorCabecalho`, loggers que leem o estado estruturado).
  - Achado PO6-1 (Baixa, para o analista): uma escrita recusada pela cota de escrita por minuto gasta 2 permissões do limitador global por IP (`Program.cs:297` + `AttemptAcquire` e `AcquireAsync` do middleware). Detalhe no relatório do TestBrain.

### Contrato do POST e decisões (referência, implementados; plano do PO1-R movido de `afazer.md` no PO7)

> Pedido do usuário: "Crie o método POST para os endpoints graphy e metry". Features abertas pelo MegaBrain.
> **Replanejado em 2026-10-23 (PO1-R)**: depois do parecer PO2 do SecBrain, o usuário mudou o modelo de dados.
> O plano anterior (D-PO1 a D-PO10: POST com id na rota e várias linhas por id) está **superado**; o registro
> fica em `feito.md` (PO1 e PO2).

**Decisões do usuário (fechadas, não reabrir):**
1. **`public.mapas.id` passa a ser PRIMARY KEY com autoincremento**: uma linha por id, com as colunas `graphy` e `metry` da mesma linha.
2. **POST cria um id novo pela sequência, sem id na rota**: `POST /api/mapas/graphy` e `POST /api/mapas/metry`. Preenche só a coluna da rota, deixa a outra NULL e devolve o id gerado no `Location`.
3. **PUT e GET passam a tratar uma geometria por id** (a da coluna da rota).
4. **`last_value`** foi a escolha do usuário para o aviso de colisão (PO2.2). Ver D-N2: com a PK e o id vindo da sequência, o aviso perde o sentido e sai do plano.
5. Continuam valendo do pedido original: mesmas validações (`WktValidator`, `GeometriaEntrada`, política de SRID por coluna, RP-11, RP-14), 10.000 caracteres por WKT, conexão `PG_ESCRITA_*`, liga/desliga do D-P9 e method override proibido.
6. **Respostas à PU (2026-10-23, todas as recomendações aceitas):**
   - Q1: o corpo do POST e do PUT é um objeto único: `{"wkt":"POINT(-46.63 -23.55)","srid":4326}`.
   - Q2: o GET devolve um objeto único `{"id","coluna","tipo","srid","wkt","geojson","x","y","longitude","latitude"}`, sem `quantidade`, `truncado` nem lista.
   - Q3: o PUT pode preencher a outra coluna da mesma linha. O PUT nunca cria id (sem linha, responde 404).
   - Q4: o PUT exige 1 geometria e só faz UPDATE da coluna, sem limpar nem apagar linha. O `DELETE` da `api_mapas_escrita` é revogado, o que encerra o U-2. Limpar ou apagar vira um pedido futuro.
   - Q5: `id` passa a `GENERATED ALWAYS AS IDENTITY`.
   - Q6: o DataBrain entrega um relatório somente leitura e um modelo **comentado** de mescla só para os ids "mescláveis" (no máximo 1 linha com `graphy` e 1 com `metry`). Nos ids conflitantes, o usuário escolhe qual linha manter.

**Ordem:** PU (concluída) → PO2b security (concluído) → PO3 database (concluído) → PU-DBA (migração pelo usuário/DBA) → PO4 backend (concluído) → PO5 security (concluído) → PO5.C1 database e PO5.C2 backend (concluído) → PO6 qa (concluído) → PO7 analista. Frontend: fora do escopo.

#### Plano do PO1-R (SubBrain), ajustado pelo parecer PO2b

#### Decisões do SubBrain (D-N1 a D-N12)
- **D-N1 - Modelo.** `id` int4 identity e PRIMARY KEY, uma linha por id. `graphy` e `metry` do mesmo id ficam na mesma linha; qualquer uma pode ser NULL. A tabela continua só com `id`, `graphy` e `metry` (RP-2).
- **D-N2 - `last_value` e o aviso de colisão saem do plano.**
  - Motivo: a API não informa mais id em nenhum INSERT (o POST usa a identity e o PUT só faz UPDATE). Com isso, o risco que motivou o D-P3 e o D-PO2 (id explícito à frente da sequência, misturando linhas sem PK) deixa de existir no caminho da API. Também não há consulta à sequência nem `max(id)`.
  - Risco residual: um INSERT manual com id explícito (ex.: `casaos`) à frente da sequência faz um POST futuro falhar com `23505` (violação da PK). Esse caso responde **500 genérico**, com log **Error** próprio ("id gerado já existe; conferir a sequência", sem valores). Não mistura dados, porque a PK impede. Com a Q5 (`GENERATED ALWAYS`), id explícito só entra com `OVERRIDING SYSTEM VALUE` (manual, fora da API).
  - A escolha do usuário (`last_value`) fica registrada como superada pela decisão 1. Se ele ainda quiser um aviso, ele volta como pedido separado.
- **D-N3 - Migração (DataBrain, script novo em `database/`, executado pelo usuário/DBA; ajustada pelo PO2b, RS-8 a RS-14).**
  - Tudo numa única transação, executada com `psql -v ON_ERROR_STOP=1 -1`, nesta ordem:
    1. `SET LOCAL lock_timeout = '5s'` e `SET LOCAL statement_timeout = '5min'`;
    2. `LOCK TABLE public.mapas IN ACCESS EXCLUSIVE MODE`;
    3. guardas: só as 3 colunas (RP-2) e `id` identity;
    4. contagem de linhas (guardada para a conferência);
    5. detecção de `id` NULL ou duplicado. Se houver, `RAISE EXCEPTION` com a quantidade e no máximo 20 ids, nunca geometrias. Nada é alterado;
    6. `ADD CONSTRAINT mapas_pkey PRIMARY KEY (id)`;
    7. `setval(pg_get_serial_sequence('public.mapas','id'), GREATEST(max(id), last_value))`. Nunca volta a sequência, e a tabela vazia não mexe nela. `RAISE` se `max(id) > 2147483647 - 1000000`;
    8. `ALTER COLUMN id SET GENERATED ALWAYS` (Q5);
    9. `ADD CONSTRAINT ... CHECK (graphy IS NOT NULL OR metry IS NOT NULL) NOT VALID` (RS-13);
    10. `DROP INDEX IF EXISTS`, só se o índice for de `public.mapas(id)`;
    11. conferência: PK, sequência ≥ `max(id)`, contagem igual à de antes e `attidentity = 'a'`;
    12. `COMMIT`.
  - Idempotência passo a passo:
    - a PK só conta como existente se for **exata** em `id` (`contype = 'p'`, `conkey` = `attnum` de `id`, `indisvalid`);
    - outra PK, ou UNIQUE em outra forma, aborta;
    - cada passo já aplicado é pulado.
  - O `VALIDATE CONSTRAINT` do CHECK é feito depois, como passo separado, quando o relatório não mostrar linhas vazias.
  - Relatório à parte (Q6):
    - roda em `READ ONLY` e não mostra geometria por padrão;
    - mostra, por id, a contagem de linhas, de linhas com `graphy` e de linhas com `metry`, e a classificação "mesclável" ou "conflitante";
    - a saída **nunca** vai para o repositório nem para o OneDrive.
  - Modelo comentado de mescla (Q6):
    - roda numa transação com `LOCK ... EXCLUSIVE` e reclassifica os ids dentro dela;
    - faz UPDATE/DELETE por `ctid`, exigindo `rowcount = 1` (senão `RAISE`);
    - nunca toca nos conflitantes.
  - Pré-requisitos do usuário/DBA: ver PU-DBA.
- **D-N4 - Impacto no `database/roles.sql`.**
  - **Sequência: nenhuma mudança.** O `nextval` da identity não verifica privilégio na sequência; basta o `INSERT` na tabela. Isso já está documentado no próprio `roles.sql` (nota da seção 4.1) e acontece hoje com a `mapas_teste`, que insere sem id e sem GRANT na sequência. Por isso a `api_mapas_escrita` **não precisa de `USAGE`**: a guarda da seção 5 (sequência sem privilégio para as roles da API) e o R-3 continuam iguais. O DataBrain confirma isso num teste no PO3.1.
  - `api_mapas_escrita`:
    - `INSERT (graphy, metry)`, sem `id`, porque nenhum INSERT da API informa id;
    - `SELECT (id, graphy, metry)` (para o `WHERE id` e o `RETURNING id`);
    - `UPDATE (graphy, metry)`;
    - **`DELETE` revogado** (Q4).
  - `mapas_teste` e `api_mapas_leitura`: sem mudança.
  - RP-2 e seção 5 (ajustado pelo PO2b, RS-15):
    - a guarda das 3 colunas continua. A guarda C3 passa a exigir `attidentity = 'a'` (Q5);
    - nova guarda de PK **exata** em `id` (mesmos critérios do D-N3). Sem ela, o script aborta e aponta para a migração: o `roles.sql` revisado deixa de rodar antes da migração, e isso fica documentado;
    - 4.3: só `SELECT (id, graphy, metry)`, `UPDATE (graphy, metry)` e `INSERT (graphy, metry)`, sem `DELETE`;
    - 4.5: a matriz espera INSERT em `id` = false e DELETE = false;
    - os textos do cabeçalho, da 0.1 e da 4.3 são atualizados;
    - a 5.4, a 5.5 e o CP-14 são ajustados;
    - a 5.12 sai e o advisory lock sai da 5.11;
    - nova conferência informativa da sequência na seção 5;
    - o U-2 fica registrado como encerrado.
- **D-N5 - Contrato do POST.**
  - Rotas: `POST /api/mapas/graphy` (só SRID 4326) e `POST /api/mapas/metry` (4326, 4674, 3857, 31983). Constantes de rota novas. `POST /api/mapas/{id}/graphy` continua **405**.
  - Corpo: objeto único `{"wkt":"...","srid":4326}` (Q1), lido pelo `LeitorCorpoHttp`, sem binding automático (RS-1).
  - SQL W3/W4, constante por coluna: `INSERT INTO public.mapas (graphy) VALUES (ST_GeomFromText(@wkt, @srid)::geography) RETURNING id` (no `metry`, sem `::geography`). Antes vem a validação no banco (`ST_IsValid`/`GeometryType`) na mesma conexão de escrita. Sem advisory lock, sem UPDATE e sem DELETE.
  - Sucesso: **201** com `TypedResults.Created`, sem corpo, `Location: /api/mapas/{idGerado}/{coluna}` relativo e montado a partir do int do `RETURNING` (RS-4, RS-25).
  - Não é idempotente: cada POST cria um id novo, e um retry após 500/timeout pode criar outro. Isso fica documentado (RS-5).
  - Erros:
    - 400 `Corpo inválido` e `Geometria inválida` (o `detail` sem índice, porque o corpo tem uma geometria só);
    - 403 de origem (RS-2);
    - 413, 415 e 429;
    - **409** para `55P03` e `40P01`, com os textos do PUT, log Warning e ROLLBACK (RS-18, decisão do orquestrador);
    - 500 para o `23505` em `mapas_pkey` (log Error só com SqlState e constraint, sem retry; RS-17), para os outros `23505`, para o `23514`, para o prazo de 30 s, para o `57014` e para o pool esgotado.
    - Não existe 404 nas rotas com a escrita ligada.
- **D-N6 - Contrato do PUT (uma geometria por id).**
  - `PUT /api/mapas/{id}/{coluna}`, com o corpo da Q1.
  - SQL W1/W2 novos: `BEGIN` → `SET LOCAL lock_timeout = '5s'` → `UPDATE public.mapas SET graphy = ... WHERE id = @id` → `COMMIT`.
  - Resultados: 0 linhas responde **404**, `55P03` responde **409** (mesmos textos) e sucesso responde **204**. Se o `rowcount` for maior que 1, faz ROLLBACK e responde 500 com log Error (RS-19).
  - O advisory lock 1296125011 sai: o lock de linha do UPDATE pela PK basta.
  - Preenche a coluna mesmo que estivesse NULL (Q3) e nunca toca a outra coluna. É idempotente.
  - Não há lista vazia, limpeza nem DELETE (Q4).
  - Ficam obsoletos o D-P1 e o D-P2, o U-3, o limite de 100 itens e o U-2 (`DELETE` revogado). O D-P3 continua: o PUT não cria id.
- **D-N7 - Contrato do GET (uma geometria por id).**
  - Resposta: objeto único (Q2).
  - 404 com os textos atuais quando não há linha ou quando a coluna é NULL.
  - Saem `LIMIT 101`, `ORDER BY ctid`, `quantidade` e `truncado`.
  - O `ApiMapas:LimiteBytesGeometrias` continua como teto do item. Acima dele, a resposta é 500 genérico com log Warning; isso só acontece com dado inserido fora da API, porque a escrita limita o WKT a 10.000 caracteres.
  - Se a consulta achar 2 linhas (migração ainda não aplicada), a resposta é 500 com log Error próprio. Para isso a consulta usa `LIMIT 2` (RS-23). A migração é pré-requisito também da leitura.
- **D-N8 - Requisitos do parecer PO2 que continuam valendo (RS-1 a RS-7):**
  - RS-1: Content-Type estrito com 415 pelo mesmo `LeitorCorpoHttp`, sem binding automático.
  - RS-2: middleware só nos endpoints de escrita (PUT e POST), **antes do rate limiter**, com ProblemDetails de texto fixo, sem eco do cabeçalho. Os detalhes estão no RS-22.
  - RS-3: proibido `UseHttpMethodOverride`. Os cabeçalhos de override são ignorados, e um POST com override `PUT`/`DELETE` só faz INSERT.
  - RS-4: logs com o método (POST ou PUT) e EventIds próprios para o POST. Os eventos 4006 a 4010 passam a dizer "PUT e POST". 201 via `TypedResults.Created`, sem corpo, com `Location` montado a partir do int.
  - RS-5: o README documenta o crescimento da tabela e do disco (até 10 POST/min por IP, cada um com WKT de no máximo 10.000 caracteres: cerca de 6 MB/h no pior caso) e o retry sem idempotência.
  - RS-6: testes de 415 com `text/plain`, `application/x-www-form-urlencoded`, `multipart/form-data`, sem Content-Type, lista separada por vírgula, Content-Type duplicado e `charset=latin1`.
  - RS-7: o Postman e a integração não criam ids fora do controle (D-N12).
- **D-N9 - Limites.**
  - PUT e POST dividem o limitador: 10 escritas por minuto por IP, 3 simultâneas, **mais um limitador global de escrita de 10/min, sem partição** (RS-25, decisão do orquestrador), tudo encadeado ao global de 60/min.
  - Mantidos: prazo de 30 s, pool de escrita 5 e 10.000 caracteres por WKT.
  - O padrão do `ApiMapas:LimiteBytesCorpoPut` cai para **65.536** bytes, porque o corpo agora tem uma geometria só (a faixa 1..1048576 continua). As chaves não mudam de nome.
  - Ordem das verificações no POST: 403 → 429 → 415 → 413 → JSON (400) → geometria (400) → banco (400/500/201).
  - Ordem no PUT: a mesma, com o id (400) logo depois do 429 e o banco respondendo 404/409/204.
- **D-N10 - Liga/desliga.** Valem as regras do D-P9, com mais condições (RS-20). Na subida, o `VerificadorRoleEscrita` também confere:
  - a PK exata em `id`;
  - `DELETE` na tabela;
  - INSERT em `id`.
  Se faltar a PK ou sobrar privilégio, a escrita fica **desligada**, com log Warning e `DataSource.Clear()`; o GET continua funcionando. O log de início passa a dizer "Escrita (PUT e POST) HABILITADA/DESLIGADA". Com a escrita desligada, `POST /api/mapas/graphy|metry` responde **404** (a rota não existe) e o PUT responde 405 (RS-21, decisão: aceitar o 404).
- **D-N11 - `teste_graphy` e `teste_metry`.**
  - Sem mudança de código prevista. Eles já fazem `INSERT (graphy|metry) ... RETURNING id` sem id, e cada WKT ganha um id próprio, o que é coerente com uma geometria por id. Também funcionam com `GENERATED ALWAYS`. A limpeza dos testes de integração (DELETE por id com `rowcount == 1`) passa a ser garantida pela PK.
  - Mudanças só de documentação:
    - os READMEs e a nota S3 ("sem PK") ficam desatualizados;
    - o `validacao.sql` dos dois ganha uma conferência informativa da PK (DataBrain);
    - o TestBrain roda de novo as suítes dos dois.
- **D-N12 - Postman e integração com ids sob controle (RS-7).**
  - Postman:
    - os POST ficam numa **coleção separada** (RS-25), fora da execução padrão do CLI;
    - o script de teste captura o id do `Location` em variáveis de coleção;
    - o README traz a limpeza (`DELETE ... WHERE id = <capturado>`), feita com a `mapas_teste` religada temporariamente ou com `casaos` local (RS-16);
    - os PUT usam o `idEscrita` criado antes com a `mapas_teste`.
  - Integração da `api_mapas`: todo id criado por POST é lido do `Location` e apagado no `finally` com a `mapas_teste`, conferindo `rowcount == 1`.

#### Requisitos do parecer PO2b (RS-8 a RS-26, obrigatórios; o detalhe está em `feito.md`, PO2b)
- **Banco (DataBrain, PO3):**
  - RS-8: identity sem `USAGE`, provado nos modos `BY DEFAULT` e `ALWAYS`.
  - RS-9: C3 com `attidentity = 'a'` e INSERT em `id` = false.
  - RS-10: migração numa única transação, na ordem do D-N3, com `ON_ERROR_STOP` e `-1`.
  - RS-12: `setval`, limite de `max(id)` e conferência informativa da sequência.
  - RS-13: CHECK `NOT VALID` e `VALIDATE` depois do relatório.
  - RS-14: idempotência com PK exata e `DROP INDEX` restrito.
  - RS-15: mudanças no `roles.sql` (D-N4).
  - RS-24: relatório em `READ ONLY` e mescla segura (D-N3).
  - Documentação do RS-23.
- **Usuário/DBA:**
  - RS-11: o primeiro passo do PU-DBA é desligar a escrita antiga; fazer um segundo backup se houver intervalo grande antes da migração.
  - RS-26: aceitar os riscos residuais R-N1 a R-N5 (tarefa PU-R).
- **Backend (BackBrain, PO4):**
  - RS-16: `DELETE` revogado; a limpeza documentada usa `mapas_teste` religada temporariamente ou `casaos` local.
  - RS-17: `23505` em `mapas_pkey` responde 500, com log Error só com SqlState e constraint, sem retry; `23514` e os outros `23505` também respondem 500.
  - RS-18: `55P03`/`40P01` no POST respondem 409 com os textos do PUT, log Warning e ROLLBACK.
  - RS-19: PUT com `rowcount > 1` faz ROLLBACK, responde 500 e registra log Error.
  - RS-20: `VerificadorRoleEscrita` com PK exata, `DELETE` e INSERT em `id`; com problema, a escrita fica desligada com Warning e `DataSource.Clear()`.
  - RS-21: com a escrita desligada, o POST responde 404 e o PUT 405.
  - RS-22: middleware 403 entre `UseStatusCodePages` e `UseRateLimiter`, nunca como filtro, com a metadata também nos POST. Responde 403 para `Origin` presente (qualquer valor), `Sec-Fetch-Site` com mais de um valor, ou `Sec-Fetch-Site` diferente de `none`/`same-origin` (com trim, sem diferenciar maiúsculas). Não lê o corpo, não ecoa nada e registra log Debug sem os valores dos cabeçalhos.
  - RS-23: GET com `LIMIT 2`; 2 linhas respondem 500 com log Error. A migração é pré-requisito também da leitura.
  - RS-25: RS-1, RS-3 e RS-6 nas rotas novas; `Location` a partir do `RETURNING`; corpo de 64 KiB; limitador global de escrita de 10/min sem partição; POST do Postman em coleção separada.
- **QA (TestBrain, PO6):** `55P03` no POST, `rowcount > 1`, 403 sem consumir limite, escrita desligada por privilégio a mais e 415 nas rotas novas.

#### Critérios de aceite (CN-1 a CN-15)
- CN-1: `POST /api/mapas/graphy` válido responde 201, sem corpo, com `Location: /api/mapas/{n}/graphy`. A linha `n` é nova, tem `graphy` igual ao enviado e `metry` NULL. Nenhuma outra linha muda. Com `metry` é simétrico.
- CN-2: dois POST iguais criam dois ids distintos (não idempotente). POST simultâneos nunca recebem o mesmo id.
- CN-3: um POST de `metry` seguido de `PUT /api/mapas/{n}/graphy` deixa uma única linha `n` com as duas colunas (Q3). O GET de cada coluna devolve uma geometria.
- CN-4: PUT em id sem linha responde 404 e nada é criado. PUT repetido chega ao mesmo estado. A outra coluna nunca muda. Nenhum caminho da API apaga linha (Q4).
- CN-5: o GET devolve uma geometria no formato da Q2. Sem linha ou com a coluna NULL, responde 404. Com 2 linhas no id (sem migração), responde 500 com log Error.
- CN-6: as validações do CP-6 e do CP-8, adaptadas ao corpo da Q1, valem para POST e PUT, e nada é gravado. Os 415 do RS-6 estão todos cobertos.
- CN-7: PUT e POST respondem 403 **sem consumir o rate limit** quando chega `Origin` (qualquer valor), `Sec-Fetch-Site` com mais de um valor, ou `Sec-Fetch-Site` diferente de `none`/`same-origin`. O GET não é afetado (RS-22).
- CN-8: os limites compartilhados funcionam. A 4ª escrita simultânea responde 429. A 11ª escrita no mesmo minuto também responde 429, tanto no mesmo IP quanto somando todos os IPs (limitador global de escrita), com PUT e POST contados juntos.
- CN-9: o `23505` (em `mapas_pkey` ou outro) e o `23514` no POST respondem 500 com log Error, sem retry. `55P03`/`40P01` no POST e `55P03` no PUT respondem 409. PUT com `rowcount > 1` responde 500 com ROLLBACK. Falha, cancelamento ou prazo fazem ROLLBACK.
- CN-10: com escrita desligada (CP-11, sem PK exata, ou `DELETE`/INSERT em `id` sobrando), o POST responde 404, o PUT responde 405 e o log de início diz "Escrita (PUT e POST) DESLIGADA". O GET continua funcionando.
- CN-11: method override é ignorado (RS-3). `POST /api/mapas/{id}/graphy`, PATCH e DELETE respondem 405 e não gravam. `OPTIONS` não devolve CORS. `Host` malicioso responde 400.
- CN-12: SQL constante por coluna, parametrizado, sem id explícito em INSERT e sem acesso à sequência; a escrita usa só a conexão de escrita.
- CN-13: a migração para sem alterar nada quando há duplicado, id NULL, outra PK/UNIQUE ou `max(id)` acima do limite. Quando roda, cria a PK exata, ajusta a sequência (nunca para trás), aplica `GENERATED ALWAYS` e o CHECK `NOT VALID`, e mantém a contagem de linhas. Rodar de novo não faz nada.
- CN-14: os privilégios conferem com o D-N4: a `api_mapas_escrita` não tem privilégio na sequência e mesmo assim o POST funciona; não tem `DELETE` (Q4). A seção 5 do `roles.sql` não dá alerta.
- CN-15: cobertura de linhas de pelo menos 80% em `api_mapas`, `teste_graphy` e `teste_metry` (meta: manter cerca de 99%). Postman e READMEs atualizados (PO7).

### PO7 - Fechamento do pedido do POST (analista / SubBrain)
- Responsável: analista (SubBrain)
- Concluído em: 2026-10-23. Nada foi executado contra a API nem contra o banco.
  - `api_mapas/README.md` reescrito para o modelo de uma geometria por id:
    - aviso da migração como pré-requisito, inclusive da leitura (RS-23);
    - tabela de rotas com GET, PUT e POST;
    - condições do U-1 revistas;
    - escrita desligada: motivos da V1 de 14 colunas e `SemChavePrimaria`, com POST 404 e PUT 405 (RS-21);
    - `appsettings` (`LimiteBytesCorpoPut` 64 KiB para PUT e POST);
    - seção "Limitadores", com a ordem da cadeia, o achado 1 do PO5 (`LimiteEscritasPorMinuto` controla o limite por IP e o da API toda) e a limitação conhecida PO6-1 (escrita recusada pela cota gasta 2 permissões do global por IP; aceita, sem correção);
    - crescimento de disco no padrão 10 e no teto 60 (RS-5);
    - "403 de origem" (RS-22);
    - contratos do GET (objeto único), do corpo de escrita, do PUT (UPDATE pela PK, Q3/Q4) e do POST (201 + `Location`, não idempotente, 409/500);
    - limpeza de ids (RS-16);
    - logs, `curl`, as duas coleções Postman, testes (PO6) e manual da base;
    - riscos R-N1 a R-N5 com aceite pendente (PU-R).
  - `api_mapas/.env.example`: comentário da escrita (PUT e POST; POST 404/PUT 405 desligados; só depois da migração). O `appsettings.json` não mudou (já com 65536) e está documentado no README.
  - Postman:
    - `api_mapas/postman/api_mapas.postman_collection.json` regerada: GET com objeto único, pasta `escrita (PUT)` com corpo novo, 10 PUT, 403 de origem e POST 405 na rota com id;
    - `api_mapas/postman/api_mapas_post.postman_collection.json` nova e separada (RS-25):
      - só roda com `permitirCriacao=sim`;
      - captura o id do `Location` em `idCriadoGraphy`/`idCriadoMetry`/`idsCriados`;
      - PUT na linha criada (Q3), 400, 415 e 403;
      - imprime os ids e o SQL de limpeza.
    - Validação de formato sem execução: `postman collection migrate` para v3 no scratchpad e `postman collection lint` com 0 erros e 0 avisos (27 e 11 itens); os 48 scripts passaram na checagem de sintaxe do Node.
  - `teste_graphy/README.md` e `teste_metry/README.md`: nota S3 atualizada (PK `mapas_pkey` e `GENERATED ALWAYS` depois da migração, sem mudança de código, colunas `pk_exata`/`id_always` do `validacao.sql`).
  - `database/README.md`: conferido; já estava completo (migração, limpeza, roles revisão 5). Sem mudança.
  - Kanban: o plano do PO1-R foi movido para este arquivo como referência. Em `afazer.md` ficaram, deste pedido, só o PU-R e o PU-DBA. A ação 5 foi atualizada para "Escrita (PUT e POST)", depois da migração.
