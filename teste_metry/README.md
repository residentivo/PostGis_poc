# TesteMetry

Programa de **teste** (console .NET 10 + Npgsql) que insere geometrias WKT na coluna
`metry` (`public.geometry`, sem typmod) da tabela `public.mapas` (base `casaos`, servidor
`192.168.168.106`), no SRID escolhido em `PG_SRID`, e lê cada registro de volta para
conferência.

Fica isolado nesta pasta, sem referência ao `teste_graphy/`. Não cria nem altera tabelas e
**nunca executa DELETE**.

## Estrutura

```
teste_metry/
  TesteMetry.sln
  .env.example                  somente placeholders (o programa NAO carrega .env)
  sql/validacao.sql             consultas somente leitura (DataBrain)
  sql/insert_referencia.sql     SQL oficial usado pelo programa (DataBrain)
  src/TesteMetry/               programa console
  tests/TesteMetry.Tests/       projeto de testes (xUnit + coverlet)
```

## Pré-requisitos

- .NET SDK 10 (`dotnet --version`).
- Acesso de rede ao servidor PostgreSQL/PostGIS.
- Um usuário de banco com o **mínimo** de privilégios: `SELECT`/`INSERT` em `public.mapas`
  (e `DELETE` só se for rodar os testes de integração). Não use superuser, createrole ou
  createdb (confira com `sql/validacao.sql`).

## Variáveis de ambiente

| Variável      | Obrigatória | Padrão            | Descrição |
|---------------|-------------|-------------------|-----------|
| `PG_HOST`     | não         | `192.168.168.106` | Servidor |
| `PG_PORT`     | não         | `5432`            | Porta (1 a 65535) |
| `PG_DATABASE` | não         | `casaos`          | Base |
| `PG_USER`     | **sim**     | (sem padrão)      | Usuário |
| `PG_PASSWORD` | **sim**     | (sem padrão)      | Senha (só espaços é rejeitada) |
| `PG_SSLMODE`  | não         | `Prefer` (com aviso) | `Disable`, `Prefer`, `Require`, `VerifyCA` ou `VerifyFull`. Definida vazia = erro |
| `PG_SRID`     | não         | `4326`            | `4326`, `4674`, `3857` ou `31983` (ver abaixo) |

Credenciais e SRID são lidos **somente** de variáveis de ambiente. O programa nunca aceita
senha, connection string ou SRID por argumento e não lê nada disso de `appsettings.json` nem
de `launchSettings.json`.

> **O programa NÃO carrega o arquivo `.env` automaticamente.** O `.env.example` só documenta
> os nomes das variáveis. **Esta pasta está no OneDrive** e qualquer arquivo nela é
> sincronizado para a nuvem (inclusive o histórico de versões): não grave senhas em arquivos
> aqui, nem num `.env` local.

### `PG_SRID` e faixas de coordenadas

| `PG_SRID` | Sistema | X aceito | Y aceito |
|-----------|---------|----------|----------|
| `4326` (padrão) | WGS 84 (graus) | longitude [-180, 180] | latitude [-90, 90] |
| `4674` | SIRGAS 2000 (graus) | longitude [-180, 180] | latitude [-90, 90] |
| `3857` | Web Mercator (metros) | [-20037508.3428, 20037508.3428] | [-20037508.3428, 20037508.3428] |
| `31983` | SIRGAS 2000 / UTM 23S (metros) | E [0, 1000000] | N [0, 10000000] |

- Ausente = `4326`. Definida porém vazia, ou com qualquer valor fora da lista, = código 1.
- Parsing estrito: só dígitos `0-9`, no máximo 6 caracteres, sem sinal, sem espaços, sem
  zeros à esquerda (`04326` é rejeitado), sem `0x` e sem dígitos não ASCII.
- A mensagem de erro cita só o nome da variável e os valores permitidos, nunca o valor
  recebido.
