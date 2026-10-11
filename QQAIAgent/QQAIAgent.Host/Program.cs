using System.Text.Json;
using QQAIAgent.Agent;
using QQAIAgent.Agent.Chat;
using QQAIAgent.Agent.Config;
using QQAIAgent.Agent;
using QQAIAgent.DeepSeek;
using QQAIAgent.Host;
using QQAIAgent.Host.Plugin;
using QQAIAgent.Messaging;
using QQAIAgent.NapCat;
using QQAIAgent.NapCat.Config;

var builder = Host.CreateApplicationBuilder(args);

var configDirectory = Path.Combine(AppContext.BaseDirectory, "Config");
var napCatConfigPath = Path.Combine(configDirectory, "napcat.local.json");
var napCatConfigJson = File.ReadAllText(napCatConfigPath);
var napCatConfig = JsonSerializer.Deserialize<NapCatConfig>(napCatConfigJson)
    ?? throw new InvalidDataException($"NapCat 配置不能为 null：{napCatConfigPath}");

var agentConfigPath = Path.Combine(configDirectory, "agent.local.json");
var agentConfigJson = File.ReadAllText(agentConfigPath);
var agentConfig = JsonSerializer.Deserialize<AgentConfig>(agentConfigJson)
    ?? throw new InvalidDataException($"Agent 配置不能为 null：{agentConfigPath}");

var toolsConfigPath = Path.Combine(configDirectory, "tools.local.json");
var toolsConfigJson = File.ReadAllText(toolsConfigPath);
var toolDefinitions = JsonSerializer.Deserialize<List<ChatToolDefinition>>(toolsConfigJson)
    ?? throw new InvalidDataException($"工具配置不能为 null：{toolsConfigPath}");
var enabledTools = toolDefinitions.Where(tool => tool.Enabled).ToList();

builder.Services.AddSingleton(napCatConfig);
builder.Services.AddSingleton(agentConfig);
builder.Services.AddSingleton<IReadOnlyList<ChatToolDefinition>>(enabledTools);
builder.Services.AddSingleton<NapCatClient>();
builder.Services.AddSingleton<IMessageSender>(services => services.GetRequiredService<NapCatClient>());
builder.Services.AddSingleton<IMessageReader>(services => services.GetRequiredService<NapCatClient>());
builder.Services.AddSingleton<IChatModel, DeepSeekChatModel>();
ToolPluginLoader.RegisterTools(builder.Services, Path.Combine(AppContext.BaseDirectory, "Plugins"));
builder.Services.AddSingleton<ToolExecutor>();
builder.Services.AddSingleton<GroupMessageAgent>(services => new GroupMessageAgent(
    agentConfig, napCatConfig.BotAccount, services.GetRequiredService<IMessageSender>(), services.GetRequiredService<IMessageReader>(),
    services.GetRequiredService<IChatModel>(), services.GetRequiredService<IReadOnlyList<ChatToolDefinition>>(),
    services.GetRequiredService<ToolExecutor>(), services.GetRequiredService<ILogger<GroupMessageAgent>>()));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
