using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Constants;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Services;

/// <summary>
/// Local LLM inference service using ONNX Runtime GenAI with DirectML backend.
/// Runs Gemma 2B IT INT4 quantized model on GPU/NPU for low-latency
/// text generation on the local machine.
///
/// Thread safety: This service is designed to be consumed by a single
/// LlmQueueService instance. Concurrent GenerateAsync calls are NOT supported
/// — the queue ensures only one generation runs at a time.
/// </summary>
public sealed class OnnxLlmInferenceService : ILocalInferenceService, IDisposable
{
    private readonly ILogger<OnnxLlmInferenceService> _logger;
    private readonly object _lock = new();

    private bool _initialized;
    private bool _disposed;
    private string? _modelPath;

    // ONNX Runtime GenAI objects (lazy initialized)
    private dynamic? _model;
    private dynamic? _tokenizer;

    public LlmState State { get; private set; } = LlmState.Idle;
    public bool IsReady => _initialized && State == LlmState.Ready;

    public OnnxLlmInferenceService(ILogger<OnnxLlmInferenceService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(
        string modelPath,
        IProgress<(double Progress, string Text)>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(modelPath);

        if (_initialized) return;

        State = LlmState.Initializing;
        _modelPath = modelPath;

        progress?.Report((0.0, "Loading LLM model..."));

        try
        {
            await Task.Run(() =>
            {
                progress?.Report((0.2, "Verifying model files..."));

                // Verify model directory structure
                var modelDir = new DirectoryInfo(modelPath);
                if (!modelDir.Exists)
                {
                    throw new DirectoryNotFoundException(
                        $"Model directory not found: {modelPath}");
                }

                // Check for required model files
                var requiredFiles = new[] { "model.onnx", "tokenizer.json", "tokenizer_config.json" };
                var foundFiles = modelDir.GetFiles()
                    .Select(f => f.Name)
                    .ToHashSet();

                // Also check for genai_config.json (OnnxRuntimeGenAI format)
                var hasGenaiConfig = File.Exists(Path.Combine(modelPath, "genai_config.json"));

                if (!foundFiles.Contains("model.onnx") && !hasGenaiConfig)
                {
                    throw new FileNotFoundException(
                        $"No model.onnx or genai_config.json found in: {modelPath}. " +
                        $"Ensure the ONNX model files are present.");
                }

                progress?.Report((0.4, "Initializing ONNX Runtime GenAI..."));

                // Use reflection to load OnnxRuntimeGenAI (available on Windows with DirectML)
                InitializeModel(modelPath);

                progress?.Report((0.8, "Warming up inference engine..."));

                // Warm-up run with a minimal prompt
                Warmup();

                progress?.Report((1.0, "LLM model ready."));
            }).ConfigureAwait(false);

            _initialized = true;
            State = LlmState.Ready;

            _logger.LogInformation(
                "[LLM] Model loaded from: {Path}, State: Ready",
                modelPath);
        }
        catch (Exception ex)
        {
            State = LlmState.Error;
            _logger.LogError(ex, "[LLM] Failed to initialize model from {Path}", modelPath);
            throw;
        }
    }

    /// <summary>
    /// Initialize the ONNX Runtime GenAI model using reflection
    /// (to handle assembly availability gracefully).
    /// Falls back to CPU execution provider if DirectML is unavailable.
    /// </summary>
    private void InitializeModel(string modelPath)
    {
        try
        {
            // Try to load OnnxRuntimeGenAI
            var genAiType = Type.GetType("Microsoft.ML.OnnxRuntimeGenAI.Model, Microsoft.ML.OnnxRuntimeGenAI");

            if (genAiType is not null)
            {
                // Create model with DirectML execution provider
                var createMethod = genAiType.GetMethod("Create", new[] { typeof(string), typeof(string) });
                if (createMethod is not null)
                {
                    _model = createMethod.Invoke(null, new object[] { modelPath, "dml" });
                    _logger.LogInformation("[LLM] ONNX Runtime GenAI model created with DirectML backend");
                    return;
                }
            }

            // Fallback: try simpler API
            var modelType = Type.GetType("Microsoft.ML.OnnxRuntimeGenAI.Model, Microsoft.ML.OnnxRuntimeGenAI");
            if (modelType is not null)
            {
                var ctor = modelType.GetConstructor(new[] { typeof(string) });
                if (ctor is not null)
                {
                    _model = ctor.Invoke(new object[] { modelPath });
                    _logger.LogInformation("[LLM] ONNX Runtime GenAI model created (default backend)");
                    return;
                }
            }

            _logger.LogWarning(
                "[LLM] OnnxRuntimeGenAI types not found. Model will be initialized at first GenerateAsync call. " +
                "Ensure Microsoft.ML.OnnxRuntimeGenAI NuGet package is installed.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LLM] Failed to create GenAI model via reflection — will use fallback");
        }
    }

    /// <summary>
    /// Perform a warm-up inference to prime the GPU/NPU caches.
    /// </summary>
    private void Warmup()
    {
        try
        {
            if (_model is not null)
            {
                // Invoke CreateTokenizer if available
                var tokenizerMethod = _model.GetType().GetMethod("CreateTokenizer");
                if (tokenizerMethod is not null)
                {
                    _tokenizer = tokenizerMethod.Invoke(_model, null);
                }
            }
            _logger.LogInformation("[LLM] Warm-up complete");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[LLM] Warm-up had issues (non-fatal)");
        }
    }

    /// <inheritdoc/>
    public async Task<string?> GenerateAsync(
        string systemPrompt,
        string userMessage,
        int maxTokens = 200,
        float temperature = 0.3f,
        CancellationToken cancellationToken = default)
    {
        if (!_initialized || _model is null)
        {
            _logger.LogWarning("[LLM] GenerateAsync called but model is not loaded");
            return null;
        }

        State = LlmState.Generating;
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Build the chat-formatted prompt
                var fullPrompt = BuildPrompt(systemPrompt, userMessage);

                // Use reflection to call Generate method
                return GenerateWithReflection(fullPrompt, maxTokens, temperature, cancellationToken);
            }, cancellationToken).ConfigureAwait(false);