- O SRID é lido uma vez, aparece em `Destino: host:porta/base usuario=... srid=NNNN` e vai ao
  banco só como parâmetro `@srid` inteiro.
- As faixas são inclusivas e valem para **todas** as coordenadas, em todos os níveis
  (`MULTI*` e `GEOMETRYCOLLECTION` aninhada). As coordenadas do WKT devem estar **no SRID
  escolhido**: o programa não converte nada (ex.: `POINT(-5191195.21 -2698729.46)` é aceito
  em 3857 e rejeitado em 4326).

### PowerShell (leitura segura da senha)

```powershell
$env:PG_HOST     = "192.168.168.106"
$env:PG_PORT     = "5432"
$env:PG_DATABASE = "casaos"
$env:PG_USER     = "<seu_usuario>"
$env:PG_SSLMODE  = "Require"
$env:PG_SRID     = "4326"

# Lê a senha sem ecoar e sem gravá-la no histórico.
$senhaSegura = Read-Host "Senha do PostgreSQL" -AsSecureString
$env:PG_PASSWORD = [System.Net.NetworkCredential]::new("", $senhaSegura).Password
Remove-Variable senhaSegura
```

Para remover ao terminar: `Remove-Item Env:PG_PASSWORD` (e `Remove-Item Env:PG_SRID` para
voltar ao padrão).

> **Aviso sobre o histórico do PSReadLine:** **não** digite `$env:PG_PASSWORD = "<sua_senha>"`
> com a senha literal. O PSReadLine grava os comandos em
> `(Get-PSReadLineOption).HistorySavePath`, e a senha ficaria salva em disco. Use o
> `Read-Host -AsSecureString` acima. Se já tiver digitado, apague a linha desse arquivo.

### bash (leitura segura da senha)

```bash
export PG_HOST="192.168.168.106" PG_PORT="5432" PG_DATABASE="casaos"
export PG_USER="<seu_usuario>" PG_SSLMODE="Require" PG_SRID="4326"

read -s -p "Senha do PostgreSQL: " PG_PASSWORD; echo
export PG_PASSWORD
```

Para remover ao terminar: `unset PG_PASSWORD`. Evite `export PG_PASSWORD=<sua_senha>` com a
senha literal (ficaria no `~/.bash_history`).

### SSL (`PG_SSLMODE`)

- **Use `Require`** (criptografa) **ou `VerifyFull`** (criptografa e verifica o certificado e o
  nome do servidor), conforme o resultado de `SHOW ssl;` e de `pg_stat_ssl` em
  `sql/validacao.sql`.
- `Disable` e `Prefer` (padrão) **podem trafegar usuário, senha e dados sem criptografia**;
  nesses casos o programa exibe um aviso.
- O programa não usa `TrustServerCertificate` nem `Include Error Detail`.

### VS Code

`.vscode/launch.json` (na raiz do workspace) tem três entradas `TesteMetry: ...`. Usuário e
senha são pedidos a cada execução (`${input:pgUser}` e `${input:pgPassword}`, este com
`password: true`) e `PG_SRID` é escolhido numa lista com os 4 valores, ou omitido na entrada
"PG_SRID ausente = 4326". Nenhuma credencial fica gravada no arquivo.

## Como executar

Na pasta `teste_metry/`:

```bash
# Sem argumentos: insere os 3 exemplos fixos (Praça da Sé, uma linha e um polígono em
# São Paulo) já expressos no SRID de PG_SRID
dotnet run --project src/TesteMetry

# Com argumentos: cada argumento é um WKT no SRID de PG_SRID (máximo de 100 por execução)
dotnet run --project src/TesteMetry -- "POINT(-46.63 -23.55)"

# Exemplo em UTM 23S
PG_SRID=31983 dotnet run --project src/TesteMetry -- "POINT(333250 7394600)"
```

Exemplos fixos por SRID (valores projetados aproximados):

