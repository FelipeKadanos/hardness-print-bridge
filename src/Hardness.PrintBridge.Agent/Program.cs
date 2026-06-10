using Hardness.PrintBridge.Agent.Application;
using Hardness.PrintBridge.Agent.Configuration;
using Hardness.PrintBridge.Agent.Infrastructure.Callback;
using Hardness.PrintBridge.Agent.Infrastructure.Printing;
using Hardness.PrintBridge.Agent.Infrastructure.Queue;
using Hardness.PrintBridge.Agent.Infrastructure.Runtime;
using Microsoft.Extensions.Options;
using Serilog;
using Hardness.PrintBridge.Agent;
using Hardness.PrintBridge.Contracts.Runtime;
using Microsoft.Extensions.FileProviders;
using Serilog.Events;
using System.Threading;

using var singleInstanceMutex = new Mutex(initiallyOwned: true, @"Global\HardnessPrintBridgeAgent", out var createdNew);
if (!createdNew) {
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
var agentConfigurationPath = RuntimePaths.GetAgentConfigurationPath();
var agentConfigurationDirectory = Path.GetDirectoryName(agentConfigurationPath);
if (!string.IsNullOrWhiteSpace(agentConfigurationDirectory)) {
    builder.Configuration.AddJsonFile(
        new PhysicalFileProvider(agentConfigurationDirectory),
        Path.GetFileName(agentConfigurationPath),
        optional: true,
        reloadOnChange: true);
}
builder.Services.AddWindowsService(options => {
    options.ServiceName = "Hardness Print Bridge Agent";
});

builder.Services
    .AddOptions<PrintBridgeOptions>()
    .Bind(builder.Configuration.GetSection(PrintBridgeOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        options => Uri.TryCreate(options.HardnessCallbackUrl, UriKind.Absolute, out _),
        "PrintBridge:HardnessCallbackUrl must be a valid absolute URL.")
    .Validate(
        options => !options.RemoteSourceEnabled || Uri.TryCreate(options.RemoteListUrl, UriKind.Absolute, out _),
        "PrintBridge:RemoteListUrl must be a valid absolute URL when remote source is enabled.")
    .Validate(
        options => !options.RemoteSourceEnabled || !string.IsNullOrWhiteSpace(options.RemoteDownloadUrlTemplate),
        "PrintBridge:RemoteDownloadUrlTemplate is required when remote source is enabled.")
    .ValidateOnStart();

var agentLogPath = RuntimePaths.GetAgentLogPath();
builder.Services.AddSerilog((services, configuration) => {
    configuration
        .MinimumLevel.Is(LogEventLevel.Information)
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .WriteTo.Sink(new FixedSizeSingleFileLogSink(agentLogPath, 10 * 1024 * 1024))
        .Enrich.FromLogContext();

    if (builder.Environment.IsDevelopment()) {
        configuration.WriteTo.Console();
    }
});

builder.Services.AddSingleton<IPrintJobParser, EtqPrintJobParser>();
builder.Services.AddSingleton<IPrinterResolver, WindowsPrinterResolver>();
builder.Services.AddSingleton<IRawPrinterClient, WindowsRawPrinterClient>();
builder.Services.AddSingleton<AgentStatusWriter>();
builder.Services.AddHttpClient<IHardnessCallbackClient, HardnessCallbackClient>();
builder.Services
    .AddHttpClient<IRemoteJobFetcher, RemoteJobFetcher>((serviceProvider, client) => {
        var remoteOptions = serviceProvider
            .GetRequiredService<IOptions<PrintBridgeOptions>>()
            .Value;
        client.Timeout = TimeSpan.FromMilliseconds(remoteOptions.RemoteTimeoutMs);
    })
    .ConfigurePrimaryHttpMessageHandler(serviceProvider => {
        var remoteOptions = serviceProvider
            .GetRequiredService<IOptions<PrintBridgeOptions>>()
            .Value;

        var handler = new HttpClientHandler();
        if (remoteOptions.RemoteAllowInsecureTls) {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        return handler;
    });
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

var options = host.Services.GetRequiredService<IOptions<PrintBridgeOptions>>().Value;
host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Startup")
    .LogInformation(
        "PrintBridge starting with watch path '{WatchPath}', default printer '{DefaultPrinterName}' and log path '{LogPath}'.",
        options.WatchPath,
        options.DefaultPrinterName,
        agentLogPath);

host.Run();
