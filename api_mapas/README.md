# api_mapas — geometrias de `public.mapas` por id (graphy e metry)

> **ATENÇÃO: a API NÃO tem autenticação. Uso SOMENTE LOCAL.**
> Ela só sobe ouvindo em loopback (`localhost`, `127.0.0.1`, `[::1]`). Se `ASPNETCORE_URLS`/`--urls`,
> `HTTP_PORTS`/`HTTPS_PORTS` ou `Kestrel:Endpoints` apontarem para `0.0.0.0`, `*`, `+`, `[::]`, IP de LAN
> ou outro nome de host, a API **recusa subir** com mensagem clara. Não exponha a porta na rede.

> **Pré-requisito (2026-10-23): migração de `public.mapas` para PRIMARY KEY.** Esta versão trata **uma
> geometria por id**: `id` é PRIMARY KEY `mapas_pkey` e `GENERATED ALWAYS`. Antes de usá-la, o usuário/DBA
> roda a migração na ordem de `database/README.md`, "Migração para PRIMARY KEY (PU-DBA)". Sem a migração:
> - a escrita (PUT e POST) fica **desligada** (motivo `SemChavePrimaria` no log de início);
> - o **GET** responde `500` para qualquer id que ainda tenha 2 ou mais linhas (RS-23).
>
> A migração é pré-requisito também da leitura.

API REST ASP.NET Core (.NET 10, DDD, Npgsql) com leitura (GET), atualização (PUT) e criação (POST) de
geometrias em `public.mapas`:

| Rota | O que faz | Conexão |
|------|-----------|---------|
| `GET /api/mapas/{id}/graphy` | lê a geometria de `graphy` do id (`geography`, lida como `graphy::geometry`) | leitura (`PG_USER`) |
| `GET /api/mapas/{id}/metry`  | lê a geometria de `metry` do id (`geometry`) | leitura (`PG_USER`) |
| `PUT /api/mapas/{id}/graphy` | grava `graphy` na linha do id, que já precisa existir (só SRID 4326) | escrita (`PG_ESCRITA_USER`), opcional |
| `PUT /api/mapas/{id}/metry`  | grava `metry` na linha do id, que já precisa existir (SRIDs 4326, 4674, 3857, 31983) | escrita, opcional |
| `POST /api/mapas/graphy` | cria um id novo (sequência) com `graphy` preenchida e `metry` NULL | escrita, opcional |
| `POST /api/mapas/metry`  | cria um id novo (sequência) com `metry` preenchida e `graphy` NULL | escrita, opcional |

As consultas são as constantes Q1/Q2 (GET), W1/W2 (PUT), W3/W4 (POST) e V1 (checagem da role de escrita) de
[`sql/consultas_referencia.sql`](sql/consultas_referencia.sql) (DataBrain), sempre parametrizadas.

> **Escrita sem autenticação (decisão U-1 do usuário).** PUT e POST não têm autenticação. Isso só é aceitável
> enquanto **todas** estas condições valerem:
> 1. a máquina onde a API roda tem **um único usuário**;
> 2. a API **nunca** é exposta por proxy reverso, túnel (ngrok, cloudflared, SSH `-R` etc.) ou port-forward;
> 3. RP-12 cumprido: só `Content-Type: application/json` (charset opcional, apenas `utf-8`), sem CORS, sem
>    method override, POST só nas rotas `/api/mapas/graphy` e `/api/mapas/metry`, `AllowedHosts` restrito
>    e 403 para requisições de navegador de outra origem (ver "403 de origem");
> 4. RP-13 cumprido: log de auditoria de cada PUT e POST mantido e consultado;
> 5. RP-16 cumprido: backup de `public.mapas` feito **antes** de ligar a escrita.
>
> Se qualquer condição deixar de valer, desligue a escrita (remova `PG_ESCRITA_*`) até haver autenticação.
> Os riscos residuais aceitos pelo usuário estão em "Riscos aceitos".

## Estrutura

```
api_mapas/
  ApiMapas.sln
  src/ApiMapas.Dominio          ColunaMapa, IdMapa (validação do id), LeituraMapa, IMapasRepository,
                                Geometrias/ (WktValidator, LimitesSrid, GeometriaEntrada), IMapasEscritaRepository
  src/ApiMapas.Aplicacao        ConsultaGeometriasService (GET), AtualizacaoGeometriasService (PUT),
                                CriacaoGeometriasService (POST), EntradaGeometria, DTOs, limites
  src/ApiMapas.Infraestrutura   ConexaoConfig (PG_*), ConexaoEscritaConfig (PG_ESCRITA_*), MapasRepository,
                                MapasEscritaRepository, VerificadorRoleEscrita (Npgsql, SQL constante)
  src/ApiMapas.Api              Program, endpoints, LeitorCorpoHttp, OrigemEscritaMiddleware, ProblemDetails,
                                cabeçalhos, rate limiting
  tests/ApiMapas.Tests          xUnit (TestBrain)
  postman/api_mapas.postman_collection.json         GET e PUT (coleção principal)
  postman/api_mapas_post.postman_collection.json    POST (coleção separada: cria linhas)
  sql/consultas_referencia.sql
```

## Pré-requisitos

- .NET SDK 10 (`dotnet --list-sdks`).
- PostgreSQL com PostGIS, `public.mapas` **migrada** (PK `mapas_pkey`, `id` `GENERATED ALWAYS`; ver o aviso
  no início) e a role **somente leitura** `api_mapas_leitura` (fonte oficial: `database/roles.sql`).
- Para a escrita (opcional): a role `api_mapas_escrita` na **revisão 5** do `database/roles.sql`, o backup e o
  procedimento do DBA (`database/README.md`, passos 8 e 9 da migração).

