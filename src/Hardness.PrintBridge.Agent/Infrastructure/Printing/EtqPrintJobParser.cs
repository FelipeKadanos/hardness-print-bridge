using Hardness.PrintBridge.Agent.Application;
using Hardness.PrintBridge.Agent.Domain;

namespace Hardness.PrintBridge.Agent.Infrastructure.Printing;

public sealed class EtqPrintJobParser : IPrintJobParser {
    public PrintJob ParseEtq(string filePath) {
        if (!File.Exists(filePath)) {
            throw new FileNotFoundException("ETQ file not found.", filePath);
        }

        var fileName = Path.GetFileName(filePath);
        var requestedPrinter = TryExtractRequestedPrinter(fileName);
        var content = File.ReadAllText(filePath).Trim();
        if (string.IsNullOrWhiteSpace(content)) {
            throw new InvalidDataException($"ETQ payload is empty for file '{fileName}'.");
        }

        var byteTokens = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var payload = new byte[byteTokens.Length];

        for (var i = 0; i < byteTokens.Length; i++) {
            if (!byte.TryParse(byteTokens[i], out var parsedByte)) {
                throw new InvalidDataException(
                    $"Invalid ETQ byte token '{byteTokens[i]}' at position {i} in file '{fileName}'.");
            }

            payload[i] = parsedByte;
        }

        return new PrintJob {
            FileName = fileName,
            SourcePath = filePath,
            RawPayload = payload,
            RequestedPrinter = requestedPrinter,
            Metadata = new Dictionary<string, string> {
                ["format"] = ".etq",
                ["tokenCount"] = byteTokens.Length.ToString(),
                ["requestedPrinter"] = requestedPrinter ?? string.Empty
            }
        };
    }

    private static string? TryExtractRequestedPrinter(string fileName) {
        // MVP convention: <job-name>__printer=<PrinterName>.etq
        const string marker = "__printer=";
        var markerIndex = fileName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) {
            return null;
        }

        var printerStart = markerIndex + marker.Length;
        var extensionIndex = fileName.LastIndexOf('.');
        if (extensionIndex <= printerStart) {
            return null;
        }

        var extracted = fileName[printerStart..extensionIndex].Trim();
        return string.IsNullOrWhiteSpace(extracted) ? null : extracted;
    }
}
