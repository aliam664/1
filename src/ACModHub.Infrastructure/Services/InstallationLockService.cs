using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ACModHub.Core.Interfaces;

namespace ACModHub.Infrastructure.Services;

public sealed class InstallationLockService : IInstallationLockService
{
    private readonly IAppPaths _paths;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _processLocks = new(StringComparer.OrdinalIgnoreCase);

    public InstallationLockService(IAppPaths paths) => _paths = paths;

    public async ValueTask<IAsyncDisposable> AcquireAsync(string gameRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        var normalizedRoot = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var processLock = _processLocks.GetOrAdd(normalizedRoot, _ => new SemaphoreSlim(1, 1));
        await processLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var lockName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot.ToUpperInvariant()))).ToLowerInvariant() + ".lock";
            var lockPath = Path.Combine(_paths.LocksDirectory, lockName);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous | FileOptions.WriteThrough);
                    stream.SetLength(0);
                    var marker = Encoding.UTF8.GetBytes($"{Environment.ProcessId}|{DateTimeOffset.UtcNow:O}|{normalizedRoot}");
                    await stream.WriteAsync(marker, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    return new Lease(stream, processLock);
                }
                catch (IOException)
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            processLock.Release();
            throw;
        }
    }

    private sealed class Lease : IAsyncDisposable
    {
        private FileStream? _stream;
        private SemaphoreSlim? _processLock;

        public Lease(FileStream stream, SemaphoreSlim processLock)
        {
            _stream = stream;
            _processLock = processLock;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _stream, null)?.Dispose();
            Interlocked.Exchange(ref _processLock, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
