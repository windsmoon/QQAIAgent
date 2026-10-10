using System.Reflection;

namespace QQAIAgent.Agent;

public static class ChatModelLoader
{
    #region methods
    /// <summary>通过模型类型的公开无参构造函数创建实例；DLL 的相对路径以宿主程序目录为基准。</summary>
    public static IChatModel Load(string assemblyPath, string typeName)
    {
        // 复用宿主中的接口程序集，并从模型 DLL 所在目录解析其托管依赖。
        var assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath, AppContext.BaseDirectory));
        var modelType = assembly.GetType(typeName, throwOnError: true)!;
        if (typeof(IChatModel).IsAssignableFrom(modelType) == false)
        {
            throw new InvalidOperationException($"配置的模型类型没有实现 IChatModel：{typeName}");
        }

        return (IChatModel)Activator.CreateInstance(modelType)!;
    }
    #endregion
}
