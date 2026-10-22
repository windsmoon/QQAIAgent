using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using QQAIAgent.Abstractions.Character;
using QQAIAgent.Abstractions.Messages;
using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Abstractions.Video;
using QQAIAgent.Agent;
using QQAIAgent.Agent.Chat;
using QQAIAgent.Agent.Config;
using QQAIAgent.Agent.Statistics;
using QQAIAgent.DeepSeek;
using QQAIAgent.Host;
using QQAIAgent.Host.Logging;
using QQAIAgent.Host.Plugin;
using QQAIAgent.Host.Schedule;
using QQAIAgent.Infrastructure.Character;
using QQAIAgent.Infrastructure.Database;
using QQAIAgent.Infrastructure.Image;
using QQAIAgent.Infrastructure.Messages;
using QQAIAgent.Infrastructure.Tokenization;
using QQAIAgent.Infrastructure.Video;
using QQAIAgent.Messaging;
using QQAIAgent.NapCat;
using QQAIAgent.NapCat.Config;
using QQAIAgent.Schedule;

var builder = Host.CreateApplicationBuilder(args);

var logFilePath = Path.GetFullPath(builder.Configuration["Logging:File:Path"] ?? "Log/qqaiaagent.log", AppContext.BaseDirectory);
builder.Services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(logFilePath));

var databasePath = Path.GetFullPath(builder.Configuration["Database:Path"] ?? "Data/qqaiaagent.sqlite3", AppContext.BaseDirectory);
Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? AppContext.BaseDirectory);
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath,
    ForeignKeys = true
}.ToString();
builder.Services.AddSqliteDatabase(connectionString);
builder.Services.AddSingleton<RequestStatistics>();
builder.Services.AddSingleton(new ImageFileStore(Path.Combine(AppContext.BaseDirectory, "Data")));
builder.Services.AddSingleton(services => new TextToImageStore(Path.Combine(AppContext.BaseDirectory, "Data"),
    services.GetRequiredService<NapCatConfig>().BotAccount));
builder.Services.AddSingleton(new VideoCacheStore(Path.Combine(AppContext.BaseDirectory, "Data")));
builder.Services.AddSingleton<IVideoCache>(services => services.GetRequiredService<VideoCacheStore>());

var messagesDirectoryPath = Path.GetFullPath(builder.Configuration["Database:MessagesDirectory"] ?? "Data/Messages", AppContext.BaseDirectory);
builder.Services.AddSingleton(services => new GroupMessageStore(messagesDirectoryPath,
    services.GetRequiredService<TextToImageStore>(), services.GetRequiredService<Tokenizer>()));
builder.Services.AddSingleton<IGroupMessageQuery>(services => services.GetRequiredService<GroupMessageStore>());
builder.Services.AddSingleton(new ForwardMessageStore(messagesDirectoryPath));

var configDirectory = Path.Combine(AppContext.BaseDirectory, "Config");
var tokenizerConfigPath = Path.Combine(configDirectory, "tokenizer.local.json");
var tokenizerConfig = JsonSerializer.Deserialize<TokenizerConfig>(File.ReadAllText(tokenizerConfigPath))
    ?? throw new InvalidDataException($"分词配置不能为 null：{tokenizerConfigPath}");
// 服务启动时加载词典，所有群的新消息共用同一个分词器。
builder.Services.AddSingleton(new Tokenizer(tokenizerConfig, configDirectory));

builder.Services.AddSingleton<ICharacterStore>(new CharacterStore(
    Path.Combine(Path.GetDirectoryName(databasePath) ?? AppContext.BaseDirectory, "Character"),
    Path.Combine(configDirectory, "Default.txt")));
var napCatConfigPath = Path.Combine(configDirectory, "napcat.local.json");
var napCatConfigJson = File.ReadAllText(napCatConfigPath);
var napCatConfig = JsonSerializer.Deserialize<NapCatConfig>(napCatConfigJson)
    ?? throw new InvalidDataException($"NapCat 配置不能为 null：{napCatConfigPath}");

var agentConfigPath = Path.Combine(configDirectory, "agent.local.json");
var agentConfigJson = File.ReadAllText(agentConfigPath);
var agentConfig = JsonSerializer.Deserialize<AgentConfig>(agentConfigJson)
    ?? throw new InvalidDataException($"Agent 配置不能为 null：{agentConfigPath}");
agentConfig.SystemPrompt = File.ReadAllText(Path.Combine(configDirectory, "system-prompt.local.md"));

