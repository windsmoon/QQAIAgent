using System.Data.Common;

namespace QQAIAgent.Infrastructure.Database;

/// <summary>提供参数化 SQL 执行、对象映射和独立连接入口，不包含业务实体。</summary>
public interface IDatabase
{
    #region methods
    /// <summary>
    /// 执行查询并由 Dapper 将结果列映射到对象成员，也支持基本类型结果。
    /// 在独立连接释放前读取完整结果；筛选、排序和分页由 SQL 指定，数据值通过参数传入。
    /// </summary>
    Task<List<T>> QueryAsync<T>(string sql, object parameters = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 在独立连接上执行建表、删表、增删改等 SQL，返回受影响的行数。
    /// 不负责生成 SQL 或自动建立事务，多条命令需要共同提交时使用 CreateConnection。
    /// </summary>
    Task<int> ExecuteAsync(string sql, object parameters = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取第一行第一列，例如计数或生成的 ID；无结果或 SQL NULL 时返回 T 的默认值。
    /// 值类型需要区分空值和零值时，T 应使用可空值类型。
    /// </summary>
    Task<T> ExecuteScalarAsync<T>(string sql, object parameters = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建尚未打开的独立连接，供调用方使用 Dapper、事务及其他 ADO.NET API。
    /// 调用方负责打开和释放连接；事务内的命令需传入同一个事务，不得并发使用同一个连接。
    /// </summary>
    DbConnection CreateConnection();
    #endregion
}
