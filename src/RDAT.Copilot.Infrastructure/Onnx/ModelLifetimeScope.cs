using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OgaConfig = Microsoft.ML.OnnxRuntimeGenAI.Config;
using OgaModel = Microsoft.ML.OnnxRuntimeGenAI.Model;
using OgaTokenizer = Microsoft.ML.OnnxRuntimeGenAI.Tokenizer;

namespace RDAT.Copilot.Infrastructure.Onnx;

// Problem: OnnxRuntimeGenAI creates three unmanaged objects:
//   Config → Model → Tokenizer
//   Each holds native memory. Dispose order MUST be reversed:
//   Tokenizer.Dispose() → Model.Dispose() → Config.Dispose()
//
// Failure modes prevented:
//   1. OOM crash after 6+ hours: solved by IdleTimeout auto-release.
//   2. Double-dispose crash: solved by Interlocked.Exchange null-check.
//   3. Use-after-free: solved by the _loadLock ReaderWriterLockSlim.

internal sealed class ModelLifetimeScope : IAsyncDisposable
{
    private OgaConfig? _config;
    private OgaModel? _model;
    private OgaTokenizer? _tokenizer;

    private readonly ReaderWriterLockSlim _loadLock = new();
    private Timer? _idleTimer;
    private readonly TimeSpan _idleTimeout = TimeSpan.FromMinutes(30);
    private readonly ILogger<ModelLifetimeScope> _logger;

    public ModelLifetimeScope(ILogger<ModelLifetimeScope> logger)
    {
        _logger = logger;
    }

    public async ValueTask LoadAsync(string modelDirectory, CancellationToken ct)
    {
        _loadLock.EnterWriteLock();
        try
        {
            // Release any previously loaded model first
            ReleaseUnmanagedObjects();

            // Load in required order: Config → Model → Tokenizer
            _config    = await Task.Run(() => new OgaConfig(modelDirectory), ct);
            _model     = await Task.Run(() => new OgaModel(_config), ct);
            _tokenizer = await Task.Run(() => new OgaTokenizer(_model), ct);

            _logger.LogInformation(
                "[ModelScope] Model loaded from {Dir}. ONNX objects live.", modelDirectory);

            ResetIdleTimer();
        }
        finally
        {
            _loadLock.ExitWriteLock();
        }
    }

    public OgaModel AcquireModel()
    {
        _loadLock.EnterReadLock();
        return _model ?? throw new InvalidOperationException("Model is not loaded.");
    }
    
    public OgaTokenizer AcquireTokenizer()
    {
        // Typically called alongside the Model, so we could return a tuple or similar.
        // It requires _loadLock is held. We are holding it via an explicit lock acquisition strategy if we split these.
        // For simplicity, we just provide an explicit lock mechanism on the Scope.
        return _tokenizer ?? throw new InvalidOperationException("Model is not loaded. Call LoadAsync before inference.");
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
        _logger.LogInformation(
            "[ModelScope] Idle timeout reached. Releasing ONNX model from VRAM.");
        _loadLock.EnterWriteLock();
        try { ReleaseUnmanagedObjects(); }
        finally { _loadLock.ExitWriteLock(); }
    }

    private void ReleaseUnmanagedObjects()
    {
        Interlocked.Exchange(ref _tokenizer, null)?.Dispose();
        Interlocked.Exchange(ref _model,     null)?.Dispose();
        Interlocked.Exchange(ref _config,    null)?.Dispose();

        _logger.LogInformation("[ModelScope] ONNX objects disposed. VRAM released.");
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