## Variáveis de ambiente (credenciais SOMENTE pelo shell)

| Variável | Obrigatória | Padrão | Observação |
|----------|-------------|--------|------------|
| `PG_USER` | sim | — | sem ela a API não sobe |
| `PG_PASSWORD` | sim | — | sem ela a API não sobe; nunca é logada |
| `PG_HOST` | não | `192.168.168.106` | |
| `PG_PORT` | não | `5432` | 1..65535 |
| `PG_DATABASE` | não | `casaos` | |
| `PG_SSLMODE` | não | `Require` | só `Disable`, `Prefer`, `Require`, `VerifyCA`, `VerifyFull`; outro valor impede subir; `Disable`/`Prefer` geram warning no log e desligam a escrita |
| `PG_ESCRITA_USER` | não (liga PUT e POST) | — | role de escrita (`api_mapas_escrita`); defina junto com `PG_ESCRITA_PASSWORD` |
| `PG_ESCRITA_PASSWORD` | não (liga PUT e POST) | — | senha da role de escrita; nunca é logada |

### Escrita (PUT e POST) — conexão separada

PUT e POST usam uma **segunda conexão**, só deles: `PG_ESCRITA_USER`/`PG_ESCRITA_PASSWORD`. Host, porta, base e
`PG_SSLMODE` são os mesmos da leitura. O GET continua só na conexão de leitura.

> **Pré-requisito (U-4 / RP-16):** faça o backup de `public.mapas` (`database/backup_mapas.sh` ou
> `database/backup_mapas.ps1`) e siga a migração em `database/README.md` **antes** de definir `PG_ESCRITA_*`.
> Sem destino informado, cada execução do backup cria uma pasta datada nova,
> `~/backups_postgis/AAAAMMDD_HHMMSS` (fora do OneDrive e de repositórios git). Confira `Backup OK`, a contagem
> de linhas e o SHA-256 antes de continuar.

A API **sobe com a escrita desligada** quando ocorre qualquer um dos casos abaixo. O log de início mostra
"Escrita (PUT e POST) DESLIGADA" e o motivo em Warning. Nesse estado, o **PUT responde `405`** (a rota GET
existe) e o **POST responde `404`** (a rota não existe; RS-21).
- as duas variáveis `PG_ESCRITA_*` estão ausentes;
- `PG_SSLMODE` efetivo é `Disable` ou `Prefer`;
- a role de escrita tem privilégio demais (checagem V1, feita uma única vez na subida; motivo
  `RolePrivilegiada`). Basta um destes itens:
  - superusuário, `BYPASSRLS`, `CREATEROLE`, `CREATEDB` ou `REPLICATION`;
  - `TRUNCATE` ou `DELETE` em `public.mapas`;
  - `INSERT` ou `UPDATE` na coluna `id`;
  - membro de qualquer outra role (inclusive `pg_write_all_data`);
  - dona da tabela `public.mapas`;
- `public.mapas` não tem a PK exata em `id` ou `id` não é `GENERATED ALWAYS`, ou seja, a migração não foi
  feita (motivo `SemChavePrimaria`);
- o banco não responde à checagem, ou a checagem lança qualquer exceção (log 4011).

Em todos esses casos a API continua de pé, o GET continua funcionando e o pool de escrita é limpo
(`DataSource.Clear()`).

**Tempo de subida:** com `PG_ESCRITA_*` definidas, a subida pode esperar **até 15 s** pela checagem V1. Com o
banco fora do ar, a API sobe depois disso com a escrita desligada.

A API **não sobe** (código de saída 1; a mensagem cita só o nome da variável) quando:
- só uma das duas variáveis `PG_ESCRITA_*` está definida;
- `PG_ESCRITA_USER` é igual a `PG_USER` (depois do `Trim`, sem diferenciar maiúsculas);
- `PG_ESCRITA_PASSWORD` é igual a `PG_PASSWORD`.

Configuração da conexão de escrita:
- `Timeout` 10 s, `CommandTimeout` 15 s e `Maximum Pool Size` 5;
- `ApplicationName=ApiMapas.Escrita` e `Persist Security Info=false`;
- sem `IncludeErrorDetail` e sem `TrustServerCertificate`.

O log mostra só `host:porta/base ssl=... (escrita)`, nunca usuário ou senha. Com a escrita habilitada, o log
de início diz "Escrita (PUT e POST) HABILITADA" e avisa em Warning que **há escrita sem autenticação**.

PowerShell (a senha é lida sem eco):

```powershell
$env:PG_ESCRITA_USER = "api_mapas_escrita"
$env:PG_ESCRITA_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
  [Runtime.InteropServices.Marshal]::SecureStringToBSTR((Read-Host "Senha de escrita" -AsSecureString)))
# ao terminar: Remove-Item Env:PG_ESCRITA_PASSWORD
```

Bash:

```bash
export PG_ESCRITA_USER=api_mapas_escrita
read -rs -p "Senha de escrita: " PG_ESCRITA_PASSWORD; echo; export PG_ESCRITA_PASSWORD
# ao terminar: unset PG_ESCRITA_PASSWORD
```

No VS Code, use a configuração "ApiMapas: API com escrita PUT". Ela liga PUT e POST e pede a senha de escrita
como `password`, sem valor padrão.

**Não** grave credenciais em `appsettings*.json`, `launchSettings.json`, `.env` ou nas coleções Postman.
O `.env.example` contém apenas placeholders e a API não lê arquivos `.env`. A pasta está no OneDrive, e
qualquer arquivo com senha seria sincronizado (ver alerta RQ-9 no Kanban).

Configuração da conexão de leitura:
- `Timeout` 10 s, `CommandTimeout` 15 s e `Maximum Pool Size` 20;
- `ApplicationName=ApiMapas` e `Persist Security Info=false`;
- sem `IncludeErrorDetail`.

