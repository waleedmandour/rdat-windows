using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace RDAT.Copilot.Infrastructure.Onnx;

/// <summary>
/// Manages the lifetime of unmanaged ONNX GenAI objects (Model, Tokenizer).
/// Ensures correct disposal order and provides idle-timeout VRAM release.
/// </summary>
internal sealed class ModelLifetimeScope : IAsyncDisposable
{
    private Model? _model;
    private Tokenizer? _tokenizer;

    private readonly ReaderWriterLockSlim _loadLock = new();
    private Timer? _idleTimer;
    private readonly TimeSpan _idleTimeout = TimeSpan.FromMinutes(30);

    public ModelLifetimeScope()
    {
    }

    public async ValueTask LoadAsync(string modelDirectory, CancellationToken ct)
    {
        _loadLock.EnterWriteLock();
        try
        {
            ReleaseUnmanagedObjects();

            _model = await Task.Run(() => new Model(modelDirectory), ct);
            _tokenizer = await Task.Run(() => new Tokenizer(_model), ct);

            ResetIdleTimer();
        }
        finally
        {
            _loadLock.ExitWriteLock();
        }
    }

    public Model AcquireModel()
    {
        _loadLock.EnterReadLock();
        return _model ?? throw new InvalidOperationException("Model is not loaded.");
    }

    public Tokenizer AcquireTokenizer()
    {
        return _tokenizer ?? throw new InvalidOperationException("Tokenizer is not loaded. Call LoadAsync first.");
    }

    public void AcquireLock() => _loadLock.EnterReadLock();

    public void ReleaseInferenceHandle() => _loadLock.ExitReadLock();

    private void ResetIdleTimer()
    {
        _idleTimer?.Dispose();
        _idleTimer = new Timer(
            _ => OnIdleTimeout(),
            null,
            _idleTimeout,
            Timeout.InfiniteTimeSpan);
    }

    private void OnIdleTimeout()
    {
        _loadLock.EnterWriteLock();
        try { ReleaseUnmanagedObjects(); }
        finally { _loadLock.ExitWriteLock(); }
    }

    private void ReleaseUnmanagedObjects()
    {
        Interlocked.Exchange(ref _tokenizer, null)?.Dispose();
        Interlocked.Exchange(ref _model, null)?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _idleTimer?.Dispose();
        _loadLock.EnterWriteLock();
        try { ReleaseUnmanagedObjects(); }
        finally
        {
            _loadLock.ExitWriteLock();
            _loadLock.Dispose();
        }
        await ValueTask.CompletedTask;
    }
}
