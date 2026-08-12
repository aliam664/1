using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace ACModHub.Infrastructure.Services;

public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(4096) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _writer;

    public RollingFileLoggerProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        _writer = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _channel.Writer);

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        try { _writer.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _stop.Cancel();
        _stop.Dispose();
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var line in _channel.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                var file = Path.Combine(_directory, $"acmodhub-{DateTime.UtcNow:yyyyMMdd}.log");
                await File.AppendAllTextAsync(file, line + Environment.NewLine, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly ChannelWriter<string> _writer;
        public FileLogger(string category, ChannelWriter<string> writer) { _category = category; _writer = writer; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var line = $"{DateTimeOffset.Now:O} [{logLevel,-11}] {_category} ({eventId.Id}) {formatter(state, exception)}";
            if (exception is not null) line += Environment.NewLine + exception;
            _writer.TryWrite(line);
        }
    }
}