| SRID | Praça da Sé |
|------|-------------|
| 4326 / 4674 | `POINT(-46.6333 -23.5505)` |
| 3857 | `POINT(-5191195.21 -2698729.46)` |
| 31983 | `POINT(333250 7394600)` |

Para cada WKT o programa:

1. valida localmente (regras abaixo e faixas do SRID);
2. valida no banco com `ST_IsValid` e `GeometryType` (recusa se `false`, se o tipo estiver fora
   da lista, ou se o PostGIS recusar o WKT com erro de dado, ex.: `SqlState XX000`);
3. insere sem informar `id` (`ST_GeomFromText(@wkt, @srid)`, sem cast para geography);
4. lê de volta e confere se `ST_SRID(metry)` é igual a `PG_SRID` (divergência = código 2).

Um WKT inválido não impede os demais. Se a **conexão** falhar (inclusive autenticação,
SqlState `28*`/`08*`/`3D*`/`57P03`/`53300`), os itens seguintes são marcados como "não
processados".

### Regras do WKT (validação local)

- Tipos: `POINT`, `LINESTRING`, `POLYGON`, `MULTIPOINT`, `MULTILINESTRING`, `MULTIPOLYGON`,
  `GEOMETRYCOLLECTION` (maiúsculas ou minúsculas). Curvas, superfícies, `TRIANGLE`, `TIN`,
  `BOX` etc. são rejeitados.
- Caracteres aceitos: letras dos nomes dos tipos, dígitos, `.`, `-`, `+`, espaço comum,
  parênteses e vírgula. Por isso `;`, `=`, tabulação, quebras de linha, espaços Unicode (ex.:
  U+00A0) e WKB em hexadecimal são rejeitados.
- **EWKT (`SRID=...;POINT(...)`) é rejeitado**: o SRID vem só de `PG_SRID`.
- Somente 2D: `Z`, `M`, `ZM` e `EMPTY` são rejeitados.
- Até 10.000 caracteres e até 10 níveis de parênteses; LINESTRING com 2 ou mais pontos; anel
  de POLYGON fechado; nada após a geometria.
- Ponto como separador decimal (`-46.63`; `-46,63` é rejeitado). Sem `NaN`/`Infinity`.

### Códigos de saída

| Código | Significado |
|--------|-------------|
| `0` | Todos os WKT foram inseridos e conferidos |
| `1` | Configuração inválida (variável ausente ou inválida, inclusive `PG_SRID` fora da lista ou sem exemplos fixos; a mensagem cita só o nome) |
| `2` | WKT inválido (local, `ST_IsValid`, tipo ou erro de dado do PostGIS), SRID lido divergente, ou mais de 100 WKT |
| `3` | Erro de banco ou de conexão, inclusive qualquer erro do servidor ao abrir a conexão (tem prioridade sobre o código 2); nesse caso só o SqlState é exibido, sem o detalhe do servidor |

As mensagens nunca mostram a senha, a connection string nem o stack trace. Erros do servidor
mostram apenas `SqlState` e a mensagem. Falhas de conexão mostram só
`falha de conexao com host:porta (TipoDaExcecao)`. O WKT ecoado é truncado em 200 caracteres,
e os caracteres de controle são trocados por `?`.

### Modo verboso (log dos passos)

`src/TesteMetry/appsettings.json` contém **apenas** a chave `Verboso` (nunca credenciais nem
SRID). Com `"Verboso": true`, cada passo é registrado na saída de erro com o prefixo
`[verboso hh:mm:ss.fff]`, inclusive os `NOTICE` do servidor (ex.: motivo de `ST_IsValid =
false`), sanitizados. As mensagens de erro (`[erro ...]`) são sempre registradas.

## Dados inseridos: permanentes e com SRIDs misturados (REC-2)

