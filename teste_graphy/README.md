# TesteGraphy

Programa de **teste** (console .NET 10 + Npgsql) que insere geografias WKT na coluna
`graphy` da tabela `public.mapas` (base `casaos`, servidor `192.168.168.106`), sempre com
SRID 4326, e lê cada registro de volta para conferência.

Fica isolado nesta pasta e fora de qualquer solução principal. Não cria nem altera tabelas.

## Estrutura

```
teste_graphy/
  TesteGraphy.sln
  .env.example                  somente placeholders (o programa NAO carrega .env)
  sql/validacao.sql             consultas somente leitura (DataBrain)
  sql/insert_referencia.sql     SQL oficial usado pelo programa (DataBrain)
  src/TesteGraphy/              programa console
  tests/TesteGraphy.Tests/      projeto de testes (xUnit + coverlet)
```

## Pré-requisitos

- .NET SDK 10 (`dotnet --version`).
- Acesso de rede ao servidor PostgreSQL/PostGIS.
- Um usuário de banco com o **mínimo** de privilégios: `SELECT`/`INSERT` em `public.mapas`
  (e `DELETE` só se for rodar os testes de integração). Evite usuários superuser,
  createrole ou createdb.

## Variáveis de ambiente

| Variável      | Obrigatória | Padrão            | Descrição |
|---------------|-------------|-------------------|-----------|
| `PG_HOST`     | não         | `192.168.168.106` | Servidor |
| `PG_PORT`     | não         | `5432`            | Porta (1 a 65535) |
| `PG_DATABASE` | não         | `casaos`          | Base |
| `PG_USER`     | **sim**     | (sem padrão)      | Usuário |
| `PG_PASSWORD` | **sim**     | (sem padrão)      | Senha |
| `PG_SSLMODE`  | não         | `Prefer`          | `Disable`, `Prefer`, `Require`, `VerifyCA` ou `VerifyFull` |

Credenciais são lidas **somente** de variáveis de ambiente. O programa nunca aceita senha
nem connection string por argumento e não lê credenciais de `appsettings.json` nem de
`launchSettings.json`.

> **O programa NÃO carrega o arquivo `.env` automaticamente.** O `.env.example` só documenta
> os nomes das variáveis. Se criar um `.env` local, ele é ignorado pelo `.gitignore`, mas
> **esta pasta está no OneDrive** e qualquer arquivo nela é sincronizado para a nuvem:
> não grave senhas em arquivos aqui.

### PowerShell (leitura segura da senha)

```powershell
$env:PG_HOST     = "192.168.168.106"
$env:PG_PORT     = "5432"
$env:PG_DATABASE = "casaos"
$env:PG_USER     = "<seu_usuario>"
$env:PG_SSLMODE  = "Require"

# Lê a senha sem ecoar e sem gravá-la no histórico.
$senhaSegura = Read-Host "Senha do PostgreSQL" -AsSecureString
$env:PG_PASSWORD = [System.Net.NetworkCredential]::new("", $senhaSegura).Password
Remove-Variable senhaSegura
```

Para remover a senha da sessão ao terminar: `Remove-Item Env:PG_PASSWORD`.

> **Aviso sobre o histórico do PSReadLine:** **não** digite `$env:PG_PASSWORD = "<sua_senha>"`
> com a senha literal. O PSReadLine grava os comandos em
> `(Get-PSReadLineOption).HistorySavePath`, e a senha ficaria salva em disco. Use o
> `Read-Host -AsSecureString` acima. Se já tiver digitado, apague a linha desse arquivo.

### bash (leitura segura da senha)

```bash
export PG_HOST="192.168.168.106"
export PG_PORT="5432"
export PG_DATABASE="casaos"
export PG_USER="<seu_usuario>"
export PG_SSLMODE="Require"

# Lê a senha sem ecoar e sem gravá-la no histórico.
read -s -p "Senha do PostgreSQL: " PG_PASSWORD; echo
export PG_PASSWORD
```

Para remover a senha da sessão ao terminar: `unset PG_PASSWORD`. Evite
`export PG_PASSWORD=<sua_senha>` com a senha literal, porque ela ficaria no `~/.bash_history`.

### Aviso sobre SSL (`PG_SSLMODE`)

- `Disable` e `Prefer` (padrão) **podem trafegar usuário, senha e dados sem criptografia**
  e não verificam a identidade do servidor. Nesses casos o programa exibe um aviso.
- Recomendado: `Require` (criptografa) ou, se o certificado do servidor for confiável,
  `VerifyCA` / `VerifyFull` (criptografa e verifica o servidor).
- O programa não usa `TrustServerCertificate`.
- O modo adequado depende do resultado de `SHOW ssl;` em `sql/validacao.sql` (veja abaixo).

## Como executar

Na pasta `teste_graphy/`:

