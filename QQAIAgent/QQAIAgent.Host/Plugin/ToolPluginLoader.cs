using QQAIAgent.Abstractions.Tools;

namespace QQAIAgent.Host.Plugin;

internal static class ToolPluginLoader
{
    #region methods
    public static void RegisterTools(IServiceCollection services, string pluginsDirectory)
    {
        if (Directory.Exists(pluginsDirectory) == false)
        {
            return;
        }

        foreach (var pluginDirectory in Directory.EnumerateDirectories(pluginsDirectory))
        {
            var pluginName = Path.GetFileName(pluginDirectory);
            var pluginPath = Path.Combine(pluginDirectory, $"{pluginName}.dll");
            if (File.Exists(pluginPath) == false)
            {
                continue;
            }

            var loadContext = new ToolPluginLoadContext(pluginPath);
            var assembly = loadContext.LoadFromAssemblyPath(pluginPath);

            // 一个插件程序集可以包含多个工具，依赖 DLL 由加载上下文解析，不作为入口扫描。
            foreach (var type in assembly.GetExportedTypes())
            {
                if (type.IsClass == false || type.IsAbstract || type.ContainsGenericParameters ||
                    typeof(ITool).IsAssignableFrom(type) == false)
                {
                    continue;
                }

                services.AddSingleton(typeof(ITool), type);
            }
        }
    }
    #endregion
}
