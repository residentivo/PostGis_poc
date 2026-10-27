using ApiMapas.Infraestrutura.Configuracao;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ApiMapas.Infraestrutura.Dados;

/// <summary>R-1 / C4 / RS-20: confere, uma vez na subida, se a role e a tabela permitem ligar a escrita.</summary>
public interface IVerificadorRoleEscrita
{
    /// <summary>
    /// Devolve <see cref="MotivoEscritaDesligada.Nenhum"/> quando a role conectada nao tem nenhum
    /// atributo ou privilegio a mais (colunas 0 a 7 e 10 a 13 da V1 = false) e public.mapas ja tem a
    /// PK exata em id GENERATED ALWAYS (colunas 8 e 9 = true); <see cref="MotivoEscritaDesligada.RolePrivilegiada"/>
    /// quando sobra privilegio; <see cref="MotivoEscritaDesligada.SemChavePrimaria"/> quando falta a PK;
    /// <see cref="MotivoEscritaDesligada.VerificacaoFalhou"/> quando o banco nao responde ou a consulta
    /// falha. Nunca lanca por falha de banco.
    /// </summary>
    Task<MotivoEscritaDesligada> VerificarAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Implementacao Npgsql da checagem R-1 / RS-20 (consulta V1 de api_mapas/sql/consultas_referencia.sql),
/// na conexao de ESCRITA. SQL constante, sem parametros, somente leitura do catalogo. Com qualquer
/// resultado diferente de <see cref="MotivoEscritaDesligada.Nenhum"/>, o Program desliga a escrita e
/// chama DataSource.Clear() (C3).
/// </summary>
public sealed partial class VerificadorRoleEscrita : IVerificadorRoleEscrita
{
    /// <summary>
    /// V1 (RS-20, D-N10, PO5 achado 3), copiada EXATAMENTE da referencia. Colunas: 0 rolsuper,
    /// 1 rolbypassrls, 2 rolcreaterole, 3 pode_truncate, 4 escreve_tudo, 5 dona_tabela, 6 pode_delete,
    /// 7 insere_id, 8 pk_exata, 9 id_always, 10 atualiza_id, 11 rolcreatedb, 12 rolreplication,
    /// 13 membro_de_role. A escrita so liga com 0 a 7 = false, 8 e 9 = true e 10 a 13 = false.
    /// </summary>
    internal const string SqlAtributosRole =
        "SELECT r.rolsuper, r.rolbypassrls, r.rolcreaterole, " +
        "has_table_privilege(current_user, 'public.mapas', 'TRUNCATE') AS pode_truncate, " +
        "pg_has_role(current_user, 'pg_write_all_data', 'USAGE') AS escreve_tudo, " +
        "EXISTS (SELECT 1 FROM pg_catalog.pg_class AS c WHERE c.oid = 'public.mapas'::regclass AND c.relowner = r.oid) AS dona_tabela, " +
        "has_table_privilege(current_user, 'public.mapas', 'DELETE') AS pode_delete, " +
        "has_column_privilege(current_user, 'public.mapas', 'id', 'INSERT') AS insere_id, " +
        "EXISTS (SELECT 1 FROM pg_catalog.pg_constraint AS k JOIN pg_catalog.pg_index AS i ON i.indexrelid = k.conindid " +
        "JOIN pg_catalog.pg_attribute AS a ON a.attrelid = k.conrelid AND a.attname = 'id' AND NOT a.attisdropped " +
        "WHERE k.conrelid = 'public.mapas'::regclass AND k.contype = 'p' AND k.conname = 'mapas_pkey' AND k.conkey = ARRAY[a.attnum] " +
        "AND NOT k.condeferrable AND i.indisvalid AND i.indisready AND i.indisunique AND i.indisprimary AND i.indpred IS NULL " +
        "AND i.indexprs IS NULL) AS pk_exata, " +
        "EXISTS (SELECT 1 FROM pg_catalog.pg_attribute AS a WHERE a.attrelid = 'public.mapas'::regclass AND a.attname = 'id' " +
        "AND NOT a.attisdropped AND a.attidentity = 'a') AS id_always, " +
        "has_column_privilege(current_user, 'public.mapas', 'id', 'UPDATE') AS atualiza_id, " +
        "r.rolcreatedb, r.rolreplication, " +
        "EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members AS m WHERE m.member = r.oid) AS membro_de_role " +
        "FROM pg_catalog.pg_roles AS r WHERE r.rolname = current_user;";

    /// <summary>Quantidade de colunas da V1.</summary>
    internal const int ColunasV1 = 14;

    private readonly FonteDadosEscrita _fonte;
    private readonly ILogger<VerificadorRoleEscrita> _log;

    public VerificadorRoleEscrita(FonteDadosEscrita fonte, ILogger<VerificadorRoleEscrita> log)
    {
        ArgumentNullException.ThrowIfNull(fonte);
        ArgumentNullException.ThrowIfNull(log);
        _fonte = fonte;
        _log = log;
    }

