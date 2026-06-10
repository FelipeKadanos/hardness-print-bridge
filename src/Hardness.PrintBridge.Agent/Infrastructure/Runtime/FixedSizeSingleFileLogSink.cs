using System.Text;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace Hardness.PrintBridge.Agent.Infrastructure.Runtime;

internal sealed class FixedSizeSingleFileLogSink : ILogEventSink, IDisposable {
    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}";

    private readonly string _logPath;
    private readonly long _maxFileSizeBytes;
    private readonly object _sync = new();
    private readonly MessageTemplateTextFormatter _formatter = new(OutputTemplate, null);
    private readonly UTF8Encoding _encoding = new(encoderShouldEmitUTF8Identifier: false);
    private FileStream? _stream;
    private StreamWriter? _writer;

    public FixedSizeSingleFileLogSink(string logPath, long maxFileSizeBytes) {
        _logPath = logPath;
        _maxFileSizeBytes = maxFileSizeBytes;

        var directory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        OpenWriter();
    }

    public void Emit(LogEvent logEvent) {
        lock (_sync) {
            EnsureWriter();

            using var payloadWriter = new StringWriter();
            _formatter.Format(logEvent, payloadWriter);
            var payload = payloadWriter.ToString();
            var payloadBytes = _encoding.GetByteCount(payload);

            if (_stream is not null && _stream.Length > 0 && _stream.Length + payloadBytes > _maxFileSizeBytes) {
                TruncateInPlace();
            }

            _writer!.Write(payload);
            _writer.Flush();
            _stream!.Flush(flushToDisk: true);
        }
    }

    public void Dispose() {
        lock (_sync) {
            _writer?.Dispose();
            _stream?.Dispose();
            _writer = null;
            _stream = null;
        }
    }

    private void EnsureWriter() {
        if (_stream is not null && _writer is not null) {
            return;
        }

        OpenWriter();
    }

    private void OpenWriter() {
        _stream = new FileStream(
            _logPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);
        _stream.Seek(0, SeekOrigin.End);
        _writer = new StreamWriter(_stream, _encoding, leaveOpen: true) {
            AutoFlush = true
        };
    }

    private void TruncateInPlace() {
        _writer?.Flush();
        _stream?.Flush(flushToDisk: true);
        _stream?.SetLength(0);
        _stream?.Seek(0, SeekOrigin.Begin);
    }
}