```bash
# Sem argumentos: insere os 3 exemplos fixos (Praça da Sé, uma linha e um polígono em São Paulo)
dotnet run --project src/TesteGraphy

# Com argumentos: cada argumento é um WKT (máximo de 100 por execução)
dotnet run --project src/TesteGraphy -- "POINT(-46.63 -23.55)"
dotnet run --project src/TesteGraphy -- "POINT(-46.63 -23.55)" "LINESTRING(-46.63 -23.55, -46.65 -23.56)"
```

Para cada WKT o programa: valida localmente, valida no banco com `ST_IsValid` (recusa se
`false`), insere sem informar `id` (gerado pela identity), lê de volta e imprime o id, o
`ST_AsText` e o SRID (esperado `4326`). Um WKT inválido não impede os demais. Se a
**conexão** falhar, os itens seguintes são marcados como "não processados".

### Regras do WKT (validação local)

- Tipos: `POINT`, `LINESTRING`, `POLYGON`, `MULTIPOINT`, `MULTILINESTRING`, `MULTIPOLYGON`,
  `GEOMETRYCOLLECTION` (maiúsculas ou minúsculas).
- Somente 2D: `Z`, `M`, `ZM` e `EMPTY` são rejeitados. Sem prefixo `SRID=`.
- Até 10.000 caracteres e até 10 níveis de parênteses.
- Ponto como separador decimal (`-46.63`; `-46,63` é rejeitado). Sem `NaN`/`Infinity`.
- Longitude (X) entre -180 e 180; latitude (Y) entre -90 e 90.

### Códigos de saída

| Código | Significado |
|--------|-------------|
| `0` | Todos os WKT foram inseridos e conferidos |
| `1` | Configuração inválida (variável ausente ou inválida; a mensagem cita só o nome) |
| `2` | WKT inválido (local ou `ST_IsValid`) ou mais de 100 WKT na execução |
| `3` | Erro de banco ou de conexão |

As mensagens de erro nunca mostram a senha, a connection string nem o stack trace. Erros do
servidor mostram apenas `SqlState` e a mensagem. Falhas de conexão mostram só
`falha de conexao com host:porta (TipoDaExcecao)`. O WKT ecoado é truncado em 200 caracteres,
e os caracteres de controle são trocados por `?`.

### Modo verboso (log dos passos)

O arquivo `src/TesteGraphy/appsettings.json` contém **apenas** a chave `Verboso` (nunca
credenciais). Com `"Verboso": true`, cada passo é registrado na saída de erro com o prefixo
`[verboso hh:mm:ss.fff]`. As mensagens de erro (`[erro ...]`) são sempre registradas.
Recompile ou edite o `appsettings.json` copiado em `bin/.../net10.0/`.

## Validação do servidor (`sql/validacao.sql`) - executada em 2026-10-21

A validação foi executada pelo usuário em 2026-10-21, via `docker exec` e `psql` por socket
local, e interpretada pelo DataBrain e pelo SecBrain (detalhes em `tarefas/feito.md`, bloco
"F3.7 e M3.7").

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

**Repetir pela rede (depois dos pré-requisitos).** Os comandos abaixo só devem ser usados com
a role de teste (nunca superusuário) e com SSL habilitado no servidor:

```bash
# bash: com PG_USER e PG_PASSWORD já definidos (ver acima)
PGPASSWORD="$PG_PASSWORD" psql "host=192.168.168.106 dbname=casaos user=$PG_USER sslmode=require" -v ON_ERROR_STOP=1 -f sql/validacao.sql
```

```powershell
# PowerShell: com $env:PG_USER e $env:PG_PASSWORD já definidos (ver acima)
$env:PGPASSWORD = $env:PG_PASSWORD
psql "host=192.168.168.106 dbname=casaos user=$($env:PG_USER) sslmode=require" -v ON_ERROR_STOP=1 -f sql/validacao.sql
Remove-Item Env:PGPASSWORD
```

Também é possível abrir `sql/validacao.sql` em um cliente gráfico (DBeaver, pgAdmin) e
executar as consultas. Confira: PostGIS instalado, tipo/SRID da coluna `graphy`,
`is_identity` de `id`, privilégios do usuário (mínimo necessário), `SHOW ssl;` (orienta o
`PG_SSLMODE`) e `SHOW password_encryption;` (esperado `scram-sha-256`).

> Depois da migração (`database/migracao_mapas_pk.sql`, PU-DBA), `public.mapas.id` é PRIMARY KEY
> `mapas_pkey` e `GENERATED ALWAYS`; antes dela, não há PK nem `UNIQUE` em `id`. O `validacao.sql`
> mostra os dois estados nas colunas informativas `pk_exata` e `id_always`. O programa nunca informa
> `id` no INSERT e funciona igual nos dois estados.

## Build, testes e auditoria de pacotes

```bash
dotnet build TesteGraphy.sln
dotnet test TesteGraphy.sln --collect:"XPlat Code Coverage"
dotnet list TesteGraphy.sln package --vulnerable --include-transitive
```

O relatório de cobertura (Cobertura XML) fica em `tests/TesteGraphy.Tests/TestResults/<guid>/coverage.cobertura.xml`.

