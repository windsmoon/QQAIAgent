using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Schedule;

namespace QQAIAgent.Host.Plugin;

internal static class PluginLoader
{
    #region methods
    public static void RegisterPlugins(IServiceCollection services, string pluginsDirectory)
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

            var loadContext = new PluginLoadContext(pluginPath);
            var assembly = loadContext.LoadFromAssemblyPath(pluginPath);

            // 一个插件程序集可以包含多个工具和定时任务，依赖 DLL 由加载上下文解析，不作为入口扫描。
            foreach (var type in assembly.GetExportedTypes())
            {
                if (type.IsClass == false || type.IsAbstract || type.ContainsGenericParameters)
                {
                    continue;
                }

                if (typeof(ITool).IsAssignableFrom(type))
                {
                    services.AddSingleton(typeof(ITool), type);
                }

                if (typeof(IScheduleHandler).IsAssignableFrom(type))
                {
                    services.AddSingleton(typeof(IScheduleHandler), type);
                }
            }
        }
    }
    #endregion
}
