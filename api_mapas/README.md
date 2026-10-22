# api_mapas — geometrias de `public.mapas` por id (graphy e metry)

> **ATENÇÃO: a API NÃO tem autenticação. Uso SOMENTE LOCAL.**
> Ela só sobe ouvindo em loopback (`localhost`, `127.0.0.1`, `[::1]`). Se `ASPNETCORE_URLS`/`--urls`,
> `HTTP_PORTS`/`HTTPS_PORTS` ou `Kestrel:Endpoints` apontarem para `0.0.0.0`, `*`, `+`, `[::]`, IP de LAN
> ou outro nome de host, a API **recusa subir** com mensagem clara. Não exponha a porta na rede.

API REST ASP.NET Core (.NET 10, DDD, Npgsql) com dois endpoints somente leitura:

| Rota | Fonte |
|------|-------|
| `GET /api/mapas/{id}/graphy` | `public.mapas.graphy` (`geography`, lida como `graphy::geometry`) |
| `GET /api/mapas/{id}/metry`  | `public.mapas.metry` (`geometry`) |

As consultas são as constantes Q1/Q2 de [`sql/consultas_referencia.sql`](sql/consultas_referencia.sql)
(DataBrain), com o único parâmetro `@id` tipado como `Integer`.

## Estrutura

```
api_mapas/
  ApiMapas.sln
  src/ApiMapas.Dominio          ColunaMapa, IdMapa (validação do id), GeometriaMapa, IMapasRepository
  src/ApiMapas.Aplicacao        ConsultaGeometriasService, DTOs, limites (100 itens / 5 MB)
  src/ApiMapas.Infraestrutura   ConexaoConfig (PG_*), MapasRepository (Npgsql, SQL constante)
  src/ApiMapas.Api              Program, endpoints, ProblemDetails, cabeçalhos, rate limiting
  tests/ApiMapas.Tests          xUnit (TestBrain)
  postman/api_mapas.postman_collection.json
  sql/consultas_referencia.sql
```

## Pré-requisitos

- .NET SDK 10 (`dotnet --list-sdks`).
- PostgreSQL com PostGIS e uma role **somente leitura** (`GRANT SELECT ON public.mapas`, não superusuário).
  Sugestão de role em `sql/consultas_referencia.sql` (não executada).

## Variáveis de ambiente (credenciais SOMENTE pelo shell)

| Variável | Obrigatória | Padrão | Observação |
|----------|-------------|--------|------------|
| `PG_USER` | sim | — | sem ela a API não sobe |
| `PG_PASSWORD` | sim | — | sem ela a API não sobe; nunca é logada |
| `PG_HOST` | não | `192.168.168.106` | |
| `PG_PORT` | não | `5432` | 1..65535 |
| `PG_DATABASE` | não | `casaos` | |
| `PG_SSLMODE` | não | `Require` | só `Disable`, `Prefer`, `Require`, `VerifyCA`, `VerifyFull`; outro valor impede subir; `Disable`/`Prefer` geram warning no log |

**Não** grave credenciais em `appsettings*.json`, `launchSettings.json`, `.env` ou na coleção Postman.
O `.env.example` contém apenas placeholders e a API não lê arquivos `.env`. A pasta está no OneDrive:
qualquer arquivo com senha seria sincronizado (ver alerta RQ-9 no Kanban).

Conexão: `Timeout` 10 s, `CommandTimeout` 15 s, `Maximum Pool Size` 20, `ApplicationName=ApiMapas`,
`Persist Security Info=false`, sem `IncludeErrorDetail`. O log mostra só `host:porta/base ssl=...`.

> **TLS:** `Require` cifra a conexão, mas **não valida o certificado** do servidor (sujeito a MITM).
> Quando o PostgreSQL tiver certificado, use `PG_SSLMODE=VerifyFull` e confie na CA do servidor
> (Root Certificate: arquivo `root.crt` da CA, ex.: via `PGSSLROOTCERT` ou no repositório de certificados do SO).

