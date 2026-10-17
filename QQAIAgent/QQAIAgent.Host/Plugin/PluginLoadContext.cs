using System.Reflection;
using System.Runtime.Loader;
using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Messaging;
using QQAIAgent.Schedule;

namespace QQAIAgent.Host.Plugin;

internal sealed class PluginLoadContext(string pluginPath) : AssemblyLoadContext
{
    #region fields
    private readonly AssemblyDependencyResolver _resolver = new(pluginPath);
    // 插件接口、执行参数和可注入的公共服务契约必须与宿主使用同一程序集。
    private static readonly List<Assembly> SharedAssemblies =
    [
        typeof(ITool).Assembly,
        typeof(IScheduleHandler).Assembly,
        typeof(IMessageSender).Assembly,
        typeof(ILogger).Assembly
    ];
    #endregion

    #region methods
    protected override Assembly Load(AssemblyName assemblyName)
    {
        foreach (var contractAssembly in SharedAssemblies)
        {
            if (assemblyName.Name == contractAssembly.GetName().Name)
            {
                // 接口必须使用宿主已加载的程序集，避免插件中的接口成为另一种类型。
                return contractAssembly;
            }
        }

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is null ? nint.Zero : LoadUnmanagedDllFromPath(libraryPath);
    }
    #endregion
}