    public async Task<MotivoEscritaDesligada> VerificarAsync(CancellationToken cancellationToken)
    {
        try
        {
            LogVerificando();
            var conexao = await _fonte.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using (conexao.ConfigureAwait(false))
            {
                var comando = new NpgsqlCommand(SqlAtributosRole, conexao);
                await using (comando.ConfigureAwait(false))
                {
                    var leitor = await comando.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    await using (leitor.ConfigureAwait(false))
                    {
                        if (!await leitor.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            LogSemLinha();
                            return MotivoEscritaDesligada.VerificacaoFalhou;
                        }

                        var superusuario = leitor.GetBoolean(0);
                        var bypassRls = leitor.GetBoolean(1);
                        var createRole = leitor.GetBoolean(2);
                        var truncate = leitor.GetBoolean(3);
                        var escreveTudo = leitor.GetBoolean(4);
                        var donaTabela = leitor.GetBoolean(5);
                        var podeDelete = leitor.GetBoolean(6);
                        var insereId = leitor.GetBoolean(7);
                        var pkExata = leitor.GetBoolean(8);
                        var idAlways = leitor.GetBoolean(9);
                        var atualizaId = leitor.GetBoolean(10);
                        var createDb = leitor.GetBoolean(11);
                        var replication = leitor.GetBoolean(12);
                        var membroDeRole = leitor.GetBoolean(13);
                        LogAtributos(superusuario, bypassRls, createRole, truncate, escreveTudo, donaTabela, podeDelete, insereId, pkExata, idAlways);
                        LogAtributosExtras(atualizaId, createDb, replication, membroDeRole);

                        if (superusuario || bypassRls || createRole || truncate || escreveTudo || donaTabela || podeDelete || insereId
                            || atualizaId || createDb || replication || membroDeRole)
                        {
                            LogPrivilegioSobrando();
                            return MotivoEscritaDesligada.RolePrivilegiada;
                        }

                        if (!pkExata || !idAlways)
                        {
                            LogSemPk(pkExata, idAlways);
                            return MotivoEscritaDesligada.SemChavePrimaria;
                        }

                        LogAprovada();
                        return MotivoEscritaDesligada.Nenhum;
                    }
                }
            }
        }
#pragma warning disable CA1031 // C2: a checagem nunca derruba a API; qualquer falha desliga a escrita.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // So tipo e SqlState (RP-10); a escrita fica desligada.
            var sqlState = (ex as PostgresException)?.SqlState ?? (ex as NpgsqlException)?.SqlState ?? "-";
            LogFalha(ex.GetType().Name, sqlState);
            return MotivoEscritaDesligada.VerificacaoFalhou;
        }
    }

    [LoggerMessage(EventId = 2201, Level = LogLevel.Debug, Message = "R-1/RS-20: conferindo a role de escrita e a PRIMARY KEY de public.mapas (V1).")]
    private partial void LogVerificando();

    [LoggerMessage(EventId = 2202, Level = LogLevel.Debug, Message = "R-1/RS-20: rolsuper={Super}, rolbypassrls={BypassRls}, rolcreaterole={CreateRole}, truncate={Truncate}, pg_write_all_data={EscreveTudo}, dona de public.mapas={DonaTabela}, delete={Delete}, insert em id={InsereId}, pk exata={PkExata}, id always={IdAlways}.")]
    private partial void LogAtributos(bool super, bool bypassRls, bool createRole, bool truncate, bool escreveTudo, bool donaTabela, bool delete, bool insereId, bool pkExata, bool idAlways);

    [LoggerMessage(EventId = 2208, Level = LogLevel.Debug, Message = "PO5: update em id={AtualizaId}, rolcreatedb={CreateDb}, rolreplication={Replication}, membro de outra role={MembroDeRole}.")]
    private partial void LogAtributosExtras(bool atualizaId, bool createDb, bool replication, bool membroDeRole);

    [LoggerMessage(EventId = 2203, Level = LogLevel.Warning, Message = "R-1: pg_roles nao devolveu a role da sessao; escrita desligada.")]
    private partial void LogSemLinha();

    [LoggerMessage(EventId = 2204, Level = LogLevel.Warning, Message = "R-1: falha ao conferir a role de escrita (tipo {TipoErro}, SqlState {SqlState}); escrita desligada.")]
    private partial void LogFalha(string tipoErro, string sqlState);

    [LoggerMessage(EventId = 2205, Level = LogLevel.Warning, Message = "RS-20: a role de escrita tem atributo ou privilegio a mais (inclui DELETE em public.mapas, INSERT ou UPDATE na coluna id, CREATEDB, REPLICATION ou membership em outra role); escrita (PUT e POST) desligada.")]
    private partial void LogPrivilegioSobrando();

    [LoggerMessage(EventId = 2206, Level = LogLevel.Warning, Message = "RS-20: public.mapas sem a PRIMARY KEY exata em id (pk exata={PkExata}, id always={IdAlways}); migracao nao aplicada; escrita (PUT e POST) desligada.")]
    private partial void LogSemPk(bool pkExata, bool idAlways);

    [LoggerMessage(EventId = 2207, Level = LogLevel.Debug, Message = "R-1/RS-20: role de escrita e PRIMARY KEY aprovadas.")]
    private partial void LogAprovada();
}