> **Situação do servidor (validação de 2026-10-21):** o servidor 192.168.168.106 está com
> `ssl = off`. Por isso, hoje a API e os testes de integração **falham** com
> `PG_SSLMODE=Require`, que é o padrão. Não use `Prefer`, que cairia para texto claro. Antes
> de executar contra este servidor, cumpra os pré-requisitos de `tarefas/afazer.md`: role
> `api_mapas_leitura`, senha rotacionada, `pg_hba` restrito e SSL habilitado. A outra opção é
> a exceção `PG_SSLMODE=Disable`, que só vale com decisão registrada pelo usuário e com todas
> as condições listadas lá.

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

A API ouve em **http://localhost:5080** (perfil `http` do `launchSettings.json`, com `ASPNETCORE_ENVIRONMENT=Production`; perfil `https`
acrescenta `https://localhost:7080` e exige o certificado de desenvolvimento). Para outra porta:
`ASPNETCORE_URLS=http://localhost:5090` (só loopback).

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
| `ApiMapas:LimiteBytesGeometrias` | `5242880` (5 MB) | teto de bytes UTF-8 somando `wkt` + `geojson` dos itens da resposta |
| `ApiMapas:LimiteRequisicoesPorMinuto` | `60` | rate limiting por IP (janela fixa de 1 min); excedente recebe `429` |
| `AllowedHosts` | `localhost;127.0.0.1;[::1]` | outros cabeçalhos `Host` recebem 400 |

Valor inválido em qualquer chave impede a API de subir (mensagem cita só o nome da chave).

## Contrato

### `id`
Inteiro decimal `1..2147483647`, só dígitos ASCII, sem sinal/espaço, até 10 caracteres.
`abc`, `0`, `-1`, `1.5`, `+1`, `2147483648`, `12345678901`, `1'` → **400** sem consultar o banco.

### 200 (`application/json`)

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

- O `id` não é único em `public.mapas`: a resposta é uma lista (`ORDER BY ctid`).
- `wkt`, `x`, `y`: SRID nativo. `geojson`, `longitude`, `latitude`: WGS84 (EPSG:4326). SRID 0 → `geojson` nativo e `longitude`/`latitude` nulos.
- `x`/`y`/`longitude`/`latitude` só para `POINT`; nos demais tipos vêm `null` (coordenadas no `geojson`).

### Limites da resposta (`truncado`)
- No máximo **100 itens** (a consulta usa `LIMIT 101`; havendo a 101ª linha, `truncado = true`).
  Não há paginação: o id raramente tem mais de 100 linhas.
- Teto de **bytes** (`ApiMapas:LimiteBytesGeometrias`, padrão 5 MB) somando `wkt` + `geojson`: o item que
  estoura o teto e todos os seguintes ficam de fora, com `truncado = true`. Se o **primeiro** item sozinho
  passar do teto, a resposta é `200` com `quantidade: 0`, `geometrias: []` e `truncado: true`.

### Erros (`application/problem+json`)

| Status | `title` | Quando |
|--------|---------|--------|
| 400 | `Id inválido` | id fora da regra (o valor enviado não é ecoado; `instance` = `/api/mapas/{id}/graphy` ou `/metry`) |
| 404 | `Geometria não encontrada` | id inexistente ou só nulos na coluna |
| 404 / 405 | `Not Found` / `Method Not Allowed` | rota desconhecida / método diferente de GET |
| 429 | `Muitas requisições` | rate limiting (com `Retry-After`) |
| 500 | `Erro interno` | erro de banco/timeout/inesperado; corpo genérico com `traceId` (detalhe só no log) |

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
`traceId` no log do console (uma falha de banco gera 3 linhas `Error` — repositório, serviço e manipulador —,
mas o stack completo só aparece no log do `ManipuladorExcecoes`).

Todas as respostas levam `X-Content-Type-Options: nosniff` e `Cache-Control: no-store`; não há header
`Server` nem CORS.

## Exemplos `curl`

```bash
curl -i http://localhost:5080/api/mapas/1/graphy
curl -i http://localhost:5080/api/mapas/1/metry
curl -i http://localhost:5080/api/mapas/abc/graphy        # 400
curl -i http://localhost:5080/api/mapas/2147483647/metry  # 404 (se o id não existir)
```

## Coleção Postman

Arquivo: `postman/api_mapas.postman_collection.json` (schema v2.1, sem autenticação e sem segredos).