O log mostra só `host:porta/base ssl=...`.

> **TLS:** `Require` cifra a conexão, mas **não valida o certificado** do servidor (sujeito a MITM).
> Quando o PostgreSQL tiver certificado, use `PG_SSLMODE=VerifyFull` e confie na CA do servidor
> (Root Certificate: arquivo `root.crt` da CA, ex.: via `PGSSLROOTCERT` ou no repositório de certificados do SO).

> **Situação do servidor (2026-10-22):** o usuário ligou o SSL do servidor 192.168.168.106 e testou a API com
> `PG_SSLMODE=Require`, que é o padrão. Não use `Prefer` nem `Disable`: além de cair para texto claro, eles
> desligam a escrita (RP-7). As pendências restantes do servidor (migração, `pg_hba`, backup, versão) estão
> em `tarefas/afazer.md`.

## Como subir

PowerShell (a senha é lida sem eco e fica só na sessão):

```powershell
cd api_mapas
$env:PG_USER = "api_mapas_leitura"
$env:PG_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
  [Runtime.InteropServices.Marshal]::SecureStringToBSTR((Read-Host "Senha" -AsSecureString)))
$env:PG_SSLMODE = "Require"
dotnet run --project src/ApiMapas.Api
# ao terminar: Remove-Item Env:PG_PASSWORD
```

Bash (Git Bash / Linux):

```bash
cd api_mapas
export PG_USER=api_mapas_leitura
read -rs -p "Senha: " PG_PASSWORD; echo; export PG_PASSWORD
dotnet run --project src/ApiMapas.Api
# ao terminar: unset PG_PASSWORD
```

A API ouve em **http://localhost:5080**, pelo perfil `http` do `launchSettings.json`, com
`ASPNETCORE_ENVIRONMENT=Production`. O perfil `https` acrescenta `https://localhost:7080` e exige o
certificado de desenvolvimento. Para outra porta, use `ASPNETCORE_URLS=http://localhost:5090` (só loopback).

Unix socket: no **Windows** use o formato `http://unix:///caminho/api.sock`
(ex.: `ASPNETCORE_URLS=http://unix:///C:/temp/api.sock`). O formato `http://unix:/tmp/api.sock`
gera `FormatException` e a API recusa subir.

Sem `PG_USER`/`PG_PASSWORD`, a saída é, por exemplo:

```
[ApiMapas] A API nao foi iniciada. Configuracao invalida: PG_USER e obrigatoria e nao foi definida (defina no shell antes de subir a API).
```

## appsettings.json (sem segredos)

| Chave | Padrão | Efeito |
|-------|--------|--------|
| `Verboso` | `false` | `true` liga os logs de passo (nível Debug) das categorias `ApiMapas.*`. Também por linha de comando: `dotnet run --project src/ApiMapas.Api -- --Verboso=true` |
| `ApiMapas:LimiteBytesGeometrias` | `5242880` (5 MB) | teto de bytes UTF-8 de `wkt` + `geojson` na resposta do GET; acima dele, `500` com log Warning |
| `ApiMapas:LimiteRequisicoesPorMinuto` | `60` | rate limiting por IP em todas as rotas (janela fixa de 1 min); excedente recebe `429` |
| `ApiMapas:LimiteBytesCorpoPut` | `65536` (64 KiB) | teto do corpo do **PUT e do POST**, `1..1048576`; acima, `413`. O nome da chave foi mantido |
| `ApiMapas:LimiteEscritasPorMinuto` | `10` | `1..60`. **Uma chave só controla dois limites:** escritas (PUT + POST) por minuto **por IP** e escritas por minuto **na API toda** (ver "Limitadores") |
| `AllowedHosts` | `localhost;127.0.0.1;[::1]` | outros cabeçalhos `Host` recebem 400 |

Valor inválido em qualquer chave impede a API de subir (mensagem cita só o nome da chave).
Fixos no código:
- no máximo **3 escritas simultâneas** (PUT + POST) na API toda;
- **10.000 caracteres** por WKT;
- prazo total de **30 s** por PUT ou POST (estouro: ROLLBACK e `500` genérico).

## Limitadores (429)

Todas as rotas passam pelo limite **por IP** de `LimiteRequisicoesPorMinuto` (60/min). PUT e POST passam
também por três limitadores de escrita, **compartilhados** entre os dois métodos. A ordem na cadeia é esta:

| Ordem | Limitador | Limite | Vale para |
|-------|-----------|--------|-----------|
| 1 | escritas simultâneas | 3 na API toda, sem fila | PUT + POST |
| 2 | global por IP | `LimiteRequisicoesPorMinuto` (60/min) | todas as rotas |
| 3 | escritas por IP | `LimiteEscritasPorMinuto` (10/min) | PUT + POST |
| 4 | escritas na API toda | `LimiteEscritasPorMinuto` (10/min), sem partição | PUT + POST |

- O encadeamento para no primeiro limitador que recusa. Por isso, uma escrita recusada por **simultaneidade**
  não gasta as cotas por minuto (correção do achado 2 do PO5).
- O `429` traz `Retry-After` quando o limitador informa o tempo (janelas por minuto). O `429` por
  simultaneidade não traz `Retry-After`: tente de novo em alguns segundos.
- **Uma chave para dois limites (achado 1 do PO5).** `LimiteEscritasPorMinuto` define ao mesmo tempo o
  limite por IP e o limite da API toda. Como a API só ouve em loopback, na prática há um IP só, e o valor é o
  total de escritas por minuto. Não há como ter 10 por IP e um total maior.
