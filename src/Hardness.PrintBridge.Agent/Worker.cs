using Hardness.PrintBridge.Agent.Configuration;
using Microsoft.Extensions.Options;

namespace Hardness.PrintBridge.Agent;

public class Worker(
    ILogger<Worker> logger,
    IOptions<PrintBridgeOptions> options) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var pollInterval = options.Value.PollIntervalMs;

        while (!stoppingToken.IsCancellationRequested) {
            if (logger.IsEnabled(LogLevel.Information)) {
                logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }
}