Importar no Postman (app desktop/web):
1. **File > Import** (ou botão **Import**) → selecione `api_mapas/postman/api_mapas.postman_collection.json`.
2. A coleção `api_mapas` aparece com as pastas `graphy` e `metry` (6 requisições cada: 200, 404, 400 texto,
   400 zero, 400 longo `12345678901`, 400 `1%27`).
3. Na coleção, aba **Variables**, ajuste o *Current value*:

| Variável | Padrão | Uso |
|----------|--------|-----|
| `baseUrl` | `http://localhost:5080` | endereço da API local |
| `id` | `1` | id **existente** em `public.mapas` (caso 200) |
| `idInexistente` | `2147483647` | caso 404 |
| `idInvalido` | `abc` | caso 400 |

4. Suba a API (seção "Como subir") e clique **Send** em cada requisição, ou **Run collection** para rodar todas.
   Cada requisição tem testes (status, `Content-Type`, campos do corpo, ausência de vazamento, cabeçalhos
   `nosniff`/`no-store`) e exemplos de resposta salvos (200/404/400, dados fictícios).

Postman CLI (com a API no ar):

```bash
postman collection run postman/api_mapas.postman_collection.json --env-var "baseUrl=http://localhost:5080" --env-var "id=<id existente>"
```

Os casos 200/404 dependem do banco; sem banco eles recebem 500 (esperado). Os casos 400 não tocam o banco.

## Testes

```bash
dotnet test ApiMapas.sln                                   # tudo (integração fica ignorada)
dotnet test ApiMapas.sln --filter "Category!=Integration"  # só unitários / API em memória
dotnet test ApiMapas.sln --collect:"XPlat Code Coverage"   # cobertura (coverlet)
```

Resultado do TestBrain (2026-10-21): 287 testes, 284 aprovados, 0 falhas, 3 ignorados (integração);
cobertura 99,61% linhas / 98,72% branches.

Integração opt-in contra o banco real (use a role somente leitura):

```bash
export PG_RUN_INTEGRATION=1 PG_USER=api_mapas_leitura PG_SSLMODE=Require PG_TEST_ID=<id existente>
read -rs -p "Senha: " PG_PASSWORD; echo; export PG_PASSWORD
dotnet test ApiMapas.sln --filter "Category=Integration"
unset PG_PASSWORD
```

Nos testes com `WebApplicationFactory`, defina `PG_USER`/`PG_PASSWORD` fictícios no processo e, se fizer muitas
chamadas, aumente `ApiMapas:LimiteRequisicoesPorMinuto` (ex.: `UseSetting`).

## Manual da base (leitura)

A API só lê `public.mapas` (servidor `192.168.168.106`, base `casaos`); não cria nem altera tabelas.

| Coluna | Tipo | Uso na API |
|--------|------|------------|
| `id` | inteiro (int4) | filtro `WHERE id = @id`; **não** é PK nem UNIQUE (vários registros por id) e não tem índice |
| `graphy` | `geography` (normalmente SRID 4326) | `GET .../graphy`, lida como `graphy::geometry` |
| `metry` | `geometry` (SRIDs 4326, 4674, 3857, 31983 ou 0) | `GET .../metry` |

- Consultas Q1/Q2 e funções usadas (`GeometryType`, `ST_SRID`, `ST_AsText`, `ST_AsGeoJSON`, `ST_Transform`,
  `ST_X`/`ST_Y`): `sql/consultas_referencia.sql`.
- Sugestões **não executadas** no mesmo arquivo (decisão do DBA): índice `ix_mapas_id` (`CREATE INDEX CONCURRENTLY`)
  e a role `api_mapas_leitura` (`LOGIN NOSUPERUSER`, só `SELECT ON public.mapas`, `default_transaction_read_only`,
  `statement_timeout 15s`, `CONNECTION LIMIT 20`; senha só via `\password api_mapas_leitura`).

## Riscos aceitos (POC)

- Sem autenticação: uso somente local (loopback).
- O teto de bytes é verificado após materializar as linhas (o limite de 101 linhas segura o volume).
- `PG_HOST` padrão é um IP interno citado no código e neste README.