- **Limitação conhecida (PO6-1, aceita, sem correção).** Uma escrita recusada pela cota **por minuto** de
  escrita (limitadores 3 ou 4) gasta **2 permissões** do limitador global por IP (60/min). O middleware de
  rate limiting tenta adquirir a cadeia duas vezes, e o limitador 2 vem antes dos de escrita. Inverter a
  ordem só inverteria o problema. Na prática, um cliente que insiste em escrever acima da cota consome a
  própria cota de GET duas vezes mais rápido: 30 escritas recusadas esgotam os 60/min daquele IP até a
  janela virar.
- **403 de origem não consome nenhum limite**: ele é decidido antes do rate limiter.

### Crescimento da tabela e do disco (RS-5)

Cada POST cria uma linha nova, e nada na API apaga linhas. O WKT tem no máximo 10.000 caracteres, o que dá
até cerca de 2.500 vértices, ou cerca de **40 KB por linha** no pior caso (antes da compressão TOAST). Pior
caso de crescimento:

| `LimiteEscritasPorMinuto` | Linhas/hora | Disco/hora (pior caso) | Disco/dia (pior caso) |
|---------------------------|-------------|------------------------|-----------------------|
| `10` (padrão) | 600 | ~24 MB | ~576 MB |
| `60` (teto) | 3.600 | ~144 MB | ~3,5 GB |

Como o limite também vale para a API toda, esses números são o máximo da API, não por cliente. Com pontos e
geometrias pequenas, cada linha fica em centenas de bytes. Acompanhe o tamanho com
`SELECT pg_size_pretty(pg_total_relation_size('public.mapas'));`.

## 403 de origem (RS-2 / RS-22)

PUT e POST respondem **`403 Origem não permitida`** quando a requisição parece vir de um navegador de outra
origem. Basta uma destas condições:
- o cabeçalho `Origin` está presente, com **qualquer** valor (inclusive vazio ou `null`);
- `Sec-Fetch-Site` aparece mais de uma vez, ou com uma lista separada por vírgula;
- `Sec-Fetch-Site` vem com valor diferente de `none` ou `same-origin` (comparado depois do trim, sem
  diferenciar maiúsculas). Um valor vazio também cai aqui.

Comportamento:
- o corpo não é lido, os cabeçalhos não são ecoados e o `instance` é omitido;
- o log é Debug (4012), sem os valores dos cabeçalhos;
- o GET não passa por essa regra.

`curl`, Postman (app desktop e CLI) e os testes não mandam `Origin` por padrão e passam normalmente.

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4", "title": "Origem não permitida", "status": 403,
  "detail": "Escrita aceita somente sem Origin e com Sec-Fetch-Site ausente, none ou same-origin.", "traceId": "00-...-00" }
```

## Contrato do GET (uma geometria por id)

### `id`
Inteiro decimal `1..2147483647`, só dígitos ASCII, sem sinal/espaço, até 10 caracteres.
`abc`, `0`, `-1`, `1.5`, `+1`, `2147483648`, `12345678901`, `1'` → **400** sem consultar o banco.

### 200 (`application/json`)

```json
{
  "id": 1,
  "coluna": "graphy",
  "tipo": "POINT",
  "srid": 4326,
  "wkt": "POINT(-46.633309 -23.55052)",
  "geojson": { "type": "Point", "coordinates": [-46.633309, -23.55052] },
  "x": -46.633309,
  "y": -23.55052,
  "longitude": -46.633309,
  "latitude": -23.55052
}
```

- Objeto único (Q2), sem lista. Os campos `quantidade` e `truncado` deixaram de existir.
- `wkt`, `x`, `y`: SRID nativo. `geojson`, `longitude`, `latitude`: WGS84 (EPSG:4326). SRID 0 → `geojson` nativo e `longitude`/`latitude` nulos.
- `x`/`y`/`longitude`/`latitude` só para `POINT`; nos demais tipos vêm `null` (coordenadas no `geojson`).

### Erros (`application/problem+json`)

| Status | `title` | Quando |
|--------|---------|--------|
| 400 | `Id inválido` | id fora da regra (o valor enviado não é ecoado; `instance` = `/api/mapas/{id}/graphy` ou `/metry`) |
| 404 | `Geometria não encontrada` | o id não tem linha, **ou** a linha existe mas a coluna pedida é NULL (ex.: id criado por `POST .../metry` e lido em `.../graphy`) |
| 404 / 405 | `Not Found` / `Method Not Allowed` | rota desconhecida / método não mapeado (PATCH, DELETE, POST na rota com id; PUT quando a escrita está desligada) |
| 429 | `Muitas requisições` | rate limiting (ver "Limitadores") |
| 500 | `Erro interno` | erro de banco/timeout/inesperado; **id com 2 ou mais linhas** (tabela não migrada; log Error 1004); geometria acima de `LimiteBytesGeometrias` (log Warning 1005). Corpo genérico com `traceId`, detalhe só no log |

Exemplos de corpo de erro:

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1", "title": "Id inválido", "status": 400,
  "detail": "O id deve ser um inteiro entre 1 e 2147483647.", "instance": "/api/mapas/{id}/graphy", "traceId": "00-...-00" }
```

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Geometria não encontrada", "status": 404,
  "detail": "Nenhuma geometria encontrada na coluna 'graphy' para o id 99.", "instance": "/api/mapas/99/graphy", "traceId": "00-...-00" }
```

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1", "title": "Erro interno", "status": 500,
  "detail": "Não foi possível processar a requisição. Informe o traceId ao suporte.", "traceId": "00-...-00" }
