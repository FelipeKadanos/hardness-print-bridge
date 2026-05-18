using Hardness.PrintBridge.Agent.Configuration;
using Microsoft.Extensions.Options;
using Serilog;
using Hardness.PrintBridge.Agent;

var builder = Host.CreateApplicationBuilder(args);

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
