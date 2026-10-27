# Infraestrutura — publicação da api_mapas na Azure via Jenkins

Pipeline Jenkins que testa a `api_mapas`, gera duas imagens Docker, publica no Azure Container
Registry (ACR) e implanta numa VM Linux da Azure com Docker Compose.

```
Jenkins ──build/push──▶ ACR (acrpostgispoc.azurecr.io)
   │                         │ pull
   └──ssh/scp──▶ VM Ubuntu 24.04 (/opt/api_mapas)
                 ┌───────────────────────── docker compose ─────────────────────────┐
  Internet ─443─▶│ nginx (TLS + auth básica + rate limit) ─127.0.0.1:5080─▶ api     │
                 │        └──────── rede "interna" (sem internet) ────▶ postgis:5432 │
                 └───────────────────────────────────────────────────────────────────┘
```

## Arquivos

| Arquivo | Conteúdo |
|---------|----------|
| `Jenkinsfile` | Pipeline: versão → testes (cobertura ≥ 80%) → imagens → push no ACR → implantação (só `main`). |
| `jenkins/publicar_acr.sh` | Estágio "Publicar no ACR": confere credencial, imagens, DNS, acesso HTTPS, login e push, e em cada falha mostra a causa provável e o comando para corrigir. |
| `docker-compose.yml` | Pilha da VM: `postgis`, `nginx` e `api`. |
| `.env.example` | Modelo do `/opt/api_mapas/.env` da VM (registro, tags, limites; sem segredos). |
| `postgis/Dockerfile` | **PostgreSQL 17 + PostGIS 3.5** (`postgis/postgis:17-3.5`) com os scripts de `database/`. |
| `postgis/initdb/20_ssl.sh` | 1ª subida: certificado autoassinado do Postgres no `PGDATA`. |
| `postgis/initdb/30_mapas.sh` | 1ª subida: `mapas.sql` → `migracao_mapas_pk.sql` → `validar_check_mapas.sql` → `roles.sql` (com SSL) → senhas → `pg_hba.conf`. |
| `postgis/pg_hba.conf` | Só `api_mapas_leitura`/`api_mapas_escrita` por `hostssl` + `scram-sha-256` na rede interna; o resto é `reject`. |
| `api/Dockerfile` | Build .NET 10 multi-stage; estágio `resultados` (testes + cobertura) e `final` (runtime, usuário sem privilégio). |
| `nginx/api_mapas.conf` | Proxy na frente da API: HTTPS, autenticação básica, limite por IP real, corpo ≤ 1 MiB. |
| `servidor/preparar_servidor.sh` | Roda **uma vez** na VM: senhas aleatórias, `api.env`, `htpasswd`, certificado. |
| `servidor/implantar.sh` | Chamado pelo Jenkins na VM: login no ACR, `compose pull/up`, teste de fumaça. |
| `azure/provisionar.sh` | Cria grupo de recursos, ACR, VM, regras de NSG e service principal (Azure CLI). |
| `azure/cloud-init.yaml` | 1º boot da VM: Docker, compose, ufw, rotação de logs. |

Os scripts de banco **não foram copiados nem alterados**: a imagem do PostGIS usa os arquivos de
`database/` como estão (fonte oficial). Nenhum arquivo desta pasta contém senha.

## Por que tem um nginx

A API não tem autenticação e só aceita ouvir em loopback (RA-5): ela recusa subir com
`0.0.0.0`. No compose, a `api` usa o **namespace de rede do nginx** (`network_mode:
"service:nginx"`), ouve em `127.0.0.1:5080` e o nginx é o único que publica portas. O nginx
acrescenta o que a API não tem para ficar na internet:

- **HTTPS** (TLS 1.2/1.3) e redirecionamento de 80 para 443;
- **autenticação básica** (usuário em `segredos/htpasswd`); o cabeçalho `Authorization` não
  segue para a API;
- **limite por IP real**: 60 req/min e 10 escritas/min. Atrás do proxy a API vê todo mundo como
  `127.0.0.1`, então os limites "por IP" dela passam a valer para a API inteira
  (`API_LIMITE_*` no `.env` ajusta);
- `Host: localhost` para o `AllowedHosts` da API.

## Escrita (PUT/POST) desligada por padrão

A decisão **U-1** do projeto diz que a escrita sem autenticação só vale em máquina de um único
usuário e **nunca exposta por proxy**. Por isso o `preparar_servidor.sh` cria o `api.env` só com
a role de leitura: o GET funciona, o PUT responde 405 e o POST 404. Para ligar a escrita (fica
atrás do TLS + autenticação do nginx), depois de uma revisão do SecBrain:

```bash
sudo rm /opt/api_mapas/segredos/api.env
sudo HABILITAR_ESCRITA=1 bash preparar_servidor.sh
# e rodar o pipeline de novo (ou: cd /opt/api_mapas && sudo docker compose up -d --force-recreate api)
```

## Primeira implantação

1. **Azure** (máquina com Azure CLI, `az login` feito):
   ```bash
   IP_ADMIN=<seu-ip>/32 IP_JENKINS=<ip-do-jenkins>/32 bash infraestrutura/azure/provisionar.sh
   ```
   Variáveis opcionais: `GRUPO`, `LOCAL` (padrão `brazilsouth`), `ACR_NOME` (único na Azure),
   `VM_NOME`, `VM_TAMANHO` (padrão `Standard_B2s`), `CHAVE_SSH`. O script mostra o IP da VM, o
   `appId` e a senha do service principal **uma vez**: cadastre direto no Jenkins.