```

O corpo nunca traz SQL, SqlState, stack, host, usuário ou texto do Npgsql. Para investigar um 500, procure o
`traceId` no log do console.

Todas as respostas levam `X-Content-Type-Options: nosniff` e `Cache-Control: no-store`; não há header
`Server` nem CORS.

## Corpo de escrita (PUT e POST)

Objeto único com **uma** geometria (Q1):

```json
{ "wkt": "POINT(-46.633309 -23.55052)", "srid": 4326 }
```

- `Content-Type: application/json`, comparado sem diferenciar maiúsculas (`APPLICATION/JSON; CHARSET=UTF-8`
  também vale). O `charset` é opcional e, se vier, só `utf-8`.
- Respondem `415`:
  - `text/plain`, `application/x-www-form-urlencoded`, `multipart/form-data` e `charset=latin1`;
  - Content-Type ausente, com lista separada por vírgula ou repetido.
- Corpo de até `LimiteBytesCorpoPut` (64 KiB); acima, `413`, inclusive sem `Content-Length`.
- Nomes exatamente `wkt` e `srid` (sensíveis a maiúsculas), os dois obrigatórios. Respondem
  `400 Corpo inválido`:
  - campo desconhecido ou duplicado;
  - `srid` em texto (`"4326"`);
  - profundidade acima de 8;
  - o **formato antigo** `{"geometrias":[...]}`.
- WKT com até **10.000** caracteres.
- SRID por coluna: `graphy` aceita só **4326**; `metry` aceita **4326, 4674, 3857 e 31983**. O SRID é
  conferido antes das coordenadas. Faixa de cada SRID:
  - 4326 e 4674: longitude [-180, 180] e latitude [-90, 90];
  - 3857: ±20037508.3428;
  - 31983: E [0, 1000000] e N [0, 10000000].
- Tipos aceitos: `POINT`, `LINESTRING`, `POLYGON`, `MULTIPOINT`, `MULTILINESTRING`, `MULTIPOLYGON` e
  `GEOMETRYCOLLECTION`. Restrições:
  - só 2D (sem Z/M/ZM);
  - sem `EMPTY`, sem EWKT (`SRID=...;`) e sem HEX/WKB;
  - só letras dos tipos, dígitos, `.`, `-`, `+`, espaço, parênteses e vírgula.
- Depois da validação local, o banco confere `ST_IsValid` e o `GeometryType`. Se recusar, a resposta é
  `400 Geometria inválida` e nada é gravado.
- `400 Geometria inválida` traz no `detail` só o motivo fixo, sem índice e nunca com o WKT. Exemplos:
  `geometria inválida segundo o PostGIS (ST_IsValid)`, `tipo devolvido pelo banco diverge do WKT`,
  `srid não permitido para a coluna 'graphy' (aceitos: 4326)`.
- O corpo é lido pelo handler (`LeitorCorpoHttp`), nunca por binding automático. Method override
  (`X-HTTP-Method-Override`, `X-HTTP-Method`, `X-Method-Override`) é **ignorado**: um POST com override `PUT`
  ou `DELETE` continua sendo só um INSERT.

## Contrato do PUT (grava a coluna do id)

`PUT /api/mapas/{id}/graphy` e `PUT /api/mapas/{id}/metry` gravam a geometria enviada na coluna da rota, na
linha do id. Sucesso: **`204 No Content`**, sem corpo; para conferir, faça o GET da coluna.

- O PUT só faz `UPDATE ... WHERE id = @id` (PK), com `lock_timeout` de 5 s. **Nunca cria id e nunca apaga
  linha.** A outra coluna da linha não muda.
- O PUT **pode preencher a outra coluna** de uma linha criada por POST (Q3). Exemplo: um POST de `metry` criou
  o id 10, com `graphy` NULL; um `PUT /api/mapas/10/graphy` preenche `graphy` nessa mesma linha.
- Não existe mais lista nem lista vazia: o PUT não limpa coluna (Q4). Limpar ou apagar fica para um pedido
  futuro.
- O PUT é idempotente: repetir o mesmo PUT chega ao mesmo estado.

### Ordem das verificações (PUT)

A primeira falha responde:
1. origem → `403`;
2. limitadores → `429`;
3. id → `400 Id inválido` (o corpo nem é lido);
4. `Content-Type` → `415`;
5. tamanho → `413`;
6. JSON → `400 Corpo inválido`;
7. geometria (SRID, WKT) → `400 Geometria inválida`;
8. banco:
   - `ST_IsValid`/tipo → `400`;
   - lock não obtido em 5 s (`55P03`) → `409`;
   - id sem linha → `404`;
   - gravação → `204`;
   - mais de uma linha afetada (tabela não migrada) → ROLLBACK e `500`, com log Error 2116.

### Respostas do PUT (`application/problem+json`, com `traceId`)

| Status | `title` | `detail` | `instance` |
|--------|---------|----------|------------|
| 204 | (sem corpo) | | |
| 400 | `Id inválido` | `O id deve ser um inteiro entre 1 e 2147483647.` | `/api/mapas/{id}/graphy` (modelo fixo) |
| 400 | `Corpo inválido` | `O corpo deve ser um JSON no formato {"wkt":"...","srid":4326}.` | modelo fixo |
| 400 | `Geometria inválida` | motivo fixo (ver acima) | modelo fixo |
| 403 | `Origem não permitida` | ver "403 de origem" | omitido |
| 404 | `Mapa não encontrado` | `Nenhuma linha encontrada em public.mapas para o id 99.` | `/api/mapas/99/graphy` |
| 405 | `Method Not Allowed` | PUT com a escrita desligada | omitido |
| 409 | `Atualização concorrente` | `Outra atualização do id 99 está em andamento. Tente novamente.` | `/api/mapas/99/graphy` |
| 413 | `Corpo muito grande` | `O corpo excede o limite de 65536 bytes.` | modelo fixo |
| 415 | `Tipo de mídia não suportado` | `Use Content-Type: application/json (charset utf-8 opcional).` | modelo fixo |
| 429 | `Muitas requisições` | texto do GET | omitido |
| 500 | `Erro interno` | texto do GET (inclui o prazo de 30 s, `statement_timeout`, pool esgotado e `rowcount > 1`) | omitido |

## Contrato do POST (cria um id novo)

`POST /api/mapas/graphy` e `POST /api/mapas/metry` criam **uma linha nova**. O `id` é gerado pela sequência
(`INSERT ... RETURNING id`; a API nunca informa id). A geometria vai na coluna da rota e a outra coluna fica
NULL. Sucesso: **`201 Created`**, **sem corpo**, com:

```
Location: /api/mapas/{id gerado}/{coluna}
```

O `Location` é relativo e montado a partir do inteiro devolvido pelo `RETURNING`. Ele aponta para o GET da
coluna. Exemplo:

```bash
curl -i -X POST http://localhost:5080/api/mapas/metry -H "Content-Type: application/json" \
     -d '{"wkt":"POINT(333250 7394600)","srid":31983}'
