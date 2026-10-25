using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;

namespace QQAIAgent.Infrastructure.Database;

internal sealed class SqliteDatabase : IDatabase
{
    #region fields
    private readonly string _connectionString;
    #endregion

    #region constructors
    internal SqliteDatabase(string connectionString)
    {
        _connectionString = connectionString;
    }
    #endregion

    #region methods
    public async Task<List<T>> QueryAsync<T>(string sql, object parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        // 默认缓冲查询结果，在连接释放前完成读取，AsList 复用 Dapper 已生成的列表。
        var results = await connection.QueryAsync<T>(command);
        return results.AsList();
    }

    public async Task<int> ExecuteAsync(string sql, object parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(command);
    }

    public async Task<T> ExecuteScalarAsync<T>(string sql, object parameters = null, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<T>(command);
    }

    public DbConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        // 扩展按连接注册，Microsoft.Data.Sqlite 会在打开及重新打开连接时加载。
        connection.LoadExtension(Path.Combine(AppContext.BaseDirectory, "Third", "Sqlite", "vec0.dll"));
        return connection;
    }
    #endregion
}