Resultado da última execução (2026-10-21): 394 testes, 391 aprovados, 0 falhas e 3 ignorados
(os de integração). Cobertura de linhas do projeto `TesteGraphy`: 93,99%. Nenhum pacote vulnerável.

### Só os testes unitários

```bash
dotnet test TesteGraphy.sln --filter "Category!=Integration"
```

### Testes de integração (contra o servidor real)

Os testes de integração (`tests/TesteGraphy.Tests/Dados/GraphyRepositoryIntegrationTests.cs`,
`[Trait("Category","Integration")]`) só rodam com **opt-in explícito**: é preciso definir
`PG_RUN_INTEGRATION=1` **e também** `PG_USER`/`PG_PASSWORD`. Sem isso, eles são ignorados (Skip)
e nem tentam conectar.

O que eles fazem: inserem os 3 exemplos fixos, leem cada um de volta e conferem o tipo e o
SRID 4326. Ao final (em `finally`/`DisposeAsync`), apagam **apenas** os ids devolvidos pelo
`RETURNING` e conferem que cada `DELETE` afetou exatamente 1 linha. O usuário de banco
precisa de `DELETE` em `public.mapas`.

```powershell
# PowerShell: defina PG_USER/PG_PASSWORD com leitura segura (ver acima), depois:
$env:PG_RUN_INTEGRATION = "1"
dotnet test TesteGraphy.sln --filter "Category=Integration"
Remove-Item Env:PG_RUN_INTEGRATION
```

```bash
# bash: defina PG_USER/PG_PASSWORD com read -s (ver acima), depois:
PG_RUN_INTEGRATION=1 dotnet test TesteGraphy.sln --filter "Category=Integration"
```

> **Pendente:** a execução real da integração ainda não foi feita, porque depende das
> credenciais do usuário. O mesmo vale para `sql/validacao.sql` (ver seção acima).

## Banco de dados: `public.mapas`

DDL de origem: `database/mapas.sql`. O programa **não cria nem altera** a tabela.

| Coluna   | Tipo                     | Nulo | Observação |
|----------|--------------------------|------|------------|
| `id`     | `int4` `GENERATED ALWAYS AS IDENTITY`, PRIMARY KEY `mapas_pkey` (depois da migração; antes: `GENERATED BY DEFAULT`, sem PK) | não | Gerado pelo banco. O programa nunca informa `id` no INSERT |
| `graphy` | `public.geography` (sem typmod) | sim | Coluna alvo. O SRID não é restrito na coluna; o programa força **4326** |
| `metry`  | `public.geometry`        | sim  | Não é usada por este programa |

- **PK em `id` (S3, atualizado em 2026-10-23):** a nota S3 original ("sem PK nem `UNIQUE`") vale só
  até a migração. Depois dela, `id` é PRIMARY KEY `mapas_pkey` e `GENERATED ALWAYS`: cada INSERT
  do programa gera um id novo, uma linha por id, e um `id` explícito só entra com
  `OVERRIDING SYSTEM VALUE` (manual, fora dos programas). Os testes de integração continuam
  conferindo que o `DELETE` por id afeta exatamente 1 linha; com a PK, isso fica garantido.
  Nenhuma mudança de código foi necessária (D-N11).
- SQL usado pelo programa (`sql/insert_referencia.sql`), sempre parametrizado:
  - validação: `SELECT ST_IsValid(ST_GeomFromText(@wkt, 4326)), GeometryType(ST_GeomFromText(@wkt, 4326));`
  - inserção: `INSERT INTO public.mapas (graphy) VALUES (ST_GeomFromText(@wkt, 4326)::geography) RETURNING id;`
  - leitura: `SELECT id, ST_AsText(graphy), ST_SRID(graphy::geometry) FROM public.mapas WHERE id = @id;`
  - limpeza (só nos testes de integração): `DELETE FROM public.mapas WHERE id = @id;`
- Para conferir manualmente: `SELECT id, ST_AsText(graphy), ST_SRID(graphy::geometry) FROM public.mapas ORDER BY id DESC LIMIT 10;`

## Observações operacionais

- **O1: logins falhos com senha errada.** Com a senha errada, o Npgsql pode abrir até
  **2 conexões** por tentativa (comportamento interno do driver). Assim, uma execução pode gerar **até 2 logins falhos** no log do servidor. Leve
  isso em conta se o servidor tiver bloqueio por tentativas (fail2ban, `auth_delay` ou política
  de lockout). Na primeira falha de autenticação (SqlState `28*`) o programa para e marca os
  itens seguintes como "não processados", sem tentar outra vez.
- Os dados inseridos pelo programa (fora dos testes de integração) **ficam** na tabela. Se
  quiser removê-los, use os ids que o programa imprimiu.

## Coleção Postman

Não se aplica. Este é um programa console e não expõe nenhuma API HTTP, então não há
endpoints para colocar numa coleção Postman.