# HTTP/1.1 201 Created
# Location: /api/mapas/42/metry
```

- **O POST não é idempotente (R-N4).** Cada POST cria um id novo, e dois POST iguais criam dois ids. Se o
  cliente receber `500`, timeout ou queda de conexão, **não sabe** se a linha foi criada: o COMMIT pode ter
  acontecido. Um retry automático pode criar um id a mais. A API não tem `Idempotency-Key` e não faz retry
  sozinha. Antes de repetir, procure no log de auditoria (4207) o `traceId` da tentativa, ou confira os
  últimos ids. Se sobrar um id duplicado, apague-o (ver "Limpeza de ids criados").
- `POST /api/mapas/{id}/graphy` (rota com id) continua `405`. PATCH e DELETE em qualquer rota também
  respondem `405`.
- Com a escrita desligada, `POST /api/mapas/graphy|metry` responde **`404`**, porque a rota não existe
  (RS-21).
- Ids têm lacunas: um INSERT que falha também consome um valor da sequência.

### Ordem das verificações (POST)

1. origem → `403`;
2. limitadores → `429`;
3. `Content-Type` → `415`;
4. tamanho → `413`;
5. JSON → `400 Corpo inválido`;
6. geometria → `400 Geometria inválida`;
7. banco:
   - `ST_IsValid`/tipo → `400`;
   - lock não obtido em 5 s (`55P03`) ou deadlock (`40P01`) → ROLLBACK e `409`;
   - INSERT → `201`, ou `500` se falhar.

### Respostas do POST

| Status | `title` | `detail` | `instance` |
|--------|---------|----------|------------|
| 201 | (sem corpo) | `Location: /api/mapas/{id}/{coluna}` | |
| 400 | `Corpo inválido` / `Geometria inválida` | como no PUT | `/api/mapas/graphy` ou `/api/mapas/metry` |
| 403 | `Origem não permitida` | ver "403 de origem" | omitido |
| 404 | `Not Found` | escrita desligada (rota não mapeada) | omitido |
| 409 | `Atualização concorrente` | `Outra atualização de public.mapas está em andamento. Tente novamente.` | `/api/mapas/graphy` ou `/metry` |
| 413 / 415 / 429 | como no PUT | | |
| 500 | `Erro interno` | texto genérico | omitido |

Casos de `500` no POST:
- `23505` em `mapas_pkey`: o id gerado já existe porque alguém inseriu um id à frente da sequência (R-N3).
  Log Error 2132, só com SqlState e constraint, sem retry. Correção: o DBA roda de novo a migração, cujo passo
  7 faz o `setval`.
- outros `23505` e o `23514` (CHECK `mapas_nao_vazia`): log Error 2133.
- `RETURNING` inválido (log Error 2134), prazo de 30 s, `statement_timeout` e pool esgotado.

### Limpeza de ids criados (RS-16)

A role de escrita **não tem `DELETE`**, e nenhum caminho da API apaga linhas. Para apagar ids criados por
POST de teste (coleção Postman de POST ou testes manuais), anote os ids do `Location` e siga
`database/README.md`, "Limpeza de ids de teste". O caminho é um destes:
- a `mapas_teste` religada temporariamente (`roles.sql` + `\password mapas_teste`, depois `NOLOGIN PASSWORD
  NULL`);
- `casaos` por socket local.

Apague um id por vez, conferindo `DELETE 1`:

```sql
BEGIN;
DELETE FROM public.mapas WHERE id = <id capturado do Location>;
-- se não aparecer "DELETE 1": ROLLBACK;
COMMIT;
```

## Decisões de comportamento

- **Queda durante o COMMIT.** O COMMIT é enviado sem cancelamento (`CancellationToken.None`), para não
  abandonar uma transação no meio. Se a conexão cair justo durante o COMMIT, o cliente recebe `500` sem saber
  se gravou. No **PUT**, repetir é seguro, porque ele é idempotente. No **POST**, repetir pode criar outro id:
  confira antes (ver "Contrato do POST").
- **Subida até 15 s mais lenta** com a escrita configurada (checagem V1).
- Prazo total de **30 s** por PUT ou POST: ao estourar, ROLLBACK e `500` genérico. Cliente que desconecta no
  meio recebe `499` sem corpo e a transação é desfeita.

## Logs de escrita

- **Auditoria PUT** (Information 4110): id, coluna e `traceId`.
- **Auditoria POST** (Information 4207): id criado, coluna e `traceId`.
- Os eventos do PUT são 4101 a 4116 e os do POST são 4201 a 4212. Os eventos de início 4006 a 4010 dizem
  "PUT e POST".
- Falhas registram só SqlState, constraint, id e coluna. Nunca o WKT, o corpo, a mensagem do
  Npgsql/PostgreSQL ou credenciais.
- O 403 de origem gera log Debug (4012), sem os valores dos cabeçalhos.

## Exemplos `curl`

```bash
curl -i http://localhost:5080/api/mapas/1/graphy
curl -i http://localhost:5080/api/mapas/1/metry
curl -i http://localhost:5080/api/mapas/abc/graphy        # 400
curl -i http://localhost:5080/api/mapas/2147483647/metry  # 404 (se o id não existir)

