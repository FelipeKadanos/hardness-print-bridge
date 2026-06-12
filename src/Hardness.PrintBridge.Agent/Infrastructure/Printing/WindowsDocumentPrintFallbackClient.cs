using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.Runtime.Versioning;
using Hardness.PrintBridge.Agent.Application;
using Hardness.PrintBridge.Agent.Domain;

namespace Hardness.PrintBridge.Agent.Infrastructure.Printing;

[SupportedOSPlatform("windows")]
public sealed class WindowsDocumentPrintFallbackClient : IDocumentPrintFallbackClient {
    private static readonly HashSet<string> PdfExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".pdf"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"
    };

    public bool CanPrint(PrintJob printJob) {
        var extension = Path.GetExtension(printJob.SourcePath);
        return PdfExtensions.Contains(extension) || ImageExtensions.Contains(extension);
    }

    public void Print(string printerName, PrintJob printJob) {
        if (!OperatingSystem.IsWindows()) {
            throw new PlatformNotSupportedException("Document print fallback is only supported on Windows.");
        }

        var extension = Path.GetExtension(printJob.SourcePath);
        if (PdfExtensions.Contains(extension)) {
            PrintPdfViaShell(printerName, printJob);
            return;
        }

        if (ImageExtensions.Contains(extension)) {
            PrintImageViaDriver(printerName, printJob);
            return;
        }

        throw new PrintJobProcessingException(
            $"No fallback print route is available for '{printJob.FileName}'.",
            canRetry: false);
    }

    private static void PrintPdfViaShell(string printerName, PrintJob printJob) {
        try {
            using var process = Process.Start(new ProcessStartInfo {
                FileName = printJob.SourcePath,
                Verb = "printto",
                Arguments = $"\"{printerName}\"",
                CreateNoWindow = true,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(printJob.SourcePath) ?? AppContext.BaseDirectory
            });

            if (process is null) {
                throw new PrintJobProcessingException(
                    $"Failed to start PDF fallback print for '{printJob.FileName}'.",
                    canRetry: true);
            }

            if (!process.WaitForExit(15000)) {
                try {
                    process.Kill(entireProcessTree: true);
                } catch {
                    // Best effort only.
                }

                throw new PrintJobProcessingException(
                    $"Timed out waiting for PDF fallback print to start for '{printJob.FileName}'.",
                    canRetry: true);
            }

            if (process.ExitCode != 0) {
                throw new PrintJobProcessingException(
                    $"PDF fallback print for '{printJob.FileName}' failed with exit code {process.ExitCode}.",
                    canRetry: true);
            }
        } catch (Win32Exception ex) {
            throw new PrintJobProcessingException(
                $"PDF fallback print failed for '{printJob.FileName}': {ex.Message}",
                ex,
                canRetry: false);
        }
    }

    private static void PrintImageViaDriver(string printerName, PrintJob printJob) {
        try {
            using var image = Image.FromFile(printJob.SourcePath);
            using var printDocument = new PrintDocument();
            printDocument.PrinterSettings.PrinterName = printerName;
            printDocument.DocumentName = printJob.FileName;

            if (!printDocument.PrinterSettings.IsValid) {
                throw new PrintJobProcessingException(
                    $"Image fallback print could not validate printer '{printerName}'.",
                    canRetry: true);
            }

            printDocument.PrintPage += (_, args) => {
                if (args.Graphics is null) {
                    throw new PrintJobProcessingException(
                        $"Image fallback print could not acquire drawing surface for '{printJob.FileName}'.",
                        canRetry: true);
                }

                var destination = FitImageBounds(image.Size, args.MarginBounds);
                args.Graphics.DrawImage(image, destination);
                args.HasMorePages = false;
            };

            printDocument.Print();
        } catch (PrintJobProcessingException) {
            throw;
        } catch (Exception ex) {
            throw new PrintJobProcessingException(
                $"Image fallback print failed for '{printJob.FileName}': {ex.Message}",
                ex,
                canRetry: true);
        }
    }

    private static Rectangle FitImageBounds(Size imageSize, Rectangle bounds) {
        var ratioX = (double)bounds.Width / imageSize.Width;
        var ratioY = (double)bounds.Height / imageSize.Height;
        var ratio = Math.Min(ratioX, ratioY);

        var width = Math.Max(1, (int)(imageSize.Width * ratio));
        var height = Math.Max(1, (int)(imageSize.Height * ratio));
        var x = bounds.X + ((bounds.Width - width) / 2);
        var y = bounds.Y + ((bounds.Height - height) / 2);
        return new Rectangle(x, y, width, height);
    }
}
