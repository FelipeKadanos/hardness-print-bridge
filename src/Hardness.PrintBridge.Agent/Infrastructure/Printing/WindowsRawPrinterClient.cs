using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hardness.PrintBridge.Agent.Application;

namespace Hardness.PrintBridge.Agent.Infrastructure.Printing;

[SupportedOSPlatform("windows")]
public sealed class WindowsRawPrinterClient : IRawPrinterClient {
    public void Print(string printerName, byte[] payload, string documentName) {
        if (!OperatingSystem.IsWindows()) {
            throw new PlatformNotSupportedException("RAW printing is only supported on Windows.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Length == 0) {
            throw new PrintJobProcessingException("Cannot print an empty RAW payload.");
        }

        if (!OpenPrinter(printerName, out var printerHandle, IntPtr.Zero)) {
            throw new PrintJobProcessingException(
                $"Failed to open printer '{printerName}'. Win32Error={Marshal.GetLastWin32Error()}.");
        }

        try {
            var docInfo = new DocInfo {
                pDocName = documentName,
                pDataType = "RAW"
            };

            if (StartDocPrinter(printerHandle, 1, docInfo) == 0) {
                throw new PrintJobProcessingException(
                    $"Failed to start print document for '{printerName}'. Win32Error={Marshal.GetLastWin32Error()}.");
            }

            try {
                if (!StartPagePrinter(printerHandle)) {
                    throw new PrintJobProcessingException(
                        $"Failed to start page for '{printerName}'. Win32Error={Marshal.GetLastWin32Error()}.");
                }

                try {
                    var unmanagedPointer = Marshal.AllocHGlobal(payload.Length);
                    try {
                        Marshal.Copy(payload, 0, unmanagedPointer, payload.Length);
                        if (!WritePrinter(printerHandle, unmanagedPointer, payload.Length, out var bytesWritten)) {
                            throw new PrintJobProcessingException(
                                $"Failed to write RAW data to '{printerName}'. Win32Error={Marshal.GetLastWin32Error()}.");
                        }

                        if (bytesWritten != payload.Length) {
                            throw new PrintJobProcessingException(
                                $"Incomplete RAW write to '{printerName}'. Expected {payload.Length} bytes, wrote {bytesWritten} bytes.");
                        }
                    } finally {
                        Marshal.FreeHGlobal(unmanagedPointer);
                    }
                } finally {
                    EndPagePrinter(printerHandle);
                }
            } finally {
                EndDocPrinter(printerHandle);
            }
        } finally {
            ClosePrinter(printerHandle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class DocInfo {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDocName = string.Empty;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pOutputFile = string.Empty;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDataType = string.Empty;
    }

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int StartDocPrinter(IntPtr hPrinter, int level, [In] DocInfo pDocInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);
}
