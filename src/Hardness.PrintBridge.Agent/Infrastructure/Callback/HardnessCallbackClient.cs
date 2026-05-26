using System.Net.Http.Json;
using Hardness.PrintBridge.Agent.Application;
using Hardness.PrintBridge.Agent.Configuration;
using Microsoft.Extensions.Options;

namespace Hardness.PrintBridge.Agent.Infrastructure.Callback;

public sealed class HardnessCallbackClient(
    HttpClient httpClient,
    IOptions<PrintBridgeOptions> options,
    ILogger<HardnessCallbackClient> logger) : IHardnessCallbackClient {
    private readonly PrintBridgeOptions _options = options.Value;

    public async Task SendAsync(PrintCallbackRequest request, CancellationToken cancellationToken) {
        var delays = new[] { 300, 900, 1800 };
        Exception? lastException = null;

        for (var attempt = 1; attempt <= delays.Length; attempt++) {
            try {
                using var httpRequest = BuildRequest(request);
                using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
                if (response.IsSuccessStatusCode) {
                    logger.LogInformation(
                        "Callback sent for '{FileName}' with status '{Status}' (attempt {Attempt}).",
                        request.FileName,
                        request.Status,
                        attempt);
                    return;
                }

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                lastException = new HttpRequestException(
                    $"Callback returned {(int)response.StatusCode} {response.ReasonPhrase}. Body: {responseBody}");
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                lastException = ex;
            }

            if (attempt < delays.Length) {
                logger.LogWarning(
                    lastException,
                    "Callback attempt {Attempt} failed for '{FileName}'. Retrying...",
                    attempt,
                    request.FileName);
                await Task.Delay(delays[attempt - 1], cancellationToken);
            }
        }

        throw new HttpRequestException(
            $"Failed to send callback for '{request.FileName}' after {delays.Length} attempts.",
            lastException);
    }

    private HttpRequestMessage BuildRequest(PrintCallbackRequest request) {
        var callbackMessage = string.IsNullOrWhiteSpace(request.Message)
            ? "Falha no processamento de impressao."
            : request.Message;

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.HardnessCallbackUrl) {
            Content = JsonContent.Create(new {
                arquivo = request.FileName,
                acao = request.Action,
                status = request.Status,
                mensagem = callbackMessage
            })
        };

        return httpRequest;
    }
}