var toolsConfigPath = Path.Combine(configDirectory, "tools.local.json");
var toolsConfigJson = File.ReadAllText(toolsConfigPath);
var toolDefinitions = JsonSerializer.Deserialize<List<ChatToolDefinition>>(toolsConfigJson)
    ?? throw new InvalidDataException($"工具配置不能为 null：{toolsConfigPath}");

var scheduleConfigPath = Path.Combine(configDirectory, "schedule.local.json");
var scheduleConfigJson = File.ReadAllText(scheduleConfigPath);
var scheduleJsonOptions = new JsonSerializerOptions();
scheduleJsonOptions.Converters.Add(new JsonStringEnumConverter());
var scheduleConfig = JsonSerializer.Deserialize<ScheduleConfig>(scheduleConfigJson, scheduleJsonOptions)
    ?? throw new InvalidDataException($"定时任务配置不能为 null：{scheduleConfigPath}");

builder.Services.AddSingleton(napCatConfig);
builder.Services.AddSingleton(agentConfig);
builder.Services.AddSingleton(scheduleConfig);
builder.Services.AddSingleton<ScheduleService>();
builder.Services.AddSingleton<IReadOnlyList<ChatToolDefinition>>(toolDefinitions);
builder.Services.AddSingleton<IReadOnlyDictionary<string, ChatToolDefinition>>(
    toolDefinitions.ToDictionary(definition => definition.Name, StringComparer.Ordinal));
builder.Services.AddSingleton(services => new NapCatClient(napCatConfig,
    services.GetRequiredService<ForwardMessageStore>(), services.GetRequiredService<TextToImageStore>(),
    agentConfig.TextToImageThreshold, services.GetRequiredService<ILogger<NapCatClient>>()));
builder.Services.AddSingleton<IMessageSender>(services => services.GetRequiredService<NapCatClient>());
builder.Services.AddSingleton<IMessageReader>(services => services.GetRequiredService<NapCatClient>());
builder.Services.AddSingleton<RkeyCache>();
builder.Services.AddSingleton<IChatModel, DeepSeekChatModel>();
PluginLoader.RegisterPlugins(builder.Services, Path.Combine(AppContext.BaseDirectory, "Plugins"));
builder.Services.AddSingleton<IScheduleHandler, SendGroupMessageHandler>();
builder.Services.AddSingleton<IScheduleHandler, SummarizeGroupMessagesHandler>();
builder.Services.AddSingleton<IReadOnlyDictionary<string, ITool>>(services =>
    services.GetServices<ITool>().ToDictionary(tool => tool.Name, StringComparer.Ordinal));
builder.Services.AddSingleton<IReadOnlyDictionary<string, IScheduleHandler>>(services =>
    services.GetServices<IScheduleHandler>().ToDictionary(handler => handler.FunctionName, StringComparer.Ordinal));
builder.Services.AddSingleton<ToolExecutor>();
builder.Services.AddSingleton<GroupMessageAgent>(services => new GroupMessageAgent(
    agentConfig, napCatConfig.BotAccount, services.GetRequiredService<IMessageSender>(), services.GetRequiredService<IMessageReader>(),
    services.GetRequiredService<IChatModel>(), services.GetRequiredService<IReadOnlyList<ChatToolDefinition>>(),
    services.GetRequiredService<ToolExecutor>(), services.GetRequiredService<GroupMessageStore>(), services.GetRequiredService<ICharacterStore>(),
    services.GetRequiredService<RequestStatistics>(), services.GetRequiredService<ILogger<GroupMessageAgent>>()));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
#region temp
// 图片缓存已改用独立数据库，删除主数据库中的旧表。
await host.Services.GetRequiredService<IDatabase>().ExecuteAsync("DROP TABLE IF EXISTS image_files;", cancellationToken: CancellationToken.None);
#endregion
await host.Services.GetRequiredService<RequestStatistics>().InitializeAsync(CancellationToken.None);
await host.Services.GetRequiredService<ImageFileStore>().InitializeAsync(CancellationToken.None);
await host.Services.GetRequiredService<TextToImageStore>().InitializeAsync(CancellationToken.None);
await host.Services.GetRequiredService<VideoCacheStore>().InitializeAsync(CancellationToken.None);
await host.Services.GetRequiredService<GroupMessageStore>().InitializeAsync(agentConfig.AllowedGroups, CancellationToken.None);
await host.Services.GetRequiredService<ForwardMessageStore>().InitializeAsync(CancellationToken.None);
host.Run();