# POST (CRIA uma linha; anote o id do Location para a limpeza)
curl -i -X POST http://localhost:5080/api/mapas/graphy -H "Content-Type: application/json" \
     -d '{"wkt":"POINT(-46.633309 -23.55052)","srid":4326}'                      # 201 + Location
curl -i -X POST http://localhost:5080/api/mapas/metry -H "Content-Type: application/json" \
     -d '{"wkt":"POINT(333250 7394600)","srid":31983}'                           # 201 + Location

# PUT (grava na linha de um id EXISTENTE; use um id de teste)
curl -i -X PUT http://localhost:5080/api/mapas/2/graphy -H "Content-Type: application/json" \
     -d '{"wkt":"POINT(-46.5 -23.5)","srid":4326}'                               # 204
curl -i -X PUT http://localhost:5080/api/mapas/2/graphy -H "Content-Type: text/plain" -d '{}'   # 415
curl -i -X PUT http://localhost:5080/api/mapas/2/graphy -H "Content-Type: application/json" \
     -H "Origin: http://exemplo.invalido" -d '{"wkt":"POINT(0 0)","srid":4326}'   # 403
```

## Coleções Postman

São duas coleções (schema v2.1, sem autenticação e sem segredos). A de POST fica separada porque **cria
linhas** (RS-25).

| Arquivo | Conteúdo | Grava no banco? |
|---------|----------|-----------------|
| `postman/api_mapas.postman_collection.json` | GET (pastas `graphy` e `metry`) e pasta `escrita (PUT)` | só a pasta de PUT, e só no `idEscrita` |
| `postman/api_mapas_post.postman_collection.json` | POST, GET e PUT de conferência nos ids criados | **sim, cria 2 ids por execução** |

Importar no Postman (app desktop/web): **File > Import** (ou botão **Import**) e selecione o arquivo.

### Coleção principal (`api_mapas`)

- Pastas `graphy` e `metry`, com 6 GET cada: 200, 404, 400 texto, 400 zero, 400 longo `12345678901` e
  400 `1%27`.
- Pasta **`escrita (PUT)`**, com 15 requisições. Dez PUT contam no limite de 10 escritas/min, o que esgota a cota do minuto:
  - PUT 204 em `graphy` e em `metry`, cada um seguido de GET de conferência;
  - 400 de id, de corpo (campo extra e formato antigo com lista) e de geometria (faixa local e `ST_IsValid`);
  - 404;
  - 413 (corpo acima de 64 KiB);
  - 415;
  - 403 de origem;
  - POST na rota com id (405).

| Variável | Padrão | Uso |
|----------|--------|-----|
| `baseUrl` | `http://localhost:5080` | endereço da API local |
| `id` | `1` | id **existente** com `graphy` e `metry` preenchidas (casos 200) |
| `idInexistente` | `2147483647` | caso 404 |
| `idInvalido` | `abc` | caso 400 |
| `idEscrita` | (vazio) | id **existente** dedicado a testes de escrita, **diferente** de `id`. A pasta `escrita (PUT)` regrava `graphy` e `metry` dele (a linha fica com as duas colunas). Vazio ou igual a `id`: a pasta é pulada |

O `idEscrita` precisa existir antes. Crie-o com a `mapas_teste` (como nos testes de integração) ou com a
coleção de POST, e reaproveite o id do `Location`.

```bash
postman collection run postman/api_mapas.postman_collection.json --env-var "baseUrl=http://localhost:5080" --env-var "id=<id existente>"
postman collection run postman/api_mapas.postman_collection.json --env-var "baseUrl=http://localhost:5080" \
  --env-var "id=<id existente>" --env-var "idEscrita=<outro id existente, só de teste>"
```

Os casos 200/404 dependem do banco; sem banco eles recebem 500 (esperado). Os casos 400 não tocam o banco.

### Coleção de POST (`api_mapas - POST (cria linhas)`)

Ela **cria 2 ids novos por execução**, um com `graphy` e outro com `metry`, e nada na API os apaga. Para
rodar, é preciso definir `permitirCriacao=sim`; com qualquer outro valor, todas as requisições são puladas.
O script de teste lê o id do `Location`, guarda em `idCriadoGraphy`/`idCriadoMetry` e acumula em `idsCriados`.
A última requisição imprime no console a lista de ids e o SQL de limpeza.

Requisições, na ordem (6 escritas, dentro do limite de 10/min):
1. POST `graphy` 201;
2. GET `graphy` 200 e GET `metry` 404 do id criado (coluna NULL);
3. POST `metry` 201 e GET `metry` 200;
4. PUT `graphy` no id criado por `metry` (204, Q3) e GET 200;
5. POST 400 com o corpo antigo;
6. POST 400 de SRID;
7. POST 415;
8. POST 403 de origem (não conta no limite).

```bash
postman collection run postman/api_mapas_post.postman_collection.json \
  --env-var "baseUrl=http://localhost:5080" --env-var "permitirCriacao=sim"
```

**Depois de cada execução, apague os ids impressos** (ver "Limpeza de ids criados"). Rode só contra uma API
local de teste ou contra o servidor com a autorização do dono dos dados.