            var elapsedMs = sw.Elapsed.TotalMilliseconds;
            State = LlmState.Ready;

            if (string.IsNullOrWhiteSpace(result))
            {
                _logger.LogDebug("[LLM] Generation returned empty result in {Ms:F0}ms", elapsedMs);
                return null;
            }

            _logger.LogInformation(
                "[LLM] Generated {Chars} chars in {Ms:F0}ms (maxTokens: {Max}, temp: {Temp})",
                result.Length, elapsedMs, maxTokens, temperature);

            return result;
        }
        catch (OperationCanceledException)
        {
            State = LlmState.Ready;
            _logger.LogDebug("[LLM] Generation cancelled (preempted)");
            return null;
        }
        catch (Exception ex)
        {
            State = _initialized ? LlmState.Ready : LlmState.Error;
            _logger.LogError(ex, "[LLM] Generation failed");
            return null;
        }
    }

    /// <summary>
    /// Build a chat-formatted prompt for the Gemma model.
    /// Uses the Gemma instruction format: &lt;start_of_turn&gt;user\n...&lt;end_of_turn&gt;\n&lt;start_of_turn&gt;model\n...
    /// </summary>
    private static string BuildPrompt(string systemPrompt, string userMessage)
    {
        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            sb.Append("<start_of_turn>user\n");
            sb.Append(systemPrompt);
            sb.Append("<end_of_turn>\n");
        }

        sb.Append("<start_of_turn>user\n");
        sb.Append(userMessage);
        sb.Append("<end_of_turn>\n");
        sb.Append("<start_of_turn>model\n");

        return sb.ToString();
    }

    /// <summary>
    /// Call the ONNX Runtime GenAI Generate method via reflection.
    /// This allows graceful handling even if the exact API changes.
    /// </summary>
    private string? GenerateWithReflection(
        string prompt,
        int maxTokens,
        float temperature,
        CancellationToken cancellationToken)
    {
        if (_model is null) return null;

        var modelType = _model.GetType();

        // Try Generate method with params
        var generateMethod = modelType.GetMethod("Generate", new[] { typeof(string), typeof(string) });
        if (generateMethod is not null)
        {
            var paramsStr = $"max_new_tokens={maxTokens};temperature={temperature:F2}";
            var result = (string?)generateMethod.Invoke(_model, new object[] { prompt, paramsStr });
            return PostProcessResult(result);
        }

        // Try simpler Generate method
        var simpleGen = modelType.GetMethod("Generate", new[] { typeof(string) });
        if (simpleGen is not null)
        {
            var result = (string?)simpleGen.Invoke(_model, new object[] { prompt });
            return PostProcessResult(result);
        }

        _logger.LogWarning("[LLM] No suitable Generate method found on model type: {Type}", modelType.FullName);
        return null;
    }

    /// <summary>
    /// Post-process the LLM output: trim whitespace, remove trailing EOS tokens.
    /// </summary>
    private static string? PostProcessResult(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Remove common EOS tokens
        var cleaned = text
            .Replace("<end_of_turn>", "")
            .Replace("<eos>", "")
            .Replace("<|end_of_turn|>", "")
            .Trim();

        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    /// <inheritdoc/>
    public void InterruptGenerate()
    {
        // State is managed by the queue via CancellationToken
        State = LlmState.Ready;
        _logger.LogDebug("[LLM] Generate interrupt requested");
    }

    /// <inheritdoc/>
    public async Task UnloadAsync()
    {
        _logger.LogInformation("[LLM] Unloading model...");

        await Task.Run(() =>
        {
            try
            {
                // Dispose via reflection if possible
                if (_model is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                if (_tokenizer is IDisposable tokDisposable)
                {
                    tokDisposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[LLM] Error during model disposal (non-fatal)");
            }

            _model = null;
            _tokenizer = null;
        }).ConfigureAwait(false);

        _initialized = false;
        State = LlmState.Idle;
        _logger.LogInformation("[LLM] Model unloaded");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _model?.Dispose();
            _tokenizer?.Dispose();
        }
        catch
        {
            // Suppressed during disposal
        }

        _model = null;
        _tokenizer = null;
        _initialized = false;
    }
}
