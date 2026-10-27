<#
=====================================================================
database/backup_mapas.ps1  (U-4 / RP-16 / S-5 - P3.6)

Versao PowerShell (Windows PowerShell 5.1 e PowerShell 7) de
database/backup_mapas.sh: backup de public.mapas com
pg_dump -t public.mapas, em arquivo datado. Obrigatorio ANTES de
definir PG_ESCRITA_* na api_mapas (U-4). Quem roda e o usuario/DBA; os
agentes NAO executam este script.

NENHUMA CREDENCIAL NESTE SCRIPT. Nao existe parametro de senha. A senha
vem de $env:PGPASSWORD (so na sessao atual), do arquivo
%APPDATA%\postgresql\pgpass.conf ou do prompt do pg_dump.

Uso:
  powershell -ExecutionPolicy Bypass -File database\backup_mapas.ps1 `
      -PgHost 192.168.168.106 -User casaos
  (ou pwsh -File ...). Parametros:
    -PgHost   servidor ($env:PGHOST). Obrigatorio.
    -Port     porta ($env:PGPORT, padrao 5432)
    -Database base ($env:PGDATABASE, padrao casaos)
    -User     role ($env:PGUSER). Obrigatorio. Use casaos: as roles da
              API e de teste nao tem privilegio na sequencia da identity
              e o pg_dump falharia (ver backup_mapas.sh). Pelo pg_hba,
              casaos so entra local; se a sua maquina nao estiver
              liberada para casaos, rode backup_mapas.sh --container no
              host do servidor.
    -OutDir   destino ($env:BACKUP_DIR), FORA do repositorio e do
              OneDrive e DEDICADO (C7): um diretorio que ainda nao existe
              (o script cria e restringe a ACL) ou um diretorio vazio
              chamado backups_postgis. Outro diretorio existente faz o
              script abortar sem mexer na ACL dele. Sem -OutDir: cria a
              subpasta nova $HOME\backups_postgis\AAAAMMDD_HHMMSS a cada
              execucao.
SSL: $env:PGSSLMODE padrao require; aceita verify-ca e verify-full
(com $env:PGSSLROOTCERT). disable, allow e prefer sao recusados.

Saida: <OutDir>\mapas_backup_AAAAMMDD_HHMMSS.sql, com acesso so do
usuario atual. Sai com codigo 1 se o dump falhar, ficar vazio, nao tiver
o COPY de public.mapas ou o rodape "dump complete" (o parcial e
removido). Restauracao: ver o cabecalho de database/backup_mapas.sh.
=====================================================================
#>
[CmdletBinding()]
param(
    [string]$PgHost   = $env:PGHOST,
    [string]$Port     = $(if ($env:PGPORT) { $env:PGPORT } else { '5432' }),
    [string]$Database = $(if ($env:PGDATABASE) { $env:PGDATABASE } else { 'casaos' }),
    [string]$User     = $env:PGUSER,
    [string]$OutDir   = $env:BACKUP_DIR
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$carimbo = Get-Date -Format 'yyyyMMdd_HHmmss'
# Diretorio criado nesta execucao (C7); removido em caso de falha se
# ainda estiver vazio.
$script:DirCriado = $null

function Fail([string]$Mensagem) {
    [Console]::Error.WriteLine("ERRO: $Mensagem")
    if ($script:DirCriado -and (Test-Path -LiteralPath $script:DirCriado) -and
        -not (Get-ChildItem -LiteralPath $script:DirCriado -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $script:DirCriado -Force
    }
    exit 1
}
function Info([string]$Mensagem) { Write-Host ">> $Mensagem" }

# ---------------------------------------------------------------------
# Validacoes
# ---------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($PgHost)) { Fail 'informe -PgHost (ou $env:PGHOST)' }
if ($PgHost -notmatch '^[A-Za-z0-9]([A-Za-z0-9.:-]*[A-Za-z0-9])?$') { Fail "host invalido: $PgHost" }
if ($Port -notmatch '^[0-9]{1,5}$' -or [int]$Port -lt 1 -or [int]$Port -gt 65535) { Fail "porta invalida: $Port" }
if ($Database -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { Fail "base invalida: $Database" }
if ([string]::IsNullOrWhiteSpace($User)) { Fail 'informe -User (ou $env:PGUSER); use casaos' }
if ($User -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { Fail "role invalida: $User" }
if (@('api_mapas_leitura', 'api_mapas_escrita', 'mapas_teste') -contains $User) {
    Fail "a role $User nao tem privilegio na sequencia da identity e o pg_dump falharia; use casaos"
}

$sslMode = if ($env:PGSSLMODE) { $env:PGSSLMODE } else { 'require' }
if (@('require', 'verify-ca', 'verify-full') -notcontains $sslMode) {
    Fail "PGSSLMODE=$sslMode recusado: use require, verify-ca ou verify-full"
}
$env:PGSSLMODE = $sslMode

$pgDump = Get-Command pg_dump -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $pgDump) { Fail 'pg_dump nao encontrado no PATH (instale o cliente PostgreSQL 17 ou mais novo)' }

# ---------------------------------------------------------------------
# Diretorio de saida: fora do OneDrive e de repositorio git; ACL so do
# usuario atual.
# ---------------------------------------------------------------------
function Test-DentroOneDrive([string]$Caminho) {
    if ($Caminho -match '(^|[\\/])OneDrive( - [^\\/]*)?([\\/]|$)') { return $true }
    foreach ($raiz in @($env:OneDrive, $env:OneDriveConsumer, $env:OneDriveCommercial)) {
        if ($raiz -and $Caminho.StartsWith($raiz.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

# C7 (SecBrain, P5): a ACL so e restringida quando o PROPRIO script criou
# o diretorio, ou quando ele ja existe, esta VAZIO e se chama
# backups_postgis. Em qualquer outro caso, aborta sem tocar na ACL (nunca
# alterar a permissao de um diretorio de uso geral, ex.: -OutDir $HOME).
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path (Join-Path $HOME 'backups_postgis') $carimbo
}
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
if (Test-DentroOneDrive $OutDir) { Fail "-OutDir esta dentro do OneDrive ($OutDir); use um diretorio local" }
$criadoPeloScript = $false
if (Test-Path -LiteralPath $OutDir) {
    $item = Get-Item -LiteralPath $OutDir -Force
    if (-not $item.PSIsContainer -or ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
        Fail "-OutDir ($OutDir) existe e nao e um diretorio comum; use um diretorio dedicado"
    }
} else {
    New-Item -ItemType Directory -Path $OutDir | Out-Null
    $criadoPeloScript = $true
    $script:DirCriado = $OutDir
}
$OutDir = (Resolve-Path -LiteralPath $OutDir).ProviderPath
if ($criadoPeloScript) { $script:DirCriado = $OutDir }
if (Test-DentroOneDrive $OutDir) { Fail "-OutDir esta dentro do OneDrive ($OutDir); use um diretorio local" }

$git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($git) {
    # PowerShell 5.1: stderr de comando nativo redirecionado vira erro com
    # ErrorActionPreference = Stop; por isso Continue so nesta chamada.
    $eapAnterior = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $git.Source -C $OutDir rev-parse --is-inside-work-tree 2>$null | Out-Null
    $codigoGit = $LASTEXITCODE
    $ErrorActionPreference = $eapAnterior
    if ($codigoGit -eq 0) { Fail "-OutDir esta dentro de um repositorio git ($OutDir); use um diretorio fora do repo" }
}

if (-not $criadoPeloScript) {
    $vazio = -not (Get-ChildItem -LiteralPath $OutDir -Force | Select-Object -First 1)
    if ((Split-Path -Leaf $OutDir) -ne 'backups_postgis' -or -not $vazio) {
        Fail "-OutDir ($OutDir) ja existe e nao e um diretorio dedicado vazio chamado backups_postgis. A ACL dele NAO foi alterada. Informe um diretorio NOVO (o script cria) ou omita -OutDir (cria $HOME\backups_postgis\<data>)."
    }
}

# ACL: remove heranca e deixa so o usuario atual com controle total.
$eu = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
& icacls.exe $OutDir /inheritance:r /grant:r "${eu}:(OI)(CI)F" | Out-Null
if ($LASTEXITCODE -ne 0) { Fail "nao foi possivel restringir a permissao de $OutDir (icacls)" }

$arquivo = Join-Path $OutDir "mapas_backup_$carimbo.sql"
$parcial = "$arquivo.parcial"
if ((Test-Path -LiteralPath $arquivo) -or (Test-Path -LiteralPath $parcial)) {
    Fail "ja existe $arquivo; aguarde 1 s e rode de novo"
}

# ---------------------------------------------------------------------
# Dump e conferencia
# ---------------------------------------------------------------------
$ok = $false
try {
    Info "pg_dump -t public.mapas em ${PgHost}:$Port (role $User, base $Database, sslmode=$sslMode)"
    & $pgDump.Source -h $PgHost -p $Port -U $User -d $Database -t public.mapas -f $parcial
    $codigo = $LASTEXITCODE
    if ($codigo -ne 0) { Fail "pg_dump falhou (codigo $codigo). Nada foi gravado." }

    if (-not (Test-Path -LiteralPath $parcial) -or (Get-Item -LiteralPath $parcial).Length -eq 0) {
        Fail 'dump vazio. Nada foi gravado.'
    }
    if (-not (Select-String -LiteralPath $parcial -Pattern '^COPY public\.mapas ' -Quiet)) {
        Fail "dump sem 'COPY public.mapas' (tabela nao exportada). Nada foi gravado."
    }
    if (-not (Select-String -LiteralPath $parcial -Pattern 'PostgreSQL database dump complete' -SimpleMatch -Quiet)) {
        Fail "dump incompleto (sem o rodape 'dump complete'). Nada foi gravado."
    }

    $linhas = 0
    $dentro = $false
    foreach ($l in [System.IO.File]::ReadLines($parcial)) {
        if (-not $dentro) {
            if ($l.StartsWith('COPY public.mapas ')) { $dentro = $true }
        } elseif ($l -eq '\.') {
            $dentro = $false
        } else {
            $linhas++
        }
    }

    Move-Item -LiteralPath $parcial -Destination $arquivo
    $ok = $true
}
finally {
    if (-not $ok -and (Test-Path -LiteralPath $parcial)) {
        Remove-Item -LiteralPath $parcial -Force
    }
    if (-not $ok -and $script:DirCriado -and (Test-Path -LiteralPath $script:DirCriado) -and
        -not (Get-ChildItem -LiteralPath $script:DirCriado -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $script:DirCriado -Force
    }
}

Info "Backup OK: $arquivo"
Info "Linhas de public.mapas no dump: $linhas (compare com SELECT count(*) FROM public.mapas;)"
Info ("SHA-256: " + (Get-FileHash -LiteralPath $arquivo -Algorithm SHA256).Hash.ToLowerInvariant())
Info 'Guarde o arquivo fora do repositorio e do OneDrive. Restauracao: ver database/backup_mapas.sh.'
exit 0