- As linhas inseridas pelo programa **ficam na tabela**: o programa não apaga nada.
- Como a coluna `metry` não tem typmod, cada linha guarda o próprio SRID. Execuções com
  `PG_SRID` diferentes deixam a coluna com **SRIDs misturados** (4326, 4674, 3857, 31983).
  Consultas espaciais entre linhas de SRIDs diferentes falham ou exigem `ST_Transform`. Para
  ver a distribuição: `SELECT ST_SRID(metry), count(*) FROM public.mapas WHERE metry IS NOT NULL GROUP BY 1;`
- **Limpeza manual por id** (de `sql/insert_referencia.sql`), no `psql`, trocando `<ID>` e
  `<SRID>` pelos valores impressos pelo programa:

```sql
BEGIN;
SELECT id, ST_AsText(metry), ST_SRID(metry) FROM public.mapas WHERE id = <ID>;
DELETE FROM public.mapas WHERE id = <ID> AND ST_SRID(metry) = <SRID>;
-- saída esperada: DELETE 1
COMMIT;      -- se DELETE 1
-- ROLLBACK; -- se DELETE 0 ou maior que 1
```

> Antes da migração (`database/migracao_mapas_pk.sql`, PU-DBA), `public.mapas` não tem PK nem `UNIQUE`
> em `id`; por isso a confirmação de `DELETE 1`. Depois dela, `id` é PRIMARY KEY `mapas_pkey` e
> `GENERATED ALWAYS` (nota S3 atualizada em 2026-10-23), e o `DELETE 1` fica garantido. A confirmação
> continua no roteiro. O `validacao.sql` mostra o estado nas colunas `pk_exata` e `id_always`.

## Banco de dados: `public.mapas`

DDL de origem: `database/mapas.sql`. O programa **não cria nem altera** a tabela.

| Coluna   | Tipo | Nulo | Observação |
|----------|------|------|------------|
| `id`     | `int4` `GENERATED ALWAYS AS IDENTITY`, PRIMARY KEY `mapas_pkey` (depois da migração; antes: `GENERATED BY DEFAULT`, sem PK) | não | Gerado pelo banco; o programa nunca informa `id` e funciona igual nos dois estados (D-N11) |
| `graphy` | `public.geography` | sim | Não é usada por este programa |
| `metry`  | `public.geometry` (sem typmod) | sim | Coluna alvo; SRID definido por `PG_SRID` |

SQL usado pelo programa (`sql/insert_referencia.sql`), sempre parametrizado (`@wkt` text,
`@srid` e `@id` integer):

- validação: `SELECT ST_IsValid(ST_GeomFromText(@wkt, @srid)) AS valido, GeometryType(ST_GeomFromText(@wkt, @srid)) AS tipo;`
- inserção: `INSERT INTO public.mapas (metry) VALUES (ST_GeomFromText(@wkt, @srid)) RETURNING id;`
- leitura: `SELECT id, ST_AsText(metry) AS wkt, ST_SRID(metry) AS srid FROM public.mapas WHERE id = @id;`

## Validação do servidor (`sql/validacao.sql`) - executada em 2026-10-21

A validação foi executada pelo usuário em 2026-10-21, via `docker exec` e `psql` por socket
local, e interpretada pelo DataBrain e pelo SecBrain (detalhes em `tarefas/feito.md`, bloco
"F3.7 e M3.7"). Além do resumo abaixo, `ST_Transform` dos exemplos fixos bate com
`ExemplosFixos` (D-M8), e `metry` tinha 3 linhas, todas com SRID 4326.

**Resultado (resumo)**

- **OK**
  - Versões: PostGIS 3.5.2, PostgreSQL 17, GEOS 3.9.0, PROJ 7.2.1.
  - `graphy`: `geography` sem typmod (srid 0).
  - `metry`: `geometry` sem typmod (2D, srid 0).
  - `id`: integer identity BY DEFAULT.
  - Sem constraints e sem triggers.
  - Os SRIDs 4326, 4674, 3857 e 31983 existem em `spatial_ref_sys`.
  - `password_encryption` = `scram-sha-256`.
