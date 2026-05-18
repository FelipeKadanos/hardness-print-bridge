using Hardness.PrintBridge.Agent.Application;
using Hardness.PrintBridge.Agent.Configuration;
using Hardness.PrintBridge.Agent.Infrastructure.Callback;
using Hardness.PrintBridge.Agent.Infrastructure.Printing;
using Microsoft.Extensions.Options;
using Serilog;
using Hardness.PrintBridge.Agent;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services
    .AddOptions<PrintBridgeOptions>()
    .Bind(builder.Configuration.GetSection(PrintBridgeOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        options => Uri.TryCreate(options.HardnessCallbackUrl, UriKind.Absolute, out _),
        "PrintBridge:HardnessCallbackUrl must be a valid absolute URL.")
    .ValidateOnStart();

builder.Services.AddSerilog((services, configuration) => {
    configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

builder.Services.AddSingleton<IPrintJobParser, EtqPrintJobParser>();
builder.Services.AddSingleton<IPrinterResolver, WindowsPrinterResolver>();
builder.Services.AddSingleton<IRawPrinterClient, WindowsRawPrinterClient>();
builder.Services.AddHttpClient<IHardnessCallbackClient, HardnessCallbackClient>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

var options = host.Services.GetRequiredService<IOptions<PrintBridgeOptions>>().Value;
host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Startup")
    .LogInformation(
        "PrintBridge starting with watch path '{WatchPath}' and default printer '{DefaultPrinterName}'.",
        options.WatchPath,
        options.DefaultPrinterName);

host.Run();
