namespace QQAIAgent.Infrastructure.Runtime;

public static class ApplicationPaths
{
    #region properties
    // 发布脚本将主程序集中到 App；配置、插件、第三方资源和数据仍以发布根目录为基准。
    // 普通构建和源码调试继续使用可执行文件所在目录。
    public static string RootDirectory { get; } =
        string.Equals(new DirectoryInfo(AppContext.BaseDirectory).Name, "App", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath("..", AppContext.BaseDirectory)
            : AppContext.BaseDirectory;
    #endregion
}
