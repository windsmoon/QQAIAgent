using System.Text.Json;
using QQAIAgent.Host;
using QQAIAgent.Messaging;
using QQAIAgent.NapCat;
using QQAIAgent.NapCat.Config;

var builder = Host.CreateApplicationBuilder(args);

var napCatConfigPath = Path.Combine(AppContext.BaseDirectory, "Config", "napcat.local.json");
var napCatConfigJson = File.ReadAllText(napCatConfigPath);
var napCatConfig = JsonSerializer.Deserialize<NapCatConfig>(napCatConfigJson)
    ?? throw new InvalidDataException($"NapCat 配置不能为 null：{napCatConfigPath}");

builder.Services.AddSingleton(napCatConfig);
builder.Services.AddSingleton<NapCatClient>();
builder.Services.AddSingleton<IMessageSender>(services => services.GetRequiredService<NapCatClient>());
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
