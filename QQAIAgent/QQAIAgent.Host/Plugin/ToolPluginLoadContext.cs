using System.Reflection;
using System.Runtime.Loader;
using QQAIAgent.Abstractions.Tool;

namespace QQAIAgent.Host.Plugin;

internal sealed class ToolPluginLoadContext(string pluginPath) : AssemblyLoadContext
{
    #region fields
    private readonly AssemblyDependencyResolver _resolver = new(pluginPath);
    #endregion

    #region methods
    protected override Assembly Load(AssemblyName assemblyName)
    {
        var contractAssembly = typeof(ITool).Assembly;
        if (assemblyName.Name == contractAssembly.GetName().Name)
        {
            // 接口必须使用宿主已加载的程序集，避免插件中的 ITool 成为另一种类型。
            return contractAssembly;
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