2. **VM** (depois que o cloud-init terminar):
   ```bash
   scp infraestrutura/servidor/preparar_servidor.sh azureuser@<ip-da-vm>:
   ssh azureuser@<ip-da-vm> 'cloud-init status --wait && sudo bash preparar_servidor.sh <dominio-ou-ip>'
   ```

3. **Jenkins**:
   - Agente Linux com git, Docker 23+ (BuildKit) e ssh/scp. Plugins: Pipeline, Credentials
     Binding, SSH Agent, Timestamper.
   - Credenciais: `acr-credenciais` (Username with password = `appId`/senha do service
     principal) e `azure-vm-ssh` (SSH Username with private key, usuário `azureuser`).
   - Job Pipeline (ou Multibranch) apontando para este repositório, **Script Path**
     `infraestrutura/Jenkinsfile`.
   - Parâmetros: `ACR_NOME`, `VM_HOST` (IP da VM), `VM_USUARIO`, `IMPLANTAR`,
     `COBERTURA_MINIMA`.

4. **Rodar o job.** Na primeira subida o PostGIS cria a base `casaos`, a tabela já migrada, as
   roles e o SSL (cerca de 1 minuto). O `implantar.sh` só segue quando o banco está `healthy` e
   termina com um `GET /api/mapas/1/graphy` (200 ou 404 = ok).

5. **Teste de fora:**
   ```bash
   sudo cat /opt/api_mapas/segredos/basic_auth.txt     # na VM: usuario:senha
   curl -k -u 'mapas:<senha>' https://<ip-da-vm>/api/mapas/1/graphy
   ```
   (`-k` só enquanto o certificado for autoassinado.)

## Tags das imagens

- `api_mapas:<build>-<commit>`: nova a cada build.
- `postgis_mapas:17-3.5-<commit>`: o commit é o último que mexeu em `database/` ou
  `infraestrutura/postgis/`. Se a tag já existe no ACR, o push é pulado e, na VM, o container do
  banco não é recriado.

**Atenção:** os scripts do `initdb` só rodam com o volume vazio. Uma imagem nova do PostGIS com
um `roles.sql` revisado **não** reaplica nada sozinha. Para reaplicar as roles (idempotente):

```bash
cd /opt/api_mapas
sudo docker compose exec postgis psql -X -v ON_ERROR_STOP=1 -v pular_ssl=1 \
     -U casaos -d casaos -f /opt/mapas/sql/roles.sql
```

Depois de reaplicar, as senhas das roles continuam as mesmas.

## Operação

| Tarefa | Comando (na VM, em `/opt/api_mapas`) |
|--------|--------------------------------------|
| Estado | `sudo docker compose ps` |
| Logs | `sudo docker compose logs -f api` (ou `postgis`, `nginx`) |
| psql | `sudo docker compose exec postgis psql -U casaos -d casaos` |
| Backup | `sudo docker compose exec -T postgis pg_dump -U casaos -d casaos -t public.mapas > ~/backups/mapas_$(date +%Y%m%d).sql` (guardar fora da VM) |
| Restaurar | ver o cabeçalho de `database/backup_mapas.sh` |
| Voltar versão | rodar de novo um build anterior no Jenkins (as imagens ficam no ACR) |
| Trocar senha do banco | `\password <role>` no psql **e** a mesma senha no arquivo de `segredos/` correspondente e no `api.env`; depois `sudo docker compose up -d --force-recreate api` |

- O banco **não** publica porta. Para acessar de fora, use túnel SSH até a VM e
  `docker compose exec`; o `pg_hba.conf` recusa `casaos` e `mapas_teste` por TCP.
- **Certificado válido**: troque `certs/fullchain.pem` e `certs/privkey.pem` (por exemplo, pelo
  `certbot certonly --standalone` com o nginx parado, ou por um certificado da empresa) e rode
  `sudo docker compose restart nginx`.
- Depois de um reboot da VM, o Docker sobe os containers sem esperar o banco. A leitura se
  recupera sozinha, mas a checagem da role de escrita (R-1) roda só na subida da API: com a
  escrita habilitada, rode `sudo docker compose restart api` se o log mostrar "Escrita (PUT e
  POST) DESLIGADA".
- Rode backups periódicos (cron na VM ou Azure Backup do disco). O volume `postgis_dados` fica no
  disco do SO da VM.

## O que não foi validado

- O Docker não estava rodando na máquina de desenvolvimento: o build das imagens, a
  inicialização do PostGIS e o pipeline **ainda não foram executados**. Foram conferidos a
  sintaxe dos scripts (`bash -n`) e o `docker-compose.yml` (`docker compose config`).
- O `30_mapas.sh` roda o `roles.sql` inteiro (inclusive a seção 7, SSL) numa base nova. O
  DataBrain já validou esse script no `postgis/postgis:17-3.5` descartável; a combinação com o
  `initdb` da imagem precisa ser conferida na primeira subida (`docker compose logs postgis`).
