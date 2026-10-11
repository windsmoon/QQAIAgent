using Microsoft.Extensions.DependencyInjection;

namespace QQAIAgent.Infrastructure.Database;

public static class DatabaseServiceCollectionExtensions
{
    #region methods
    /// <summary>
    /// 注册 SQLite 和 Dapper 数据库 API，每次操作使用独立连接。
    /// 连接字符串指定数据库位置；使用文件数据库时，调用方负责准备目录及其写入权限。
    /// 注册过程不打开数据库或创建表。
    /// </summary>
    public static IServiceCollection AddSqliteDatabase(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IDatabase>(_ => new SqliteDatabase(connectionString));
        return services;
    }
    #endregion
}