- **Diverge**
  - A role usada na validação é superusuária, por isso os privilégios exibidos não provam
    privilégio mínimo. Programas e testes **não** devem usar essa role.
  - `ssl = off` no servidor.
  - O `pg_hba` não foi verificado.
- **Ressalva:** privilégio mínimo e TLS pela rede **não** foram validados. Os blocos de
  privilégios devem ser repetidos com a role de teste (`mapas_teste`), conectando pela rede,
  depois das ações do DBA.

> **Atenção:** enquanto o servidor estiver com `ssl = off`, qualquer conexão com
> `sslmode=require` (no `psql`) ou `PG_SSLMODE=Require` (no programa) **falha**. Hoje, a
> validação deve ser feita **pelo DBA, dentro do servidor**, com `docker exec` e conexão local
> por socket (sem senha pela rede). Exemplo, com placeholders:
>
> ```bash
> docker exec -i <container_postgres> psql -U <role_do_dba> -d casaos -v ON_ERROR_STOP=1 < sql/validacao.sql
> ```
>
> Os pré-requisitos para voltar a validar e testar pela rede estão em `tarefas/afazer.md`, na
> seção "Ações do usuário/DBA" e na exceção `PG_SSLMODE=Disable`, que depende de decisão do
> usuário. Entre eles: role `mapas_teste` não superusuária, senha rotacionada, `pg_hba`
> restrito e SSL habilitado.

**Repetir pela rede (depois dos pré-requisitos).** A forma `"host=... dbname=casaos user=...
sslmode=require"` faz o `psql` também exigir SSL (o padrão do `psql` é `prefer`, que cairia
para texto claro). Use só com a role de teste e com SSL habilitado no servidor:

```powershell
# PowerShell: com $env:PG_USER e $env:PG_PASSWORD já definidos (ver acima)
$env:PGPASSWORD = $env:PG_PASSWORD
psql "host=192.168.168.106 dbname=casaos user=$($env:PG_USER) sslmode=require" -v ON_ERROR_STOP=1 -f sql/validacao.sql
Remove-Item Env:PGPASSWORD
```

```bash
# bash: com PG_USER e PG_PASSWORD já definidos (ver acima)
PGPASSWORD="$PG_PASSWORD" psql "host=192.168.168.106 dbname=casaos user=$PG_USER sslmode=require" -v ON_ERROR_STOP=1 -f sql/validacao.sql
```

A mesma forma de conexão vale para a limpeza manual por id (seção REC-2 acima) e tem a mesma
restrição: hoje ela falha por causa do `ssl = off`.

Confira: PostGIS e versão, `metry` em `geometry_columns`, os 4 SRIDs em `spatial_ref_sys`,
privilégios e atributos da role (não superuser), `SHOW ssl`, `pg_stat_ssl` e
`password_encryption`.

## Build, testes e auditoria de pacotes

```bash
dotnet build TesteMetry.sln
dotnet test TesteMetry.sln --collect:"XPlat Code Coverage"
dotnet list TesteMetry.sln package --vulnerable --include-transitive
```

O relatório de cobertura (Cobertura XML) fica em
`tests/TesteMetry.Tests/TestResults/<guid>/coverage.cobertura.xml`.

Resultado da última execução (2026-10-21, após as melhorias MF-1 a MF-4): 881 testes
unitários, 881 aprovados e 0 falhas; os de integração ficam ignorados sem
`PG_RUN_INTEGRATION=1`. Cobertura do projeto `TesteMetry`: linhas 98,64%, ramos 95,22%.
Abaixo de 100%: `Program.cs` (só o caminho sem credenciais é testado) e `MetryRepository.cs`
(o `catch` de `SocketException`/`TimeoutException`, mantido como defesa em profundidade).
`WktValidator.cs`: o ramo de número não finito, alcançável só por estouro de expoente (ex.:
`1e999`), é coberto por testes.

