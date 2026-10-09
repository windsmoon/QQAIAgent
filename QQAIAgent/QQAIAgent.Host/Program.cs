using System.Text.Json;
using QQAIAgent.Agent;
using QQAIAgent.Agent.Config;
using QQAIAgent.Host;
using QQAIAgent.Messaging;
using QQAIAgent.NapCat;
using QQAIAgent.NapCat.Config;

var builder = Host.CreateApplicationBuilder(args);

var napCatConfigPath = Path.Combine(AppContext.BaseDirectory, "Config", "napcat.local.json");
var napCatConfigJson = File.ReadAllText(napCatConfigPath);
var napCatConfig = JsonSerializer.Deserialize<NapCatConfig>(napCatConfigJson)
    ?? throw new InvalidDataException($"NapCat 配置不能为 null：{napCatConfigPath}");

var agentConfigPath = Path.Combine(AppContext.BaseDirectory, "Config", "agent.local.json");
var agentConfigJson = File.ReadAllText(agentConfigPath);
var agentConfig = JsonSerializer.Deserialize<AgentConfig>(agentConfigJson)
    ?? throw new InvalidDataException($"Agent 配置不能为 null：{agentConfigPath}");

builder.Services.AddSingleton(napCatConfig);
builder.Services.AddSingleton(agentConfig);
builder.Services.AddSingleton<NapCatClient>();
builder.Services.AddSingleton<IMessageSender>(services => services.GetRequiredService<NapCatClient>());
builder.Services.AddSingleton<GroupMessageAgent>(services => new GroupMessageAgent(
    agentConfig, napCatConfig.BotAccount, services.GetRequiredService<IMessageSender>()));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