Para rodar a coleção principal e a de POST em seguida, espere 1 minuto entre elas ou use
`--delay-request 7000`. O limite de 10 escritas por minuto vale para a API toda.

## Testes

```bash
dotnet test ApiMapas.sln                                   # tudo (integração fica ignorada)
dotnet test ApiMapas.sln --filter "Category!=Integration"  # só unitários / API em memória
dotnet test ApiMapas.sln --collect:"XPlat Code Coverage"   # cobertura (coverlet)
```

Resultado do TestBrain (2026-10-23, PO6, modelo de uma geometria por id):
- 1399 aprovados, 0 falhas e 25 ignorados (integração opt-in), estável em 5 execuções;
- cobertura de 97,81% de linhas e 95,53% de ramos (99,59% de linhas no código escrito à mão).

Integração opt-in contra o banco real (use a role somente leitura; o banco precisa estar **migrado**):

```bash
export PG_RUN_INTEGRATION=1 PG_USER=api_mapas_leitura PG_SSLMODE=Require PG_TEST_ID=<id existente>
read -rs -p "Senha: " PG_PASSWORD; echo; export PG_PASSWORD
dotnet test ApiMapas.sln --filter "Category=Integration"
unset PG_PASSWORD
```

A integração de escrita também usa `PG_ESCRITA_USER`/`PG_ESCRITA_PASSWORD`. Ela cria os ids de PUT com a role
`mapas_teste` (variáveis `PG_TESTE_*`), nunca com `casaos`, e lê do `Location` os ids criados por POST. Todos
são apagados no `finally` com a `mapas_teste`, conferindo `rowcount == 1` (RS-7). Não rode a integração de
escrita contra o servidor sem o backup do U-4.

Nos testes com `WebApplicationFactory`, defina `PG_USER`/`PG_PASSWORD` fictícios no processo e, se fizer muitas
chamadas, aumente `ApiMapas:LimiteRequisicoesPorMinuto` (ex.: `UseSetting`).

## Manual da base

A API lê `public.mapas` (servidor `192.168.168.106`, base `casaos`). Com a escrita ligada:
- o PUT altera **só a coluna da rota** na linha do id;
- o POST cria linhas novas.

A API não cria nem altera tabelas e não apaga linhas. Manual completo da base, das roles, da migração e dos
procedimentos do DBA: `database/README.md`.

| Coluna | Tipo | Uso na API |
|--------|------|------------|
| `id` | `int4 GENERATED ALWAYS AS IDENTITY`, **PRIMARY KEY `mapas_pkey`** (depois da migração) | uma linha por id. GET e PUT filtram `WHERE id = @id`. O POST nunca informa id: ele vem da sequência pelo `RETURNING id` |
| `graphy` | `geography` (normalmente SRID 4326) | `GET .../graphy`, lida como `graphy::geometry`; PUT e POST gravam `ST_GeomFromText(wkt, 4326)::geography` |
| `metry` | `geometry` (SRIDs 4326, 4674, 3857, 31983 ou 0) | `GET .../metry`; PUT e POST gravam `ST_GeomFromText(wkt, srid)` |

- CHECK `mapas_nao_vazia` (`graphy IS NOT NULL OR metry IS NOT NULL`): o POST sempre preenche uma coluna e o
  PUT nunca limpa, então a API respeita o CHECK.
- SQL: Q1/Q2 (GET, `LIMIT 2`), W1.V/W2.V (validação), W1/W2 (UPDATE pela PK), W3/W4 (`INSERT ... RETURNING
  id`) e V1 (14 colunas da checagem da role), todos em `sql/consultas_referencia.sql`.
- Roles (fonte oficial `database/roles.sql`, revisão 5):
  - `api_mapas_leitura`: só `SELECT`, sessão read-only, `CONNECTION LIMIT 20`;
  - `api_mapas_escrita`: `SELECT (id, graphy, metry)`, `UPDATE (graphy, metry)` e `INSERT (graphy, metry)`,
    **sem `DELETE`**, sem INSERT/UPDATE em `id` e nada na sequência (a identity não exige), com
    `CONNECTION LIMIT 5`.
  - Senhas só via `\password`.

## Riscos aceitos (POC)

- **Sem autenticação** em PUT e POST (U-1): uso somente local (loopback), com as condições do início deste
  README.
- **U-2 encerrado:** a role de escrita não tem mais `DELETE` (Q4).
- Riscos residuais do modelo novo (R-N1 a R-N5, RS-26). **O aceite do usuário está pendente** (tarefa PU-R em
  `tarefas/afazer.md`):
  - **R-N1 - Enumeração e sobrescrita via PUT.** Sem autenticação, qualquer processo local pode ler qualquer
    id e sobrescrever a geometria de qualquer id existente. Mitigações: U-1, limitadores, 403 de origem, log
    de auditoria e backup.
  - **R-N2 - `Location` sequencial.** O id devolvido revela o volume de linhas e permite prever o próximo.
  - **R-N3 - `23505` por inserção manual.** Um INSERT manual com `OVERRIDING SYSTEM VALUE` à frente da
    sequência faz o POST responder `500` até o DBA rodar o `setval` (passo 7 da migração).
  - **R-N4 - POST não idempotente.** Um retry depois de `500` ou timeout pode criar um id a mais (ver
    "Contrato do POST").
  - **R-N5 - Mescla manual dos duplicados.** A deduplicação antes da PK depende do modelo comentado e, nos
    conflitantes, da escolha do usuário.
- Limitação conhecida PO6-1: escrita recusada pela cota por minuto gasta 2 permissões do limite global por IP
  (ver "Limitadores").
- `PG_HOST` padrão é um IP interno citado no código e neste README.