### Só os testes unitários

Não conectam ao servidor (usam fakes e um servidor Postgres falso local,
`tests/TesteMetry.Tests/Fakes/ServidorPostgresFalso.cs`). O servidor falso tem estes modos:
erro na abertura da conexão (`ComErro`), erro na execução do comando (`ComErroNaExecucao`),
consulta sem linhas (`SemLinhas`) e com linhas (`ComLinhas`). Ele também registra os comandos
recebidos (`ComandosRecebidos`), o que permite cobrir a `FalhaBancoException` da execução sem
banco real. Observação: o Npgsql remove o `;` final do SQL enviado.

```bash
dotnet test TesteMetry.sln --filter "Category!=Integration"
```

### Cobertura

```bash
dotnet test TesteMetry.sln --collect:"XPlat Code Coverage"
```

Abra o `coverage.cobertura.xml` gerado em `tests/TesteMetry.Tests/TestResults/<guid>/` (ou
use uma ferramenta como o ReportGenerator, se instalada). A meta do projeto é 80% de linhas.

### Testes de integração (contra o servidor real)

Os testes de integração (`tests/TesteMetry.Tests/Dados/MetryRepositoryIntegrationTests.cs`,
`[Trait("Category","Integration")]`) só rodam com **opt-in explícito**: é preciso definir
`PG_RUN_INTEGRATION=1` **e também** `PG_USER`/`PG_PASSWORD`. Sem isso, eles são ignorados
(Skip) e nem tentam conectar.

O que eles fazem, para cada SRID permitido (4326, 4674, 3857 e 31983, independentemente de
`PG_SRID`): inserem os 3 exemplos fixos daquele SRID, leem cada um de volta e conferem o tipo
e `ST_SRID`. Ao final (`DisposeAsync`), apagam **apenas** os ids devolvidos pelo `RETURNING`,
cada um numa transação própria com `WHERE id = @id AND ST_SRID(metry) = @srid`: se o `DELETE`
afetar exatamente 1 linha, `COMMIT`; caso contrário, `ROLLBACK` e o teste falha. O usuário de
banco precisa de `DELETE` em `public.mapas` e **não** deve ser superusuário (confira com
`sql/validacao.sql`). Use `PG_SSLMODE=Require` ou `VerifyFull`.

```powershell
# PowerShell: defina PG_USER/PG_PASSWORD com leitura segura (ver acima), depois:
$env:PG_SSLMODE = "Require"
$env:PG_RUN_INTEGRATION = "1"
dotnet test TesteMetry.sln --filter "Category=Integration"
Remove-Item Env:PG_RUN_INTEGRATION
```

```bash
# bash: defina PG_USER/PG_PASSWORD com read -s (ver acima), depois:
PG_SSLMODE=Require PG_RUN_INTEGRATION=1 dotnet test TesteMetry.sln --filter "Category=Integration"
```

> **Pendente:** a execução real da integração ainda não foi feita, porque depende das
> credenciais do usuário. O mesmo vale para `sql/validacao.sql` (ver seção acima).

## Observações operacionais

- Com a senha errada, o Npgsql pode abrir até **2 conexões** por tentativa; uma execução pode
  gerar até 2 logins falhos no log do servidor. Na primeira falha de autenticação o programa
  para e marca os itens seguintes como "não processados".
- Qualquer erro do servidor na abertura da conexão (autenticação, permissão, limite de conexões,
  servidor indisponível ou erro interno) também interrompe os itens seguintes, marcados como
  "não processados" (código 3). Nesse caso só o `SqlState` é exibido.

## Coleção Postman

Não se aplica: programa console, sem API HTTP.

## Manual de telas

Não se aplica: programa console, sem interface gráfica. O passo a passo de uso está em
"Variáveis de ambiente" e "Como executar".
