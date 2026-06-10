using Hardness.PrintBridge.App.Services;
using Hardness.PrintBridge.App.Status;
using Hardness.PrintBridge.App.Update;
using Hardness.PrintBridge.App.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
builder.Services.AddSingleton<IAgentConfigurationStore, JsonAgentConfigurationStore>();
builder.Services.AddSingleton<IPrinterCatalogService, WindowsPrinterCatalogService>();
builder.Services.AddSingleton<IStartupService, WindowsStartupService>();
builder.Services.AddSingleton<IAgentStatusSource, JsonAgentStatusSource>();
builder.Services.AddSingleton<IAgentLogSource, FileAgentLogSource>();
builder.Services.AddSingleton<IAgentControlService, AgentControlService>();
builder.Services.AddHttpClient<IUpdateService, GithubReleaseUpdateService>();
builder.Services.AddSingleton<TrayApplicationContext>();

using var host = builder.Build();

ApplicationConfiguration.Initialize();
Application.Run(host.Services.GetRequiredService<TrayApplicationContext>());
